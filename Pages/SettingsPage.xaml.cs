using DeskBorder.Dialogs;
using DeskBorder.Helpers;
using DeskBorder.Models;
using DeskBorder.Services;
using DeskBorder.ViewModels;
using DeskBorder.Views;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel;
using WinRT.Interop;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace DeskBorder.Pages;

public sealed partial class SettingsPage : Page
{
    private const KeyboardModifierKeys WindowsOnlyKeyboardModifierKeys = KeyboardModifierKeys.Windows;
    private const string LogFileExtension = ".txt";
    private const string LogSuggestedFileNamePrefix = "DeskBorder_Logs";
    private const string CreateDesktopModifierSelectionTag = "CreateDesktopModifierSelection";
    private const string SwitchDesktopWhileMouseButtonsArePressedModifierSelectionTag = "SwitchDesktopWhileMouseButtonsArePressedModifierSelection";
    private const string SwitchToNextDesktopHotkeyEditorTag = "SwitchToNextDesktopHotkeyEditor";
    private const string SwitchToPreviousDesktopHotkeyEditorTag = "SwitchToPreviousDesktopHotkeyEditor";
    private const string MoveFocusedWindowToNextDesktopHotkeyEditorTag = "MoveFocusedWindowToNextDesktopHotkeyEditor";
    private const string MoveFocusedWindowToPreviousDesktopHotkeyEditorTag = "MoveFocusedWindowToPreviousDesktopHotkeyEditor";
    private const string NavigatorToggleHotkeyEditorTag = "NavigatorToggleHotkeyEditor";
    private const string SettingsFileExtension = ".dbs";
    private const string SettingsSuggestedFileName = "DeskBorder_Settings";
    private const string SwitchDesktopModifierSelectionTag = "SwitchDesktopModifierSelection";
    private const string ToggleDeskBorderEnabledHotkeyEditorTag = "ToggleDeskBorderEnabledHotkeyEditor";
    private static readonly TimeSpan s_infoBarAutoHideDelay = TimeSpan.FromSeconds(4);
    private static bool s_shouldShowPendingAlwaysRunAsAdministratorImportExcludedStatus;
    private static bool s_shouldShowPendingLanguageRestartRecommendedStatus;

    private readonly DispatcherQueueTimer _developerLogInfoBarAutoHideTimer;
    private readonly DispatcherQueueTimer _settingsImportExportInfoBarAutoHideTimer;
    private readonly IDeskBorderRuntimeService _deskBorderRuntimeService;
    private readonly IFileLogService _fileLogService;
    private readonly IHotkeyService _hotkeyService;
    private readonly ILocalizationService _localizationService;
    private readonly ManageWindow _manageWindow;
    private readonly DispatcherQueueTimer _settingsStatusInfoBarAutoHideTimer;
    private readonly ISettingsService _settingsService;
    private readonly IStoreUpdateService _storeUpdateService;
    private readonly IThemeService _themeService;
    private readonly SemaphoreSlim _settingsUpdateSemaphore = new(1, 1);
    private TeachingTip? _activeSectionTeachingTip;
    private bool _isInitialSettingsLoadCompleted;
    private bool _isLogExportInProgress;
    private bool _isNavigatorTriggerAreaSelectionInProgress;
    private bool _isSynchronizingViewModel;
    private bool _isSettingsTransferInProgress;
    private string _storeUpdateStatusResourceName = "Settings.StoreUpdate.Status.Default";

    public SettingsPageViewModel ViewModel { get; } = new();

    public SettingsPage()
    {
        InitializeComponent();

        _deskBorderRuntimeService = App.GetRequiredService<IDeskBorderRuntimeService>();
        _developerLogInfoBarAutoHideTimer = CreateInfoBarAutoHideTimer(DeveloperLogInfoBar);
        _fileLogService = App.GetRequiredService<IFileLogService>();
        _settingsImportExportInfoBarAutoHideTimer = CreateInfoBarAutoHideTimer(SettingsImportExportInfoBar);
        _hotkeyService = App.GetRequiredService<IHotkeyService>();
        _localizationService = App.GetRequiredService<ILocalizationService>();
        _manageWindow = App.GetRequiredService<ManageWindow>();
        _settingsStatusInfoBarAutoHideTimer = CreateInfoBarAutoHideTimer(SettingsStatusInfoBar);
        _settingsService = App.GetRequiredService<ISettingsService>();
        _storeUpdateService = App.GetRequiredService<IStoreUpdateService>();
        _themeService = App.GetRequiredService<IThemeService>();
        _hotkeyService.RegistrationStateChanged += OnHotkeyServiceRegistrationStateChanged;
        _localizationService.LanguageChanged += OnLocalizationServiceLanguageChanged;
        _settingsService.SettingsChanged += OnSettingsServiceSettingsChanged;
        RefreshStoreUpdateVisualState();
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
        KeyboardShortcutValidationState.MissingKey or KeyboardShortcutValidationState.ReservedByWindowsDesktopSwitch or KeyboardShortcutValidationState.Duplicate or KeyboardShortcutValidationState.RegistrationFailed => "SystemFillColorCriticalBrush",
        _ => "TextFillColorSecondaryBrush"
    }];

    private string GetKeyboardShortcutValidationText(KeyboardShortcutValidationState keyboardShortcutValidationState) => LocalizedResourceAccessor.GetString(keyboardShortcutValidationState switch
    {
        KeyboardShortcutValidationState.Valid => "Settings.HotkeyValidation.Valid",
        KeyboardShortcutValidationState.MissingKey => "Settings.HotkeyValidation.MissingKey",
        KeyboardShortcutValidationState.ReservedByWindowsDesktopSwitch => "Settings.HotkeyValidation.ReservedByWindowsDesktopSwitch",
        KeyboardShortcutValidationState.Duplicate => "Settings.HotkeyValidation.Duplicate",
        KeyboardShortcutValidationState.RegistrationFailed => "Settings.HotkeyValidation.RegistrationFailed",
        _ => "Settings.HotkeyValidation.Disabled"
    });

    private Visibility GetAutoDeleteCompletionToastVisibility(bool isAutoDeleteWarningEnabled) => !isAutoDeleteWarningEnabled
        ? Visibility.Visible
        : Visibility.Collapsed;

    private Visibility GetAutoDeleteOptionControlsVisibility(bool isAutoDeleteEnabled) => isAutoDeleteEnabled
        ? Visibility.Visible
        : Visibility.Collapsed;

    private Visibility GetAutoDeleteWarningTimeoutVisibility(bool isAutoDeleteWarningEnabled) => isAutoDeleteWarningEnabled
        ? Visibility.Visible
        : Visibility.Collapsed;

    private Visibility GetDesktopEdgeAdditionalTriggerDistanceVisibility(bool isDesktopEdgeAdditionalTriggerDistanceEnabled) => isDesktopEdgeAdditionalTriggerDistanceEnabled
        ? Visibility.Visible
        : Visibility.Collapsed;

    private Visibility GetDesktopCreationOptionControlsVisibility(bool isDesktopCreationEnabled) => isDesktopCreationEnabled
        ? Visibility.Visible
        : Visibility.Collapsed;

    private Visibility GetFullscreenOptionControlsVisibility(bool isDesktopSwitchingAndCreationDisabledWhenForegroundWindowIsFullscreen) => isDesktopSwitchingAndCreationDisabledWhenForegroundWindowIsFullscreen
        ? Visibility.Visible
        : Visibility.Collapsed;

    private bool GetMultiDisplayBehaviorSelectionEnabled(bool isVerticalDesktopSwitchingEnabled) => !isVerticalDesktopSwitchingEnabled;

    private Visibility GetVerticalDesktopSwitchingOptionsVisibility(bool areVerticalDesktopSwitchingOptionControlsVisible) => areVerticalDesktopSwitchingOptionControlsVisible
        ? Visibility.Visible
        : Visibility.Collapsed;
#pragma warning restore CA1822 // Mark members as static => Used on XAML bindings

    private void ApplyHotkeyRegistrationState()
    {
        ViewModel.UpdateHotkeyRegistrationFailureMessage(HotkeyActionType.ToggleDeskBorderEnabled, _hotkeyService.GetRegistrationFailureMessage(HotkeyActionType.ToggleDeskBorderEnabled));
        ViewModel.UpdateHotkeyRegistrationFailureMessage(HotkeyActionType.SwitchToPreviousDesktop, _hotkeyService.GetRegistrationFailureMessage(HotkeyActionType.SwitchToPreviousDesktop));
        ViewModel.UpdateHotkeyRegistrationFailureMessage(HotkeyActionType.SwitchToNextDesktop, _hotkeyService.GetRegistrationFailureMessage(HotkeyActionType.SwitchToNextDesktop));
        ViewModel.UpdateHotkeyRegistrationFailureMessage(HotkeyActionType.MoveFocusedWindowToPreviousDesktop, _hotkeyService.GetRegistrationFailureMessage(HotkeyActionType.MoveFocusedWindowToPreviousDesktop));
        ViewModel.UpdateHotkeyRegistrationFailureMessage(HotkeyActionType.MoveFocusedWindowToNextDesktop, _hotkeyService.GetRegistrationFailureMessage(HotkeyActionType.MoveFocusedWindowToNextDesktop));
        ViewModel.UpdateHotkeyRegistrationFailureMessage(HotkeyActionType.ToggleNavigator, _hotkeyService.GetRegistrationFailureMessage(HotkeyActionType.ToggleNavigator));
    }

    private static ModifierKeySelectionViewModel? GetModifierSelectionViewModel(SettingsPageViewModel settingsPageViewModel, string modifierSelectionTag) => modifierSelectionTag switch
    {
        SwitchDesktopModifierSelectionTag => settingsPageViewModel.SwitchDesktopModifierSelection,
        CreateDesktopModifierSelectionTag => settingsPageViewModel.CreateDesktopModifierSelection,
        SwitchDesktopWhileMouseButtonsArePressedModifierSelectionTag => settingsPageViewModel.SwitchDesktopWhileMouseButtonsArePressedModifierSelection,
        ToggleDeskBorderEnabledHotkeyEditorTag => settingsPageViewModel.ToggleDeskBorderEnabledHotkeyEditor.RequiredKeyboardModifierSelection,
        SwitchToPreviousDesktopHotkeyEditorTag => settingsPageViewModel.SwitchToPreviousDesktopHotkeyEditor.RequiredKeyboardModifierSelection,
        SwitchToNextDesktopHotkeyEditorTag => settingsPageViewModel.SwitchToNextDesktopHotkeyEditor.RequiredKeyboardModifierSelection,
        MoveFocusedWindowToPreviousDesktopHotkeyEditorTag => settingsPageViewModel.MoveFocusedWindowToPreviousDesktopHotkeyEditor.RequiredKeyboardModifierSelection,
        MoveFocusedWindowToNextDesktopHotkeyEditorTag => settingsPageViewModel.MoveFocusedWindowToNextDesktopHotkeyEditor.RequiredKeyboardModifierSelection,
        NavigatorToggleHotkeyEditorTag => settingsPageViewModel.NavigatorToggleHotkeyEditor.RequiredKeyboardModifierSelection,
        _ => null
    };

    private static bool IsWindowsOnlyModifierSelection(ModifierKeySelectionViewModel modifierKeySelectionViewModel) => modifierKeySelectionViewModel.CreateKeyboardModifierKeys() == WindowsOnlyKeyboardModifierKeys;

    private nint GetManageWindowHandle() => WindowNative.GetWindowHandle(_manageWindow);

    private IReadOnlyList<string> GetAvailableBlacklistedProcessNames()
    {
        var blacklistedProcessNameSet = ViewModel.BlacklistedProcessNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var whitelistedProcessNameSet = ViewModel.WhitelistedProcessNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. GetAvailableRunningProcessNames()
            .Where(processName => !blacklistedProcessNameSet.Contains(processName) && !whitelistedProcessNameSet.Contains(processName))
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    private IReadOnlyList<string> GetAvailableRunningProcessNames()
    {
        var availableProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var runningProcess in Process.GetProcesses())
        {
            using (runningProcess)
            {
                if (runningProcess.Id == Environment.ProcessId)
                    continue;

                var processName = TryGetForegroundProcessName(runningProcess);
                if (string.IsNullOrWhiteSpace(processName))
                    continue;

                _ = availableProcessNames.Add(processName);
            }
        }

        return [.. availableProcessNames.Order(StringComparer.OrdinalIgnoreCase)];
    }

    private IReadOnlyList<string> GetAvailableWhitelistedProcessNames()
    {
        var whitelistedProcessNameSet = ViewModel.WhitelistedProcessNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var availableProcessNames = new HashSet<string>(GetAvailableRunningProcessNames(), StringComparer.OrdinalIgnoreCase);
        foreach (var blacklistedProcessName in ViewModel.BlacklistedProcessNames)
            _ = availableProcessNames.Add(blacklistedProcessName);

        return [.. availableProcessNames
            .Where(processName => !whitelistedProcessNameSet.Contains(processName))
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    private static string FormatCurrentApplicationVersion(PackageVersion packageVersion) => $"v{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}";

    private static string GetCurrentApplicationVersion() => FormatCurrentApplicationVersion(Package.Current.Id.Version);

    private async Task ImportSettingsAsync()
    {
        if (_isSettingsTransferInProgress)
            return;

        SetSettingsTransferInProgress(true);
        try
        {
            var selectedSettingsFile = await PickImportSettingsFileAsync();
            if (selectedSettingsFile is null)
            {
                ShowSettingsImportExportResult(
                    LocalizedResourceAccessor.GetString("Settings.Import.CancelledTitle"),
                    LocalizedResourceAccessor.GetString("Settings.Import.CancelledMessage"),
                    InfoBarSeverity.Informational);
                return;
            }

            var settingsImportResult = await _settingsService.ImportAsync(selectedSettingsFile.Path);
            ApplySettingsToViewModel();
            if (settingsImportResult.WasAlwaysRunAsAdministratorSettingExcluded)
            {
                s_shouldShowPendingAlwaysRunAsAdministratorImportExcludedStatus = true;
                ShowAlwaysRunAsAdministratorImportExcludedStatus();
            }

            ShowSettingsImportExportResult(
                LocalizedResourceAccessor.GetString("Settings.Import.SuccessTitle"),
                LocalizedResourceAccessor.GetFormattedString("Settings.Import.SuccessMessageFormat", Path.GetFileName(selectedSettingsFile.Path)),
                InfoBarSeverity.Success);
        }
        catch (ArgumentException exception) { ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Import.FailedTitle"), exception.Message, InfoBarSeverity.Error); }
        catch (InvalidOperationException exception) { ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Import.FailedTitle"), exception.Message, InfoBarSeverity.Error); }
        catch (IOException exception) { ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Import.FailedTitle"), exception.Message, InfoBarSeverity.Error); }
        catch (UnauthorizedAccessException exception) { ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Import.FailedTitle"), exception.Message, InfoBarSeverity.Error); }
        finally { SetSettingsTransferInProgress(false); }
    }

    private async Task ExportIntegratedLogAsync()
    {
        if (_isLogExportInProgress)
            return;

        _isLogExportInProgress = true;
        ExportIntegratedLogButton.IsEnabled = false;
        try
        {
            var selectedLogFile = await PickExportLogFileAsync();
            if (selectedLogFile is null)
            {
                _fileLogService.WriteInformation(nameof(SettingsPage), "Integrated log export was canceled.");
                ShowDeveloperLogResult(
                    LocalizedResourceAccessor.GetString("Settings.DeveloperLogExport.CancelledTitle"),
                    LocalizedResourceAccessor.GetString("Settings.DeveloperLogExport.CancelledMessage"),
                    InfoBarSeverity.Informational);
                return;
            }

            _fileLogService.WriteInformation(nameof(SettingsPage), $"Exporting integrated log to '{selectedLogFile.Path}'.");
            await _fileLogService.ExportAsync(selectedLogFile.Path);
            ShowDeveloperLogResult(
                LocalizedResourceAccessor.GetString("Settings.DeveloperLogExport.SuccessTitle"),
                LocalizedResourceAccessor.GetFormattedString("Settings.DeveloperLogExport.SuccessMessageFormat", Path.GetFileName(selectedLogFile.Path)),
                InfoBarSeverity.Success);
        }
        catch (ArgumentException exception)
        {
            _fileLogService.WriteWarning(nameof(SettingsPage), "Integrated log export failed because the selected path was invalid.", exception);
            ShowDeveloperLogResult(LocalizedResourceAccessor.GetString("Settings.DeveloperLogExport.FailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        catch (InvalidOperationException exception)
        {
            _fileLogService.WriteWarning(nameof(SettingsPage), "Integrated log export failed because there were no logs to export.", exception);
            ShowDeveloperLogResult(LocalizedResourceAccessor.GetString("Settings.DeveloperLogExport.FailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        catch (IOException exception)
        {
            _fileLogService.WriteWarning(nameof(SettingsPage), "Integrated log export failed because the output file could not be written.", exception);
            ShowDeveloperLogResult(LocalizedResourceAccessor.GetString("Settings.DeveloperLogExport.FailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        catch (UnauthorizedAccessException exception)
        {
            _fileLogService.WriteWarning(nameof(SettingsPage), "Integrated log export failed because access to the output path was denied.", exception);
            ShowDeveloperLogResult(LocalizedResourceAccessor.GetString("Settings.DeveloperLogExport.FailedTitle"), exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            ExportIntegratedLogButton.IsEnabled = true;
            _isLogExportInProgress = false;
        }
    }

    private async Task LoadSettingsAsync()
    {
        await _settingsService.ReloadAsync();
        ShowPendingAlwaysRunAsAdministratorImportExcludedStatusIfNeeded();
        ShowPendingLanguageRestartRecommendedStatusIfNeeded();
    }

    private async void OnAddBlacklistedProcessNameButtonClicked(object sender, RoutedEventArgs routedEventArgs)
    {
        _ = sender;
        _ = routedEventArgs;
        await ShowBlacklistedProcessSelectionDialogAsync();
    }

    private async void OnAddWhitelistedProcessNameButtonClicked(object sender, RoutedEventArgs routedEventArgs)
    {
        _ = sender;
        _ = routedEventArgs;
        await ShowWhitelistedProcessSelectionDialogAsync();
    }

    private async void OnExportSettingsButtonClicked(object sender, RoutedEventArgs routedEventArgs) => await ExportSettingsAsync();

    private async void OnExportIntegratedLogButtonClicked(object sender, RoutedEventArgs routedEventArgs) => await ExportIntegratedLogAsync();

    private async void OnImportSettingsButtonClicked(object sender, RoutedEventArgs routedEventArgs) => await ImportSettingsAsync();

    private async void OnResetSettingsButtonClicked(object sender, RoutedEventArgs routedEventArgs) => await ResetSettingsAsync();

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

    private void OnLocalizationServiceLanguageChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (DispatcherQueue.TryEnqueue(RefreshStoreUpdateVisualState))
            return;

        RefreshStoreUpdateVisualState();
    }

    private async void OnModifierSelectionCheckBoxClicked(object sender, RoutedEventArgs routedEventArgs)
    {
        _ = routedEventArgs;
        await ShowWindowsOnlyModifierWarningIfNeededAsync(sender);
        QueueSettingsSave(shouldShowVerticalDesktopSwitchingModifierWarning: ShouldShowVerticalDesktopSwitchingModifierWarningForModifierSelectionChange(sender));
    }

    private async void OnCheckStoreUpdateButtonClicked(object sender, RoutedEventArgs routedEventArgs)
    {
        _ = sender;
        _ = routedEventArgs;
        await CheckStoreUpdateAsync();
    }

    private async void OnRemoveBlacklistedProcessNameButtonClicked(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is not Button { Tag: string blacklistedProcessName })
            return;

        if (!ViewModel.RemoveBlacklistedProcessName(blacklistedProcessName))
            return;

        await SaveSettingsAsync();
    }

    private async void OnRemoveWhitelistedProcessNameButtonClicked(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is not Button { Tag: string whitelistedProcessName })
            return;

        if (!ViewModel.RemoveWhitelistedProcessName(whitelistedProcessName))
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

    private void OnSettingToggleSwitchToggled(object sender, RoutedEventArgs routedEventArgs)
    {
        _ = routedEventArgs;
        if (_isSynchronizingViewModel || !_isInitialSettingsLoadCompleted || sender is not ToggleSwitch settingToggleSwitch)
            return;

        if (ShouldRejectAlwaysRunAsAdministratorToggleChange(settingToggleSwitch))
            return;

        QueueSettingsSave(settingToggleSwitch, ShouldShowVerticalDesktopSwitchingModifierWarningForToggleChange(settingToggleSwitch));
    }

    private void OnSettingsPageUnloaded(object sender, RoutedEventArgs routedEventArgs)
    {
        _developerLogInfoBarAutoHideTimer.Stop();
        _settingsImportExportInfoBarAutoHideTimer.Stop();
        _hotkeyService.RegistrationStateChanged -= OnHotkeyServiceRegistrationStateChanged;
        _localizationService.LanguageChanged -= OnLocalizationServiceLanguageChanged;
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
        List<string> fileTypeExtensions = [SettingsFileExtension]; // AOT Workaround : CCW doesn't support IList<string> marshaling
        fileSavePicker.FileTypeChoices.Add(LocalizedResourceAccessor.GetString("Settings.Export.FileTypeDisplayName"), fileTypeExtensions);
        fileSavePicker.DefaultFileExtension = SettingsFileExtension;
        fileSavePicker.SuggestedFileName = SettingsSuggestedFileName;
        fileSavePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        return await fileSavePicker.PickSaveFileAsync();
    }

    private async Task<PickFileResult> PickExportLogFileAsync()
    {
        var fileSavePicker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        List<string> fileTypeExtensions = [LogFileExtension]; // AOT Workaround : CCW doesn't support IList<string> marshaling
        fileSavePicker.FileTypeChoices.Add(LocalizedResourceAccessor.GetString("Settings.DeveloperLogExport.FileTypeDisplayName"), fileTypeExtensions);
        fileSavePicker.DefaultFileExtension = LogFileExtension;
        fileSavePicker.SuggestedFileName = $"{LogSuggestedFileNamePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}";
        fileSavePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        return await fileSavePicker.PickSaveFileAsync();
    }

    private async Task<PickFileResult> PickImportSettingsFileAsync()
    {
        var fileOpenPicker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId) { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        fileOpenPicker.FileTypeFilter.Add(SettingsFileExtension);

        return await fileOpenPicker.PickSingleFileAsync();
    }

    private async Task CheckStoreUpdateAsync()
    {
        CheckStoreUpdateButton.IsEnabled = false;
        SetStoreUpdateStatus("Settings.StoreUpdate.Status.Checking");
        try
        {
            if (await _storeUpdateService.GetAvailableUpdateCountAsync() > 0)
            {
                SetStoreUpdateStatus("Settings.StoreUpdate.Status.UpdateAvailable");
                if (await ShowStoreUpdateAvailableDialogAsync() == ContentDialogResult.Primary)
                    await OpenStoreProductPageAsync();

                return;
            }

            SetStoreUpdateStatus("Settings.StoreUpdate.Status.UpToDate");
        }
        catch (COMException) { ShowStoreUpdateCheckFailedStatus(); }
        catch (InvalidOperationException) { ShowStoreUpdateCheckFailedStatus(); }
        catch (UnauthorizedAccessException) { ShowStoreUpdateCheckFailedStatus(); }
        finally { CheckStoreUpdateButton.IsEnabled = true; }
    }

    private async Task OpenStoreProductPageAsync()
    {
        if (await _storeUpdateService.OpenStoreProductPageAsync())
            return;

        ShowSettingsStatus(
            LocalizedResourceAccessor.GetString("Settings.StoreUpdate.OpenStoreProductPageFailedTitle"),
            LocalizedResourceAccessor.GetString("Settings.StoreUpdate.OpenStoreProductPageFailedMessage"),
            InfoBarSeverity.Error);
    }

    private void RefreshStoreUpdateStatusText() => StoreUpdateStatusTextBlock.Text = LocalizedResourceAccessor.GetString(_storeUpdateStatusResourceName);

    private void RefreshStoreUpdateVersionInfoBar()
    {
        StoreUpdateVersionInfoBar.Title = LocalizedResourceAccessor.GetString("Settings.StoreUpdate.CurrentVersionTitle");
        StoreUpdateVersionInfoBar.Message = LocalizedResourceAccessor.GetFormattedString("Settings.StoreUpdate.CurrentVersionMessageFormat", GetCurrentApplicationVersion());
    }

    private void RefreshStoreUpdateVisualState()
    {
        RefreshStoreUpdateStatusText();
        RefreshStoreUpdateVersionInfoBar();
    }

    private void SetStoreUpdateStatus(string resourceName)
    {
        _storeUpdateStatusResourceName = resourceName;
        RefreshStoreUpdateStatusText();
    }

    private async Task<ContentDialogResult> ShowStoreUpdateAvailableDialogAsync()
    {
        var storeUpdateAvailableDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = LocalizedResourceAccessor.GetString("Settings.StoreUpdate.Dialog.Title"),
            Content = LocalizedResourceAccessor.GetString("Settings.StoreUpdate.Dialog.Content"),
            PrimaryButtonText = LocalizedResourceAccessor.GetString("Settings.StoreUpdate.Dialog.PrimaryButtonText"),
            CloseButtonText = LocalizedResourceAccessor.GetString("Settings.StoreUpdate.Dialog.CloseButtonText"),
            DefaultButton = ContentDialogButton.Primary
        };
        _themeService.RegisterFrameworkElement(storeUpdateAvailableDialog);
        return await storeUpdateAvailableDialog.ShowAsync();
    }

    private async Task<ContentDialogResult> ShowWindowsOnlyModifierWarningDialogAsync()
    {
        var windowsOnlyModifierWarningDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = LocalizedResourceAccessor.GetString("Settings.WindowsOnlyModifierWarning.Dialog.Title"),
            Content = LocalizedResourceAccessor.GetString("Settings.WindowsOnlyModifierWarning.Dialog.Content"),
            PrimaryButtonText = LocalizedResourceAccessor.GetString("Settings.WindowsOnlyModifierWarning.Dialog.PrimaryButtonText"),
            SecondaryButtonText = LocalizedResourceAccessor.GetString("Settings.WindowsOnlyModifierWarning.Dialog.SecondaryButtonText"),
            DefaultButton = ContentDialogButton.Primary
        };
        _themeService.RegisterFrameworkElement(windowsOnlyModifierWarningDialog);
        return await windowsOnlyModifierWarningDialog.ShowAsync();
    }

    private async Task<ContentDialogResult> ShowResetSettingsConfirmationDialogAsync()
    {
        var resetSettingsConfirmationDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = LocalizedResourceAccessor.GetString("Settings.Reset.Dialog.Title"),
            Content = LocalizedResourceAccessor.GetString("Settings.Reset.Dialog.Content"),
            PrimaryButtonText = LocalizedResourceAccessor.GetString("Settings.Reset.Dialog.PrimaryButtonText"),
            CloseButtonText = LocalizedResourceAccessor.GetString("Settings.Reset.Dialog.CloseButtonText"),
            DefaultButton = ContentDialogButton.Close
        };
        _themeService.RegisterFrameworkElement(resetSettingsConfirmationDialog);
        return await resetSettingsConfirmationDialog.ShowAsync();
    }

    private async Task ShowWindowsOnlyModifierWarningIfNeededAsync(object sender)
    {
        if (_isSynchronizingViewModel || !_isInitialSettingsLoadCompleted || ViewModel.IsWindowsOnlyModifierWarningSuppressed)
            return;

        if (sender is not FrameworkElement { Tag: string modifierSelectionTag })
            return;

        var modifierKeySelectionViewModel = GetModifierSelectionViewModel(ViewModel, modifierSelectionTag);
        if (modifierKeySelectionViewModel is null || !IsWindowsOnlyModifierSelection(modifierKeySelectionViewModel))
            return;

        if (await ShowWindowsOnlyModifierWarningDialogAsync() != ContentDialogResult.Secondary)
            return;

        ViewModel.IsWindowsOnlyModifierWarningSuppressed = true;
    }

    private async Task ShowBlacklistedProcessSelectionDialogAsync()
    {
        var availableBlacklistedProcessNames = GetAvailableBlacklistedProcessNames();
        if (availableBlacklistedProcessNames.Count == 0)
        {
            ShowSettingsStatus(
                LocalizedResourceAccessor.GetString("Settings.Blacklist.NoAvailableForegroundProcessesTitle"),
                LocalizedResourceAccessor.GetString("Settings.Blacklist.NoAvailableForegroundProcessesMessage"),
                InfoBarSeverity.Informational);
            return;
        }

        var selectedProcessNames = await ShowProcessSelectionDialogAsync(
            availableBlacklistedProcessNames,
            "ForegroundProcessSelectionDialog.Title",
            "ForegroundProcessSelectionDialog_DescriptionTextBlock.Text");
        if (selectedProcessNames.Count == 0 || !ViewModel.AddBlacklistedProcessNames(selectedProcessNames))
            return;

        await SaveSettingsAsync();
    }

    private async Task<IReadOnlyList<string>> ShowProcessSelectionDialogAsync(
        IReadOnlyList<string> availableProcessNames,
        string titleResourceName,
        string descriptionResourceName)
    {
        var foregroundProcessSelectionDialog = new ForegroundProcessSelectionDialog(
            availableProcessNames,
            LocalizedResourceAccessor.GetString(titleResourceName),
            LocalizedResourceAccessor.GetString(descriptionResourceName),
            LocalizedResourceAccessor.GetString("ForegroundProcessSelectionDialog.PrimaryButtonText"),
            _themeService)
        {
            XamlRoot = XamlRoot,
        };
        return await foregroundProcessSelectionDialog.ShowAsync() == ContentDialogResult.Primary
            ? foregroundProcessSelectionDialog.SelectedProcessNames
            : [];
    }

    private async Task ShowWhitelistedProcessSelectionDialogAsync()
    {
        var availableWhitelistedProcessNames = GetAvailableWhitelistedProcessNames();
        if (availableWhitelistedProcessNames.Count == 0)
        {
            ShowSettingsStatus(
                LocalizedResourceAccessor.GetString("Settings.Whitelist.NoAvailableProcessesTitle"),
                LocalizedResourceAccessor.GetString("Settings.Whitelist.NoAvailableProcessesMessage"),
                InfoBarSeverity.Informational);
            return;
        }

        var selectedProcessNames = await ShowProcessSelectionDialogAsync(
            availableWhitelistedProcessNames,
            "Settings.Whitelist.Dialog.Title",
            "Settings.Whitelist.Dialog.Description");
        if (selectedProcessNames.Count == 0 || !ViewModel.AddWhitelistedProcessNames(selectedProcessNames))
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
            var selectedSettingsFile = await PickExportSettingsFileAsync();
            if (selectedSettingsFile is null)
            {
                ShowSettingsImportExportResult(
                    LocalizedResourceAccessor.GetString("Settings.Export.CancelledTitle"),
                    LocalizedResourceAccessor.GetString("Settings.Export.CancelledMessage"),
                    InfoBarSeverity.Informational);
                return;
            }

            await _settingsService.ExportAsync(selectedSettingsFile.Path);
            ShowSettingsImportExportResult(
                LocalizedResourceAccessor.GetString("Settings.Export.SuccessTitle"),
                LocalizedResourceAccessor.GetFormattedString("Settings.Export.SuccessMessageFormat", Path.GetFileName(selectedSettingsFile.Path)),
                InfoBarSeverity.Success);
        }
        catch (ArgumentException exception) { ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Export.FailedTitle"), exception.Message, InfoBarSeverity.Error); }
        catch (InvalidOperationException exception) { ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Export.FailedTitle"), exception.Message, InfoBarSeverity.Error); }
        catch (IOException exception) { ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Export.FailedTitle"), exception.Message, InfoBarSeverity.Error); }
        catch (UnauthorizedAccessException exception) { ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Export.FailedTitle"), exception.Message, InfoBarSeverity.Error); }
        finally { SetSettingsTransferInProgress(false); }
    }

    private async Task ResetSettingsAsync()
    {
        if (_isSettingsTransferInProgress)
            return;

        if (await ShowResetSettingsConfirmationDialogAsync() != ContentDialogResult.Primary)
        {
            ShowSettingsImportExportResult(
                LocalizedResourceAccessor.GetString("Settings.Reset.CancelledTitle"),
                LocalizedResourceAccessor.GetString("Settings.Reset.CancelledMessage"),
                InfoBarSeverity.Informational);
            return;
        }

        SetSettingsTransferInProgress(true);
        try
        {
            await _settingsService.ResetAsync();
            ApplySettingsToViewModel();
            ShowSettingsImportExportResult(
                LocalizedResourceAccessor.GetString("Settings.Reset.SuccessTitle"),
                LocalizedResourceAccessor.GetString("Settings.Reset.SuccessMessage"),
                InfoBarSeverity.Success);
        }
        catch (ArgumentException exception) { ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Reset.FailedTitle"), exception.Message, InfoBarSeverity.Error); }
        catch (InvalidOperationException exception) { ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Reset.FailedTitle"), exception.Message, InfoBarSeverity.Error); }
        catch (UnauthorizedAccessException exception) { ShowSettingsImportExportResult(LocalizedResourceAccessor.GetString("Settings.Reset.FailedTitle"), exception.Message, InfoBarSeverity.Error); }
        finally { SetSettingsTransferInProgress(false); }
    }

    private void QueueSettingsSave(ToggleSwitch? sourceToggleSwitch = null, bool shouldShowVerticalDesktopSwitchingModifierWarning = false)
    {
        if (DispatcherQueue.TryEnqueue(async () => await SaveSettingsAsync(sourceToggleSwitch, shouldShowVerticalDesktopSwitchingModifierWarning)))
            return;

        _ = SaveSettingsAsync(sourceToggleSwitch, shouldShowVerticalDesktopSwitchingModifierWarning);
    }

    private bool ShouldRejectAlwaysRunAsAdministratorToggleChange(ToggleSwitch settingToggleSwitch)
    {
        if (!ReferenceEquals(settingToggleSwitch, AlwaysRunAsAdministratorToggleSwitch))
            return false;

        var currentSettings = _settingsService.Settings;
        if (currentSettings.IsAlwaysRunAsAdministratorEnabled == settingToggleSwitch.IsOn)
            return false;

        if (Environment.IsPrivilegedProcess)
            return false;

        _fileLogService.WriteWarning(nameof(SettingsPage), "Rejected an AlwaysRunAsAdministrator toggle change because the current process is not privileged.");
        RestoreToggleSwitchState(settingToggleSwitch, currentSettings);
        ApplySettingsToViewModel();
        ShowSettingsStatus(
            LocalizedResourceAccessor.GetString("Settings.Status.ApplyFailedTitle"),
            LocalizedResourceAccessor.GetString("Settings.Validation.PrivilegedProcessRequiredForAlwaysRunAsAdministrator"),
            InfoBarSeverity.Error);
        return true;
    }

    private void RestoreToggleSwitchState(ToggleSwitch settingToggleSwitch, DeskBorderSettings currentSettings)
    {
        var expectedToggleSwitchState = TryGetToggleSwitchState(settingToggleSwitch, currentSettings);
        if (!expectedToggleSwitchState.HasValue || settingToggleSwitch.IsOn == expectedToggleSwitchState.Value)
            return;

        _isSynchronizingViewModel = true;
        settingToggleSwitch.IsOn = expectedToggleSwitchState.Value;
        if (DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _isSynchronizingViewModel = false))
            return;

        _isSynchronizingViewModel = false;
    }

    private bool? TryGetToggleSwitchState(ToggleSwitch settingToggleSwitch, DeskBorderSettings currentSettings)
    {
        if (ReferenceEquals(settingToggleSwitch, DeskBorderEnabledToggleSwitch))
            return currentSettings.IsDeskBorderEnabled;

        if (ReferenceEquals(settingToggleSwitch, LaunchOnStartupToggleSwitch))
            return currentSettings.IsLaunchOnStartupEnabled;

        if (ReferenceEquals(settingToggleSwitch, AlwaysRunAsAdministratorToggleSwitch))
            return currentSettings.IsAlwaysRunAsAdministratorEnabled;

        if (ReferenceEquals(settingToggleSwitch, StoreUpdateCheckToggleSwitch))
            return currentSettings.IsStoreUpdateCheckEnabled;

        if (ReferenceEquals(settingToggleSwitch, CreateDesktopEnabledToggleSwitch))
            return currentSettings.IsDesktopCreationEnabled;

        if (ReferenceEquals(settingToggleSwitch, DesktopCreationSkippedWhenCurrentDesktopIsEmptyToggleSwitch))
            return currentSettings.IsDesktopCreationSkippedWhenCurrentDesktopIsEmpty;

        if (ReferenceEquals(settingToggleSwitch, DesktopCreationCompletionToastToggleSwitch))
            return currentSettings.IsDesktopCreationCompletionToastEnabled;

        if (ReferenceEquals(settingToggleSwitch, DesktopSwitchingAndCreationDisabledWhenFullscreenToggleSwitch)) return currentSettings.IsDesktopSwitchingAndCreationDisabledWhenForegroundWindowIsFullscreen;

        if (ReferenceEquals(settingToggleSwitch, WindowedFullscreenIncludedToggleSwitch)) return currentSettings.IsWindowedFullscreenIncludedWhenDisablingDesktopSwitchingAndCreation;

        if (ReferenceEquals(settingToggleSwitch, AutoDeleteToggleSwitch))
            return currentSettings.IsAutoDeleteEnabled;

        if (ReferenceEquals(settingToggleSwitch, AutoDeleteWarningToggleSwitch))
            return currentSettings.IsAutoDeleteWarningEnabled;

        if (ReferenceEquals(settingToggleSwitch, AutoDeleteCompletionToastToggleSwitch))
            return currentSettings.IsAutoDeleteCompletionToastEnabled;

        if (ReferenceEquals(settingToggleSwitch, DesktopEdgeAdditionalTriggerDistanceToggleSwitch))
            return currentSettings.IsDesktopEdgeAdditionalTriggerDistanceEnabled;

        if (ReferenceEquals(settingToggleSwitch, VerticalDesktopSwitchingToggleSwitch))
            return currentSettings.IsVerticalDesktopSwitchingEnabled;

        if (ReferenceEquals(settingToggleSwitch, VerticalDesktopSwitchDirectionReversedToggleSwitch))
            return currentSettings.IsVerticalDesktopSwitchDirectionReversed;

        if (ReferenceEquals(settingToggleSwitch, VerticalDesktopSwitchingOnlyInMultiDisplayEnvironmentToggleSwitch))
            return currentSettings.IsVerticalDesktopSwitchingOnlyInMultiDisplayEnvironment;

        if (ReferenceEquals(settingToggleSwitch, ToggleDeskBorderEnabledHotkeyToggleSwitch))
            return currentSettings.ApplicationHotkeySettings.ToggleDeskBorderEnabledHotkey.IsEnabled;

        if (ReferenceEquals(settingToggleSwitch, SwitchToPreviousDesktopHotkeyToggleSwitch))
            return currentSettings.DesktopSwitchHotkeySettings.SwitchToPreviousDesktopHotkey.IsEnabled;

        if (ReferenceEquals(settingToggleSwitch, SwitchToNextDesktopHotkeyToggleSwitch))
            return currentSettings.DesktopSwitchHotkeySettings.SwitchToNextDesktopHotkey.IsEnabled;

        if (ReferenceEquals(settingToggleSwitch, MoveFocusedWindowToNextDesktopHotkeyToggleSwitch))
            return currentSettings.FocusedWindowMoveHotkeySettings.MoveToNextDesktopHotkey.IsEnabled;

        if (ReferenceEquals(settingToggleSwitch, MoveFocusedWindowToPreviousDesktopHotkeyToggleSwitch))
            return currentSettings.FocusedWindowMoveHotkeySettings.MoveToPreviousDesktopHotkey.IsEnabled;

        if (ReferenceEquals(settingToggleSwitch, NavigatorToggleHotkeyToggleSwitch))
            return currentSettings.NavigatorSettings.ToggleHotkey.IsEnabled;

        if (ReferenceEquals(settingToggleSwitch, NavigatorTriggerAreaToggleSwitch))
            return currentSettings.NavigatorSettings.IsTriggerAreaEnabled;

        return null;
    }

    private static void RestartInfoBarAutoHideTimer(DispatcherQueueTimer infoBarAutoHideTimer)
    {
        infoBarAutoHideTimer.Stop();
        infoBarAutoHideTimer.Start();
    }

    private bool ShouldShowVerticalDesktopSwitchingModifierWarningForModifierSelectionChange(object sender)
    {
        if (!ViewModel.IsVerticalDesktopSwitchingEnabled || sender is not FrameworkElement { Tag: string modifierSelectionTag })
            return false;

        return modifierSelectionTag switch
        {
            SwitchDesktopModifierSelectionTag => ViewModel.SwitchDesktopModifierSelection.CreateKeyboardModifierKeys() == KeyboardModifierKeys.None,
            CreateDesktopModifierSelectionTag when ViewModel.IsDesktopCreationEnabled => ViewModel.CreateDesktopModifierSelection.CreateKeyboardModifierKeys() == KeyboardModifierKeys.None,
            _ => false
        };
    }

    private bool ShouldShowVerticalDesktopSwitchingModifierWarningForToggleChange(ToggleSwitch settingToggleSwitch)
    {
        if (ReferenceEquals(settingToggleSwitch, CreateDesktopEnabledToggleSwitch)
            && settingToggleSwitch.IsOn
            && ViewModel.IsVerticalDesktopSwitchingEnabled
            && ViewModel.CreateDesktopModifierSelection.CreateKeyboardModifierKeys() == KeyboardModifierKeys.None)
            return true;

        if (!ReferenceEquals(settingToggleSwitch, VerticalDesktopSwitchingToggleSwitch) || !settingToggleSwitch.IsOn)
            return false;

        if (ViewModel.SwitchDesktopModifierSelection.CreateKeyboardModifierKeys() == KeyboardModifierKeys.None)
            return true;

        if (ViewModel.IsDesktopCreationEnabled && ViewModel.CreateDesktopModifierSelection.CreateKeyboardModifierKeys() == KeyboardModifierKeys.None)
            return true;

        return false;
    }

    private static string? GetVerticalDesktopSwitchingModifierWarningMessage(DeskBorderSettings settings)
    {
        if (!settings.IsVerticalDesktopSwitchingEnabled)
            return null;

        var isSwitchDesktopModifierMissing = settings.SwitchDesktopModifierSettings.RequiredKeyboardModifierKeys == KeyboardModifierKeys.None;
        var isCreateDesktopModifierMissing = settings.IsDesktopCreationEnabled && settings.CreateDesktopModifierSettings.RequiredKeyboardModifierKeys == KeyboardModifierKeys.None;
        return (isSwitchDesktopModifierMissing, isCreateDesktopModifierMissing) switch
        {
            (true, true) => LocalizedResourceAccessor.GetString("Settings.Warning.VerticalDesktopSwitchingMissingSwitchAndCreateDesktopModifiers"),
            (true, false) => LocalizedResourceAccessor.GetString("Settings.Warning.VerticalDesktopSwitchingMissingSwitchDesktopModifier"),
            (false, true) => LocalizedResourceAccessor.GetString("Settings.Warning.VerticalDesktopSwitchingMissingCreateDesktopModifier"),
            _ => null
        };
    }

    private async Task SaveSettingsAsync(ToggleSwitch? sourceToggleSwitch = null, bool shouldShowVerticalDesktopSwitchingModifierWarning = false)
    {
        if (!_isInitialSettingsLoadCompleted || _isSynchronizingViewModel || _isSettingsTransferInProgress)
            return;

        await _settingsUpdateSemaphore.WaitAsync();
        try
        {
            var currentSettings = _settingsService.Settings;
            var updatedSettings = ViewModel.CreateSettings();
            var isLanguagePreferenceChanged = currentSettings.AppLanguagePreference != updatedSettings.AppLanguagePreference;
            var isThemePreferenceChanged = currentSettings.ApplicationThemePreference != updatedSettings.ApplicationThemePreference;
            if (isLanguagePreferenceChanged)
                s_shouldShowPendingLanguageRestartRecommendedStatus = true;

            await _settingsService.UpdateSettingsAsync(updatedSettings);
            if (isLanguagePreferenceChanged)
                return;

            var verticalDesktopSwitchingModifierWarningMessage = shouldShowVerticalDesktopSwitchingModifierWarning
                ? GetVerticalDesktopSwitchingModifierWarningMessage(updatedSettings)
                : null;
            if (!string.IsNullOrWhiteSpace(verticalDesktopSwitchingModifierWarningMessage))
                ShowSettingsStatus(
                    LocalizedResourceAccessor.GetString("Settings.Status.VerticalDesktopSwitchingModifierWarningTitle"),
                    verticalDesktopSwitchingModifierWarningMessage,
                    InfoBarSeverity.Warning);
            else if (isThemePreferenceChanged)
                ShowSettingsStatus(
                    LocalizedResourceAccessor.GetString("Settings.Status.ThemeRestartRecommendedTitle"),
                    LocalizedResourceAccessor.GetString("Settings.Status.ThemeRestartRecommendedMessage"),
                    InfoBarSeverity.Informational);
            else
                ClearSettingsStatus();
        }
        catch (ArgumentException exception) { HandleSaveSettingsFailure(exception.Message, sourceToggleSwitch); }
        catch (InvalidOperationException exception) { HandleSaveSettingsFailure(exception.Message, sourceToggleSwitch); }
        finally { _settingsUpdateSemaphore.Release(); }
    }

    private void ShowPendingLanguageRestartRecommendedStatusIfNeeded()
    {
        if (!s_shouldShowPendingLanguageRestartRecommendedStatus)
            return;

        s_shouldShowPendingLanguageRestartRecommendedStatus = false;
        ShowSettingsStatus(
            LocalizedResourceAccessor.GetString("Settings.Status.LanguageRestartRecommendedTitle"),
            LocalizedResourceAccessor.GetString("Settings.Status.LanguageRestartRecommendedMessage"),
            InfoBarSeverity.Informational);
    }

    private void ShowAlwaysRunAsAdministratorImportExcludedStatus() => ShowSettingsStatus(
        LocalizedResourceAccessor.GetString("Settings.Import.AlwaysRunAsAdministratorExcludedTitle"),
        LocalizedResourceAccessor.GetString("Settings.Import.AlwaysRunAsAdministratorExcludedMessage"),
        InfoBarSeverity.Informational);

    private void ShowPendingAlwaysRunAsAdministratorImportExcludedStatusIfNeeded()
    {
        if (!s_shouldShowPendingAlwaysRunAsAdministratorImportExcludedStatus)
            return;

        s_shouldShowPendingAlwaysRunAsAdministratorImportExcludedStatus = false;
        ShowAlwaysRunAsAdministratorImportExcludedStatus();
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
            var navigatorTriggerAreaSelectionWindow = new NavigatorTriggerAreaSelectionWindow(_localizationService, _themeService, targetDisplayMonitor);
            var selectedTriggerRectangleSettings = await navigatorTriggerAreaSelectionWindow.ShowSelectionAsync();
            if (selectedTriggerRectangleSettings is null)
                return;

            ViewModel.SetNavigatorTriggerRectangle(selectedTriggerRectangleSettings);
            await SaveSettingsAsync();
        }
        catch (InvalidOperationException exception) { ShowSettingsStatus(LocalizedResourceAccessor.GetString("Settings.Status.ApplyFailedTitle"), exception.Message, InfoBarSeverity.Error); }
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
        ResetSettingsButton.IsEnabled = !isSettingsTransferInProgress;
    }

    private void ShowStoreUpdateCheckFailedStatus()
    {
        SetStoreUpdateStatus("Settings.StoreUpdate.Status.CheckFailed");
        ShowSettingsStatus(
            LocalizedResourceAccessor.GetString("Settings.StoreUpdate.CheckFailedTitle"),
            LocalizedResourceAccessor.GetString("Settings.StoreUpdate.CheckFailedMessage"),
            InfoBarSeverity.Error);
    }

    private static void ShowInfoBar(InfoBar infoBar, DispatcherQueueTimer infoBarAutoHideTimer, string title, string message, InfoBarSeverity infoBarSeverity)
    {
        infoBar.Title = title;
        infoBar.Message = message;
        infoBar.Severity = infoBarSeverity;
        infoBar.IsOpen = true;
        RestartInfoBarAutoHideTimer(infoBarAutoHideTimer);
    }

    private void ShowDeveloperLogResult(string title, string message, InfoBarSeverity infoBarSeverity) => ShowInfoBar(DeveloperLogInfoBar, _developerLogInfoBarAutoHideTimer, title, message, infoBarSeverity);

    private void ShowSettingsImportExportResult(string title, string message, InfoBarSeverity infoBarSeverity) => ShowInfoBar(SettingsImportExportInfoBar, _settingsImportExportInfoBarAutoHideTimer, title, message, infoBarSeverity);

    private void ShowSettingsStatus(string title, string message, InfoBarSeverity infoBarSeverity) => ShowInfoBar(SettingsStatusInfoBar, _settingsStatusInfoBarAutoHideTimer, title, message, infoBarSeverity);

    private void HandleSaveSettingsFailure(string message, ToggleSwitch? sourceToggleSwitch = null)
    {
        s_shouldShowPendingLanguageRestartRecommendedStatus = false;
        if (sourceToggleSwitch is not null)
            RestoreToggleSwitchState(sourceToggleSwitch, _settingsService.Settings);
        ApplySettingsToViewModel();
        ShowSettingsStatus(LocalizedResourceAccessor.GetString("Settings.Status.ApplyFailedTitle"), message, InfoBarSeverity.Error);
    }

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
