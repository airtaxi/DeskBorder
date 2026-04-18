using DeskBorder.Dialogs;
using DeskBorder.Helpers;
using DeskBorder.Models;
using DeskBorder.Services;
using DeskBorder.ViewModels;
using DeskBorder.Views;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using System.IO;
using System.Threading;
using Windows.Storage;
using WinRT.Interop;

namespace DeskBorder.Pages;

public sealed partial class SettingsPage : Page
{
    private const string SettingsFileExtension = ".dbs";
    private const string SettingsSuggestedFileName = "DeskBorder_Settings";
    private static readonly TimeSpan s_infoBarAutoHideDelay = TimeSpan.FromSeconds(4);

    private readonly DispatcherQueueTimer _settingsImportExportInfoBarAutoHideTimer;
    private readonly IDeskBorderRuntimeService _deskBorderRuntimeService;
    private readonly IHotkeyService _hotkeyService;
    private readonly ILocalizationService _localizationService;
    private readonly ManageWindow _manageWindow;
    private readonly DispatcherQueueTimer _settingsStatusInfoBarAutoHideTimer;
    private readonly ISettingsService _settingsService;
    private readonly SemaphoreSlim _settingsUpdateSemaphore = new(1, 1);
    private TeachingTip? _activeSectionTeachingTip;
    private bool _isInitialSettingsLoadCompleted;
    private bool _isNavigatorTriggerAreaSelectionInProgress;
    private bool _isSynchronizingViewModel;
    private bool _isSettingsTransferInProgress;

    public SettingsPageViewModel ViewModel { get; } = new();

    public SettingsPage()
    {
        InitializeComponent();

        _deskBorderRuntimeService = App.GetRequiredService<IDeskBorderRuntimeService>();
        _settingsImportExportInfoBarAutoHideTimer = CreateInfoBarAutoHideTimer(SettingsImportExportInfoBar);
        _hotkeyService = App.GetRequiredService<IHotkeyService>();
        _localizationService = App.GetRequiredService<ILocalizationService>();
        _manageWindow = App.GetRequiredService<ManageWindow>();
        _settingsStatusInfoBarAutoHideTimer = CreateInfoBarAutoHideTimer(SettingsStatusInfoBar);
        _settingsService = App.GetRequiredService<ISettingsService>();
        _hotkeyService.RegistrationStateChanged += OnHotkeyServiceRegistrationStateChanged;
        _settingsService.SettingsChanged += OnSettingsServiceSettingsChanged;
        Unloaded += OnSettingsPageUnloaded;
        _ = LoadSettingsAsync();
    }

    private void ApplySettingsToViewModel()
    {
        _isSynchronizingViewModel = true;
        ViewModel.Load(_settingsService.Settings);
        ApplyHotkeyRegistrationState();
        _isInitialSettingsLoadCompleted = true;
        if (DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _isSynchronizingViewModel = false))
            return;

        _isSynchronizingViewModel = false;
    }

    private void ClearSettingsStatus() => SettingsStatusInfoBar.IsOpen = false;

    private DispatcherQueueTimer CreateInfoBarAutoHideTimer(InfoBar infoBar)
    {
        var infoBarAutoHideTimer = DispatcherQueue.CreateTimer();
        infoBarAutoHideTimer.Interval = s_infoBarAutoHideDelay;
        infoBarAutoHideTimer.IsRepeating = false;
        infoBarAutoHideTimer.Tick += (_, _) =>
        {
            infoBarAutoHideTimer.Stop();
            infoBar.IsOpen = false;
        };
        return infoBarAutoHideTimer;
    }

#pragma warning disable CA1822 // Mark members as static => Used on XAML bindings
    private Brush GetHotkeyLabelForegroundBrush(bool isEnabled) => (Brush)Application.Current.Resources[isEnabled ? "TextFillColorPrimaryBrush" : "TextFillColorDisabledBrush"];

    private Brush GetKeyboardShortcutValidationForegroundBrush(KeyboardShortcutValidationState keyboardShortcutValidationState) => (Brush)Application.Current.Resources[keyboardShortcutValidationState switch
    {
        KeyboardShortcutValidationState.Valid => "SystemFillColorSuccessBrush",
        KeyboardShortcutValidationState.MissingKey or KeyboardShortcutValidationState.Duplicate or KeyboardShortcutValidationState.RegistrationFailed => "SystemFillColorCriticalBrush",
        _ => "TextFillColorSecondaryBrush"
    }];

    private string GetKeyboardShortcutValidationText(KeyboardShortcutValidationState keyboardShortcutValidationState) => LocalizedResourceAccessor.GetString(keyboardShortcutValidationState switch
    {
        KeyboardShortcutValidationState.Valid => "Settings.HotkeyValidation.Valid",
        KeyboardShortcutValidationState.MissingKey => "Settings.HotkeyValidation.MissingKey",
        KeyboardShortcutValidationState.Duplicate => "Settings.HotkeyValidation.Duplicate",
        KeyboardShortcutValidationState.RegistrationFailed => "Settings.HotkeyValidation.RegistrationFailed",
        _ => "Settings.HotkeyValidation.Disabled"
    });

    private Visibility GetAutoDeleteWarningTimeoutVisibility(bool isAutoDeleteEnabled, bool isAutoDeleteWarningEnabled) => isAutoDeleteEnabled && isAutoDeleteWarningEnabled
        ? Visibility.Visible
        : Visibility.Collapsed;
#pragma warning restore CA1822 // Mark members as static => Used on XAML bindings

    private void ApplyHotkeyRegistrationState()
    {
        ViewModel.UpdateHotkeyRegistrationFailureMessage(HotkeyActionType.ToggleDeskBorderEnabled, _hotkeyService.GetRegistrationFailureMessage(HotkeyActionType.ToggleDeskBorderEnabled));
        ViewModel.UpdateHotkeyRegistrationFailureMessage(HotkeyActionType.MoveFocusedWindowToPreviousDesktop, _hotkeyService.GetRegistrationFailureMessage(HotkeyActionType.MoveFocusedWindowToPreviousDesktop));
        ViewModel.UpdateHotkeyRegistrationFailureMessage(HotkeyActionType.MoveFocusedWindowToNextDesktop, _hotkeyService.GetRegistrationFailureMessage(HotkeyActionType.MoveFocusedWindowToNextDesktop));
        ViewModel.UpdateHotkeyRegistrationFailureMessage(HotkeyActionType.ToggleNavigator, _hotkeyService.GetRegistrationFailureMessage(HotkeyActionType.ToggleNavigator));
    }

    private nint GetManageWindowHandle() => WindowNative.GetWindowHandle(_manageWindow);

    private IReadOnlyList<string> GetAvailableForegroundProcessNames()
    {
        var blacklistedProcessNameSet = ViewModel.BlacklistedProcessNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var availableForegroundProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var runningProcess in Process.GetProcesses())
        {
            using (runningProcess)
            {
                if (runningProcess.Id == Environment.ProcessId)
                    continue;

                var processName = TryGetForegroundProcessName(runningProcess);
                if (string.IsNullOrWhiteSpace(processName) || blacklistedProcessNameSet.Contains(processName))
                    continue;

                _ = availableForegroundProcessNames.Add(processName);
            }
        }

        return [.. availableForegroundProcessNames.Order(StringComparer.OrdinalIgnoreCase)];
    }

    private async Task ImportSettingsAsync()
    {
        if (_isSettingsTransferInProgress)
            return;

        SetSettingsTransferInProgress(true);
        try
        {
            var file = await PickImportSettingsFileAsync();
            if (file is null)
            {
                ShowSettingsImportExportResult(
                    LocalizedResourceAccessor.GetString("Settings.Import.CancelledTitle"),
                    LocalizedResourceAccessor.GetString("Settings.Import.CancelledMessage"),
                    InfoBarSeverity.Informational);
                return;
            }

            await _settingsService.ImportAsync(file.Path);
            ApplySettingsToViewModel();
            ShowSettingsImportExportResult(
                LocalizedResourceAccessor.GetString("Settings.Import.SuccessTitle"),
                LocalizedResourceAccessor.GetFormattedString("Settings.Import.SuccessMessageFormat", Path.GetFileName(file.Path)),
                InfoBarSeverity.Success);
        }
        catch (ArgumentException exception)
        {
            ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Import.FailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        catch (InvalidOperationException exception)
        {
            ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Import.FailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        catch (IOException exception)
        {
            ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Import.FailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Import.FailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        finally { SetSettingsTransferInProgress(false); }
    }

    private async Task LoadSettingsAsync()
    {
        await _settingsService.RefreshLaunchOnStartupEnabledAsync();
        ApplySettingsToViewModel();
    }

    private async void OnAddBlacklistedProcessNameButtonClicked(object sender, RoutedEventArgs routedEventArgs)
    {
        _ = sender;
        _ = routedEventArgs;
        await ShowForegroundProcessSelectionDialogAsync();
    }

    private async void OnExportSettingsButtonClicked(object sender, RoutedEventArgs routedEventArgs) => await ExportSettingsAsync();

    private async void OnImportSettingsButtonClicked(object sender, RoutedEventArgs routedEventArgs) => await ImportSettingsAsync();

    private async void OnSelectNavigatorTriggerAreaButtonClicked(object sender, RoutedEventArgs routedEventArgs)
    {
        _ = sender;
        _ = routedEventArgs;
        await SelectNavigatorTriggerAreaAsync();
    }

    private void OnHotkeyServiceRegistrationStateChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (DispatcherQueue.TryEnqueue(ApplyHotkeyRegistrationState))
            return;

        ApplyHotkeyRegistrationState();
    }

    private void OnModifierSelectionCheckBoxClicked(object sender, RoutedEventArgs routedEventArgs) => QueueSettingsSave();

    private async void OnRemoveBlacklistedProcessNameButtonClicked(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is not Button { Tag: string blacklistedProcessName })
            return;

        if (!ViewModel.RemoveBlacklistedProcessName(blacklistedProcessName))
            return;

        await SaveSettingsAsync();
    }

    private void OnSectionHelpButtonClicked(object sender, RoutedEventArgs routedEventArgs)
    {
        _ = routedEventArgs;
        if (sender is not Button { Tag: TeachingTip targetTeachingTip })
            return;

        if (ReferenceEquals(_activeSectionTeachingTip, targetTeachingTip) && targetTeachingTip.IsOpen)
        {
            targetTeachingTip.IsOpen = false;
            _activeSectionTeachingTip = null;
            return;
        }

        _activeSectionTeachingTip?.IsOpen = false;

        targetTeachingTip.IsOpen = true;
        _activeSectionTeachingTip = targetTeachingTip;
    }

    private void OnSettingSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs) => QueueSettingsSave();

    private void OnSettingToggleSwitchToggled(object sender, RoutedEventArgs routedEventArgs) => QueueSettingsSave();

    private void OnSettingsPageUnloaded(object sender, RoutedEventArgs routedEventArgs)
    {
        _settingsImportExportInfoBarAutoHideTimer.Stop();
        _hotkeyService.RegistrationStateChanged -= OnHotkeyServiceRegistrationStateChanged;
        _settingsService.SettingsChanged -= OnSettingsServiceSettingsChanged;
        _settingsStatusInfoBarAutoHideTimer.Stop();
        Unloaded -= OnSettingsPageUnloaded;
    }

    private void OnSectionTeachingTipClosed(TeachingTip sender, TeachingTipClosedEventArgs eventArguments)
    {
        _ = eventArguments;
        if (ReferenceEquals(_activeSectionTeachingTip, sender))
            _activeSectionTeachingTip = null;
    }

    private void OnSettingsServiceSettingsChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (DispatcherQueue.TryEnqueue(ApplySettingsToViewModel))
            return;

        ApplySettingsToViewModel();
    }

    private void OnSettingNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs eventArguments) => QueueSettingsSave();

    private async Task<PickFileResult> PickExportSettingsFileAsync()
    {
        var fileSavePicker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        var extensions = new List<string> { SettingsFileExtension }; // AOT Workaround : CCW doesn't support IList<string> marshaling
        fileSavePicker.FileTypeChoices.Add(LocalizedResourceAccessor.GetString("Settings.Export.FileTypeDisplayName"), extensions);
        fileSavePicker.DefaultFileExtension = SettingsFileExtension;
        fileSavePicker.SuggestedFileName = SettingsSuggestedFileName;
        fileSavePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        return await fileSavePicker.PickSaveFileAsync();
    }

    private async Task<PickFileResult> PickImportSettingsFileAsync()
    {
        var fileOpenPicker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId) { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        fileOpenPicker.FileTypeFilter.Add(SettingsFileExtension);

        return await fileOpenPicker.PickSingleFileAsync();
    }

    private async Task ShowForegroundProcessSelectionDialogAsync()
    {
        var availableForegroundProcessNames = GetAvailableForegroundProcessNames();
        if (availableForegroundProcessNames.Count == 0)
        {
            ShowSettingsStatus(
                LocalizedResourceAccessor.GetString("Settings.Blacklist.NoAvailableForegroundProcessesTitle"),
                LocalizedResourceAccessor.GetString("Settings.Blacklist.NoAvailableForegroundProcessesMessage"),
                InfoBarSeverity.Informational);
            return;
        }

        var foregroundProcessSelectionDialog = new ForegroundProcessSelectionDialog(availableForegroundProcessNames)
        {
            XamlRoot = XamlRoot
        };
        if (await foregroundProcessSelectionDialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        if (!ViewModel.AddBlacklistedProcessNames(foregroundProcessSelectionDialog.SelectedProcessNames))
            return;

        await SaveSettingsAsync();
    }

    private async Task ExportSettingsAsync()
    {
        if (_isSettingsTransferInProgress)
            return;

        SetSettingsTransferInProgress(true);
        try
        {
            var result = await PickExportSettingsFileAsync();
            var file = result;
            if (file is null)
            {
                ShowSettingsImportExportResult(
                    LocalizedResourceAccessor.GetString("Settings.Export.CancelledTitle"),
                    LocalizedResourceAccessor.GetString("Settings.Export.CancelledMessage"),
                    InfoBarSeverity.Informational);
                return;
            }

            await _settingsService.ExportAsync(file.Path);
            ShowSettingsImportExportResult(
                LocalizedResourceAccessor.GetString("Settings.Export.SuccessTitle"),
                LocalizedResourceAccessor.GetFormattedString("Settings.Export.SuccessMessageFormat", Path.GetFileName(file.Path)),
                InfoBarSeverity.Success);
        }
        catch (ArgumentException exception)
        {
            ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Export.FailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        catch (InvalidOperationException exception)
        {
            ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Export.FailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        catch (IOException exception)
        {
            ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Export.FailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Export.FailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        finally { SetSettingsTransferInProgress(false); }
    }

    private void QueueSettingsSave()
    {
        if (DispatcherQueue.TryEnqueue(async () => await SaveSettingsAsync()))
            return;

        _ = SaveSettingsAsync();
    }

    private static void RestartInfoBarAutoHideTimer(DispatcherQueueTimer infoBarAutoHideTimer)
    {
        infoBarAutoHideTimer.Stop();
        infoBarAutoHideTimer.Start();
    }

    private async Task SaveSettingsAsync()
    {
        if (!_isInitialSettingsLoadCompleted || _isSynchronizingViewModel || _isSettingsTransferInProgress)
            return;

        await _settingsUpdateSemaphore.WaitAsync();
        try
        {
            await _settingsService.UpdateSettingsAsync(ViewModel.CreateSettings());
            ClearSettingsStatus();
        }
        catch (ArgumentException exception)
        {
            ApplySettingsToViewModel();
            ShowSettingsStatus(LocalizedResourceAccessor.GetString("Settings.Status.ApplyFailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        catch (InvalidOperationException exception)
        {
            ApplySettingsToViewModel();
            ShowSettingsStatus(LocalizedResourceAccessor.GetString("Settings.Status.ApplyFailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        finally { _settingsUpdateSemaphore.Release(); }
    }

    private async Task SelectNavigatorTriggerAreaAsync()
    {
        if (_isNavigatorTriggerAreaSelectionInProgress)
            return;

        _isNavigatorTriggerAreaSelectionInProgress = true;
        SelectNavigatorTriggerAreaButton.IsEnabled = false;

        try
        {
            await using var runtimeSuspension = await _deskBorderRuntimeService.CreateSuspensionAsync();
            var targetDisplayMonitor = MouseHelper.GetDisplayMonitorFromWindow(GetManageWindowHandle());
            var navigatorTriggerAreaSelectionWindow = new NavigatorTriggerAreaSelectionWindow(_localizationService, targetDisplayMonitor);
            var selectedTriggerRectangleSettings = await navigatorTriggerAreaSelectionWindow.ShowSelectionAsync();
            if (selectedTriggerRectangleSettings is null)
                return;

            ViewModel.SetNavigatorTriggerRectangle(selectedTriggerRectangleSettings);
            await SaveSettingsAsync();
        }
        catch (InvalidOperationException exception)
        {
            ShowSettingsStatus(LocalizedResourceAccessor.GetString("Settings.Status.ApplyFailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SelectNavigatorTriggerAreaButton.IsEnabled = true;
            _isNavigatorTriggerAreaSelectionInProgress = false;
        }
    }

    private void SetSettingsTransferInProgress(bool isSettingsTransferInProgress)
    {
        _isSettingsTransferInProgress = isSettingsTransferInProgress;
        ExportSettingsButton.IsEnabled = !isSettingsTransferInProgress;
        ImportSettingsButton.IsEnabled = !isSettingsTransferInProgress;
    }

    private static void ShowInfoBar(InfoBar infoBar, DispatcherQueueTimer infoBarAutoHideTimer, string title, string message, InfoBarSeverity infoBarSeverity)
    {
        infoBar.Title = title;
        infoBar.Message = message;
        infoBar.Severity = infoBarSeverity;
        infoBar.IsOpen = true;
        RestartInfoBarAutoHideTimer(infoBarAutoHideTimer);
    }

    private void ShowSettingsImportExportResult(string title, string message, InfoBarSeverity infoBarSeverity) => ShowInfoBar(SettingsImportExportInfoBar, _settingsImportExportInfoBarAutoHideTimer, title, message, infoBarSeverity);

    private void ShowSettingsStatus(string title, string message, InfoBarSeverity infoBarSeverity) => ShowInfoBar(SettingsStatusInfoBar, _settingsStatusInfoBarAutoHideTimer, title, message, infoBarSeverity);

    private static string? TryGetForegroundProcessName(Process runningProcess)
    {
        try
        {
            if (runningProcess.HasExited || runningProcess.MainWindowHandle == 0)
                return null;

            var processName = runningProcess.ProcessName.Trim();
            return string.IsNullOrWhiteSpace(processName) ? null : processName;
        }
        catch (InvalidOperationException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (Win32Exception) { return null; }
    }
}
