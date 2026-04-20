using DeskBorder.Helpers;
using DeskBorder.Navigation;
using DeskBorder.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Globalization;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Windows.ApplicationModel;
using Windows.Storage;

namespace DeskBorder.Pages;

public sealed partial class DashboardPage : Page
{
    private const string ShownStorePatchNotesVersionSettingsKey = "DashboardShownStorePatchNotesVersion";
    private static readonly HttpClient s_httpClient = new();
    private static readonly ApplicationDataContainer s_localSettings = ApplicationData.Current.LocalSettings;
    private static readonly Lock s_storePatchNotesStateLock = new();
    private static bool s_isStorePatchNotesCheckInProgress;

    private readonly IDesktopEdgeMonitorService _desktopEdgeMonitorService;
    private readonly IDeskBorderRuntimeService _deskBorderRuntimeService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IManageNavigationService _manageNavigationService;
    private readonly INavigatorService _navigatorService;
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private bool _isQuickToggleStateLoaded;

    public DashboardPage()
    {
        InitializeComponent();

        _manageNavigationService = App.GetRequiredService<IManageNavigationService>();
        _deskBorderRuntimeService = App.GetRequiredService<IDeskBorderRuntimeService>();
        _settingsService = App.GetRequiredService<ISettingsService>();
        _hotkeyService = App.GetRequiredService<IHotkeyService>();
        _desktopEdgeMonitorService = App.GetRequiredService<IDesktopEdgeMonitorService>();
        _navigatorService = App.GetRequiredService<INavigatorService>();
        _themeService = App.GetRequiredService<IThemeService>();

        _deskBorderRuntimeService.StateChanged += OnPresentationSourceStateChanged;
        _settingsService.SettingsChanged += OnPresentationSourceStateChanged;
        _desktopEdgeMonitorService.MonitoringStateChanged += OnDesktopEdgeMonitorServiceMonitoringStateChanged;
        _navigatorService.ViewModel.PropertyChanged += OnNavigatorViewModelPropertyChanged;
        Unloaded += OnDashboardPageUnloaded;

        UpdatePresentation();
    }

    private async Task ApplyQuickSettingAsync(Func<Task> updateSettingsAsync)
    {
        try
        {
            await updateSettingsAsync();
            DashboardStatusInfoBar.IsOpen = false;
            UpdatePresentation();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            DashboardStatusInfoBar.Title = LocalizedResourceAccessor.GetString("Dashboard.Status.ApplyFailedTitle");
            DashboardStatusInfoBar.Message = exception.Message;
            DashboardStatusInfoBar.Severity = InfoBarSeverity.Error;
            DashboardStatusInfoBar.IsOpen = true;
            UpdatePresentation();
        }
    }

    private static Uri CreateStorePatchNotesUri(string market, string locale) => new($"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{StoreUpdateService.StoreProductId}?market={market}&locale={locale}&deviceFamily=Windows.Desktop");

    private static void EndStorePatchNotesCheck()
    {
        lock (s_storePatchNotesStateLock)
            s_isStorePatchNotesCheckInProgress = false;
    }

    private static async Task<string?> FetchStorePatchNotesAsync()
    {
        var (market, locale) = GetStorePatchNotesRequestOptions();
        using var storePatchNotesResponseMessage = await s_httpClient.GetAsync(CreateStorePatchNotesUri(market, locale));
        storePatchNotesResponseMessage.EnsureSuccessStatusCode();

        await using var storePatchNotesResponseStream = await storePatchNotesResponseMessage.Content.ReadAsStreamAsync();
        using var storePatchNotesJsonDocument = await JsonDocument.ParseAsync(storePatchNotesResponseStream);
        if (!storePatchNotesJsonDocument.RootElement.TryGetProperty("Payload", out var payloadJsonElement)
            || !payloadJsonElement.TryGetProperty("Notes", out var notesJsonElement)
            || notesJsonElement.ValueKind != JsonValueKind.Array)
            return string.Empty;

        foreach (var noteJsonElement in notesJsonElement.EnumerateArray())
        {
            if (noteJsonElement.ValueKind != JsonValueKind.String) continue;

            var storePatchNotes = noteJsonElement.GetString();
            if (!string.IsNullOrWhiteSpace(storePatchNotes)) return storePatchNotes.Trim();
        }

        return string.Empty;
    }

    private static string GetCurrentApplicationVersion() => $"{Package.Current.Id.Version.Major}.{Package.Current.Id.Version.Minor}.{Package.Current.Id.Version.Build}";

    private static (string Market, string Locale) GetStorePatchNotesRequestOptions()
    {
        var currentLanguageTag = ApplicationLanguages.PrimaryLanguageOverride;
        return currentLanguageTag switch
        {
            var languageTag when languageTag.StartsWith("ko", StringComparison.OrdinalIgnoreCase) => ("KR", "ko-KR"),
            var languageTag when languageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase) => ("JP", "ja-JP"),
            var languageTag when languageTag.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) => ("CN", "zh-Hant"),
            var languageTag when languageTag.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase) => ("CN", "zh-Hans"),
            _ => ("US", "en-US")
        };
    }

    private static bool IsStorePatchNotesAlreadyShown(string currentApplicationVersion) => string.Equals(s_localSettings.Values[ShownStorePatchNotesVersionSettingsKey] as string, currentApplicationVersion, StringComparison.Ordinal);

    private static void MarkStorePatchNotesAsShown(string currentApplicationVersion) => s_localSettings.Values[ShownStorePatchNotesVersionSettingsKey] = currentApplicationVersion;

    private void OnDashboardPageUnloaded(object sender, RoutedEventArgs routedEventArgs)
    {
        _deskBorderRuntimeService.StateChanged -= OnPresentationSourceStateChanged;
        _settingsService.SettingsChanged -= OnPresentationSourceStateChanged;
        _desktopEdgeMonitorService.MonitoringStateChanged -= OnDesktopEdgeMonitorServiceMonitoringStateChanged;
        _navigatorService.ViewModel.PropertyChanged -= OnNavigatorViewModelPropertyChanged;
        Unloaded -= OnDashboardPageUnloaded;
    }

    private async void OnDashboardPageLoaded(object sender, RoutedEventArgs routedEventArgs) => await ShowStorePatchNotesIfNeededAsync();

    private void OnDesktopEdgeMonitorServiceMonitoringStateChanged(
        object? sender,
        DesktopEdgeMonitoringStateChangedEventArgs desktopEdgeMonitoringStateChangedEventArgs)
    {
        _ = sender;
        _ = desktopEdgeMonitoringStateChangedEventArgs;
        EnqueuePresentationUpdate();
    }

    private async void OnLaunchOnStartupToggleSwitchToggled(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!_isQuickToggleStateLoaded)
            return;

        await ApplyQuickSettingAsync(() => _settingsService.SetLaunchOnStartupEnabledAsync(LaunchOnStartupToggleSwitch.IsOn));
    }

    private void OnNavigatorViewModelPropertyChanged(object? sender, PropertyChangedEventArgs propertyChangedEventArgs)
    {
        _ = sender;
        _ = propertyChangedEventArgs;
        EnqueuePresentationUpdate();
    }

    private void OnOpenSettingsButtonClicked(object sender, RoutedEventArgs routedEventArgs) => _manageNavigationService.NavigateTo(ManageNavigationTarget.Settings);

    private void OnOpenSettingsFromNavigatorButtonClicked(object sender, RoutedEventArgs routedEventArgs) => _manageNavigationService.NavigateTo(ManageNavigationTarget.Settings);

    private async Task ShowStorePatchNotesDialogAsync(string currentApplicationVersion, string storePatchNotes)
    {
        var storePatchNotesTextBlock = new TextBlock
        {
            Text = storePatchNotes,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        var storePatchNotesDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = LocalizedResourceAccessor.GetFormattedString("Dashboard.StorePatchNotes.Dialog.TitleFormat", currentApplicationVersion),
            Content = new ScrollViewer
            {
                Content = storePatchNotesTextBlock,
                MaxHeight = 400
            },
            CloseButtonText = LocalizedResourceAccessor.GetString("Dashboard.StorePatchNotes.Dialog.CloseButtonText"),
            DefaultButton = ContentDialogButton.Close
        };
        _themeService.RegisterFrameworkElement(storePatchNotesDialog);
        await storePatchNotesDialog.ShowAsync();
    }

    private async Task ShowStorePatchNotesIfNeededAsync()
    {
        var currentApplicationVersion = GetCurrentApplicationVersion();
        if (IsStorePatchNotesAlreadyShown(currentApplicationVersion) || !TryBeginStorePatchNotesCheck()) return;

        try
        {
            var (isSuccessful, storePatchNotes) = await TryFetchStorePatchNotesAsync();
            if (!isSuccessful) return;

            if (string.IsNullOrWhiteSpace(storePatchNotes))
            {
                MarkStorePatchNotesAsShown(currentApplicationVersion);
                return;
            }

            if (XamlRoot is null) return;

            try { await ShowStorePatchNotesDialogAsync(currentApplicationVersion, storePatchNotes); }
            catch (InvalidOperationException exception)
            {
                Debug.WriteLine($"Failed to show Store patch notes dialog: {exception.Message}");
                return;
            }
            MarkStorePatchNotesAsShown(currentApplicationVersion);
        }
        finally { EndStorePatchNotesCheck(); }
    }

    private static bool TryBeginStorePatchNotesCheck()
    {
        lock (s_storePatchNotesStateLock)
        {
            if (s_isStorePatchNotesCheckInProgress) return false;

            s_isStorePatchNotesCheckInProgress = true;
            return true;
        }
    }

    private static async Task<(bool IsSuccessful, string? StorePatchNotes)> TryFetchStorePatchNotesAsync()
    {
        try { return (true, await FetchStorePatchNotesAsync()); }
        catch (HttpRequestException exception) { Debug.WriteLine($"Failed to fetch Store patch notes: {exception.Message}"); }
        catch (JsonException exception) { Debug.WriteLine($"Failed to parse Store patch notes: {exception.Message}"); }
        catch (TaskCanceledException exception) { Debug.WriteLine($"Store patch notes request timed out: {exception.Message}"); }
        return (false, null);
    }

    private void OnPresentationSourceStateChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        EnqueuePresentationUpdate();
    }

    private async void OnStoreUpdateCheckToggleSwitchToggled(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!_isQuickToggleStateLoaded)
            return;

        await ApplyQuickSettingAsync(() => _settingsService.UpdateSettingsAsync(_settingsService.Settings with
        {
            IsStoreUpdateCheckEnabled = StoreUpdateCheckToggleSwitch.IsOn
        }));
    }

    private void OnToggleNavigatorButtonClicked(object sender, RoutedEventArgs routedEventArgs)
    {
        _navigatorService.ToggleOverlay();
        UpdatePresentation();
    }

    private async void OnToggleRuntimeButtonClicked(object sender, RoutedEventArgs routedEventArgs)
        => await ApplyQuickSettingAsync(() => _settingsService.UpdateSettingsAsync(_settingsService.Settings with { IsDeskBorderEnabled = !_settingsService.Settings.IsDeskBorderEnabled }));

    private void EnqueuePresentationUpdate()
    {
        if (DispatcherQueue.TryEnqueue(UpdatePresentation))
            return;

        UpdatePresentation();
    }

    private void UpdatePresentation()
    {
        UpdateQuickTogglePresentation();
        UpdateRuntimePresentation();
        UpdateSettingsSummaryPresentation();
        UpdateHotkeyPresentation();
        UpdateNavigatorPresentation();
    }

    private void UpdateHotkeyPresentation()
    {
        var currentSettings = _settingsService.Settings;
        HotkeySummaryTextBlock.Text = string.Join(Environment.NewLine,
        [
            LocalizedResourceAccessor.GetFormattedString("Dashboard.Hotkey.ToggleDeskBorderFormat", SettingsDisplayFormatter.FormatKeyboardShortcut(currentSettings.ApplicationHotkeySettings.ToggleDeskBorderEnabledHotkey)),
            LocalizedResourceAccessor.GetFormattedString("Dashboard.Hotkey.SwitchPreviousFormat", SettingsDisplayFormatter.FormatKeyboardShortcut(currentSettings.DesktopSwitchHotkeySettings.SwitchToPreviousDesktopHotkey)),
            LocalizedResourceAccessor.GetFormattedString("Dashboard.Hotkey.SwitchNextFormat", SettingsDisplayFormatter.FormatKeyboardShortcut(currentSettings.DesktopSwitchHotkeySettings.SwitchToNextDesktopHotkey)),
            LocalizedResourceAccessor.GetFormattedString("Dashboard.Hotkey.MovePreviousFormat", SettingsDisplayFormatter.FormatKeyboardShortcut(currentSettings.FocusedWindowMoveHotkeySettings.MoveToPreviousDesktopHotkey)),
            LocalizedResourceAccessor.GetFormattedString("Dashboard.Hotkey.MoveNextFormat", SettingsDisplayFormatter.FormatKeyboardShortcut(currentSettings.FocusedWindowMoveHotkeySettings.MoveToNextDesktopHotkey)),
            LocalizedResourceAccessor.GetFormattedString("Dashboard.Hotkey.NavigatorToggleFormat", SettingsDisplayFormatter.FormatKeyboardShortcut(currentSettings.NavigatorSettings.ToggleHotkey))
        ]);
        ModifierSummaryTextBlock.Text = string.Join(Environment.NewLine,
        [
            LocalizedResourceAccessor.GetFormattedString(
                "Dashboard.HotkeyModifiers.Format",
                SettingsDisplayFormatter.FormatKeyboardModifierKeys(currentSettings.SwitchDesktopModifierSettings.RequiredKeyboardModifierKeys),
                currentSettings.IsDesktopCreationEnabled
                    ? SettingsDisplayFormatter.FormatKeyboardModifierKeys(currentSettings.CreateDesktopModifierSettings.RequiredKeyboardModifierKeys)
                    : LocalizedResourceAccessor.GetString("Common.Disabled")),
            LocalizedResourceAccessor.GetString(_hotkeyService.IsInitialized ? "Dashboard.HotkeyService.Ready" : "Dashboard.HotkeyService.Initializing")
        ]);
    }

    private void UpdateNavigatorPresentation()
    {
        var currentMonitoringState = _desktopEdgeMonitorService.CurrentState;
        var currentSettings = _settingsService.Settings;
        NavigatorSummaryTextBlock.Text = string.Join(Environment.NewLine,
        [
            LocalizedResourceAccessor.GetFormattedString("Dashboard.NavigatorOverlay.Format", LocalizedResourceAccessor.GetString(_navigatorService.IsVisible ? "Common.Visible" : "Common.Hidden")),
            LocalizedResourceAccessor.GetFormattedString("Dashboard.NavigatorTriggerArea.Format", LocalizedResourceAccessor.GetString(currentSettings.NavigatorSettings.IsTriggerAreaEnabled ? "Common.Enabled" : "Common.Disabled")),
            LocalizedResourceAccessor.GetFormattedString("Dashboard.NavigatorAreaPosition.Format", SettingsDisplayFormatter.FormatTriggerRectangle(currentSettings.NavigatorSettings.TriggerRectangle))
        ]);
        DisplaySummaryTextBlock.Text = string.Join(Environment.NewLine,
        [
            LocalizedResourceAccessor.GetFormattedString("Dashboard.DisplayCount.Format", Math.Max(1, currentMonitoringState.DisplayMonitors.Length), SettingsDisplayFormatter.FormatDesktopEdgeAvailabilityStatus(currentMonitoringState.DesktopEdgeAvailabilityStatus)),
            LocalizedResourceAccessor.GetFormattedString("Dashboard.CurrentEdge.Format", SettingsDisplayFormatter.FormatDesktopEdgeKind(currentMonitoringState.ActiveDesktopEdge))
        ]);
    }

    private void UpdateQuickTogglePresentation()
    {
        var currentSettings = _settingsService.Settings;
        _isQuickToggleStateLoaded = false;
        LaunchOnStartupToggleSwitch.IsOn = currentSettings.IsLaunchOnStartupEnabled;
        StoreUpdateCheckToggleSwitch.IsOn = currentSettings.IsStoreUpdateCheckEnabled;
        _isQuickToggleStateLoaded = true;
    }

    private void UpdateRuntimePresentation()
    {
        var currentSettings = _settingsService.Settings;
        RuntimeStatusTextBlock.Text = _deskBorderRuntimeService.StatusMessage;
        ToggleRuntimeButton.Content = LocalizedResourceAccessor.GetString(currentSettings.IsDeskBorderEnabled ? "Dashboard.ToggleRuntime.Disable" : "Dashboard.ToggleRuntime.Enable");
        MonitoringStatusTextBlock.Text = _desktopEdgeMonitorService.IsMonitoring
            ? LocalizedResourceAccessor.GetString("Dashboard.Monitoring.Running")
            : LocalizedResourceAccessor.GetString("Dashboard.Monitoring.Stopped");
    }

    private void UpdateSettingsSummaryPresentation()
    {
        var currentSettings = _settingsService.Settings;
        GeneralSettingsSummaryTextBlock.Text = string.Join(Environment.NewLine,
        [
            SettingsDisplayFormatter.FormatMultiDisplayBehavior(currentSettings.MultiDisplayBehavior),
            LocalizedResourceAccessor.GetFormattedString("Dashboard.AutoDeleteSummary.Format", LocalizedResourceAccessor.GetString(currentSettings.IsAutoDeleteEnabled ? "Common.Enabled" : "Common.Disabled"), currentSettings.BlacklistedProcessNames.Length)
        ]);
    }
}
