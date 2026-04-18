using DeskBorder.Helpers;
using DeskBorder.Navigation;
using DeskBorder.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace DeskBorder.Pages;

public sealed partial class DashboardPage : Page
{
    private readonly IDesktopEdgeMonitorService _desktopEdgeMonitorService;
    private readonly IDeskBorderRuntimeService _deskBorderRuntimeService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IManageNavigationService _manageNavigationService;
    private readonly INavigatorService _navigatorService;
    private readonly ISettingsService _settingsService;
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

    private void OnDashboardPageUnloaded(object sender, RoutedEventArgs routedEventArgs)
    {
        _deskBorderRuntimeService.StateChanged -= OnPresentationSourceStateChanged;
        _settingsService.SettingsChanged -= OnPresentationSourceStateChanged;
        _desktopEdgeMonitorService.MonitoringStateChanged -= OnDesktopEdgeMonitorServiceMonitoringStateChanged;
        _navigatorService.ViewModel.PropertyChanged -= OnNavigatorViewModelPropertyChanged;
        Unloaded -= OnDashboardPageUnloaded;
    }

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

    private async void OnToggleRuntimeButtonClicked(object sender, RoutedEventArgs routedEventArgs) => await _deskBorderRuntimeService.SetRunningStateAsync(!_deskBorderRuntimeService.IsRunning);

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
        RuntimeStatusTextBlock.Text = _deskBorderRuntimeService.StatusMessage;
        ToggleRuntimeButton.Content = LocalizedResourceAccessor.GetString(_deskBorderRuntimeService.IsRunning ? "Dashboard.ToggleRuntime.Disable" : "Dashboard.ToggleRuntime.Enable");
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
            LocalizedResourceAccessor.GetFormattedString("Dashboard.EmptyDesktopDetection.Format", SettingsDisplayFormatter.FormatEmptyDesktopDetectionMode(currentSettings.EmptyDesktopDetectionMode)),
            LocalizedResourceAccessor.GetFormattedString("Dashboard.AutoDeleteSummary.Format", LocalizedResourceAccessor.GetString(currentSettings.IsAutoDeleteEnabled ? "Common.Enabled" : "Common.Disabled"), currentSettings.BlacklistedProcessNames.Length)
        ]);
    }
}
