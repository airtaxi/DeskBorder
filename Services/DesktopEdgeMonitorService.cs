using DeskBorder.Helpers;
using DeskBorder.Models;

namespace DeskBorder.Services;

public sealed class DesktopEdgeMonitorService(ISettingsService settingsService) : IDesktopEdgeMonitorService, IDisposable
{
    private static readonly TimeSpan s_defaultPollingInterval = TimeSpan.FromMilliseconds(40);

    private readonly ISettingsService _settingsService = settingsService;
    private CancellationTokenSource? _monitoringCancellationTokenSource;
    private Task? _monitoringTask;
    private bool _isDisposed;

    public event EventHandler<DesktopEdgeMonitoringStateChangedEventArgs>? MonitoringStateChanged;

    public bool IsMonitoring => _monitoringTask is not null;

    public DesktopEdgeMonitoringState CurrentState { get; private set; } = new();

    public DesktopEdgeMonitoringState CaptureCurrentState()
    {
        var currentSettings = _settingsService.Settings;
        var currentCursorPosition = MouseHelper.GetCurrentCursorPosition();
        var cursorClippingState = MouseHelper.GetCursorClippingState();
        var currentForegroundProcessName = MouseHelper.TryGetForegroundProcessName();
        var modifierKeySnapshot = MouseHelper.GetModifierKeySnapshot();
        var displayMonitors = MouseHelper.GetDisplayMonitors();
        var currentDisplayMonitor = FindCurrentDisplayMonitor(displayMonitors, currentCursorPosition);
        var desktopEdgeAvailabilityStatus = GetDesktopEdgeAvailabilityStatus(
            currentSettings,
            displayMonitors.Length,
            currentDisplayMonitor is not null,
            cursorClippingState.IsCursorClipped,
            currentForegroundProcessName);
        var activeDesktopEdge = desktopEdgeAvailabilityStatus == DesktopEdgeAvailabilityStatus.Enabled
            ? GetActiveDesktopEdge(displayMonitors, currentDisplayMonitor, currentCursorPosition, currentSettings.DesktopEdgeIgnoreZoneSettings)
            : DesktopEdgeKind.None;
        var previousState = CurrentState;
        var navigatorTriggerState = CreateNavigatorTriggerState(
            currentSettings.NavigatorSettings,
            desktopEdgeAvailabilityStatus == DesktopEdgeAvailabilityStatus.Enabled,
            currentDisplayMonitor,
            currentCursorPosition,
            previousState.NavigatorTriggerState.IsCursorInsideTriggerRectangle);
        var hasActiveDesktopEdgeChanged = previousState.ActiveDesktopEdge != activeDesktopEdge;
        return new()
        {
            CursorPosition = currentCursorPosition,
            CursorClippingState = cursorClippingState,
            ModifierKeySnapshot = modifierKeySnapshot,
            DisplayMonitors = displayMonitors,
            CurrentDisplayMonitor = currentDisplayMonitor,
            DesktopEdgeAvailabilityStatus = desktopEdgeAvailabilityStatus,
            ActiveDesktopEdge = activeDesktopEdge,
            HasCursorEnteredDesktopEdge = hasActiveDesktopEdgeChanged && activeDesktopEdge != DesktopEdgeKind.None,
            HasCursorLeftDesktopEdge = hasActiveDesktopEdgeChanged && previousState.ActiveDesktopEdge != DesktopEdgeKind.None,
            IsSwitchDesktopModifierSatisfied = MouseHelper.AreRequiredKeyboardModifierKeysPressed(currentSettings.SwitchDesktopModifierSettings.RequiredKeyboardModifierKeys, modifierKeySnapshot.PressedKeyboardModifierKeys),
            IsCreateDesktopModifierSatisfied = MouseHelper.AreRequiredKeyboardModifierKeysPressed(currentSettings.CreateDesktopModifierSettings.RequiredKeyboardModifierKeys, modifierKeySnapshot.PressedKeyboardModifierKeys),
            NavigatorTriggerState = navigatorTriggerState
        };
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _settingsService.SettingsChanged -= OnSettingsServiceSettingsChanged;
        if (_monitoringCancellationTokenSource is not null)
            _monitoringCancellationTokenSource.Cancel();

        _monitoringCancellationTokenSource?.Dispose();
        _isDisposed = true;
    }

    public void Refresh()
    {
        var previousState = CurrentState;
        var currentState = CaptureCurrentState();
        CurrentState = currentState;
        if (!HasStateChanged(previousState, currentState))
            return;

        MonitoringStateChanged?.Invoke(this, new(previousState, currentState));
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_monitoringTask is not null)
            return Task.CompletedTask;

        _settingsService.SettingsChanged += OnSettingsServiceSettingsChanged;
        Refresh();
        _monitoringCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitoringTask = RunMonitoringLoopAsync(_monitoringCancellationTokenSource.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_monitoringTask is null)
            return;

        _settingsService.SettingsChanged -= OnSettingsServiceSettingsChanged;
        _monitoringCancellationTokenSource?.Cancel();

        try { await _monitoringTask; }
        catch (OperationCanceledException) { }
        finally
        {
            _monitoringCancellationTokenSource?.Dispose();
            _monitoringCancellationTokenSource = null;
            _monitoringTask = null;
        }
    }

    private static NavigatorTriggerState CreateNavigatorTriggerState(
        NavigatorSettings navigatorSettings,
        bool isNavigatorTriggerAvailable,
        DisplayMonitorInfo? currentDisplayMonitor,
        ScreenPoint currentCursorPosition,
        bool wasCursorInsideTriggerRectangle)
    {
        if (!navigatorSettings.IsTriggerAreaEnabled || !isNavigatorTriggerAvailable || currentDisplayMonitor is null)
            return new()
            {
                HasCursorLeftTriggerRectangle = wasCursorInsideTriggerRectangle
            };

        var triggerRectangle = CreateNavigatorTriggerRectangle(currentDisplayMonitor.MonitorBounds, navigatorSettings.TriggerRectangle);
        var isCursorInsideTriggerRectangle = triggerRectangle.Contains(currentCursorPosition);
        return new()
        {
            IsEnabled = true,
            TriggerRectangle = triggerRectangle,
            IsCursorInsideTriggerRectangle = isCursorInsideTriggerRectangle,
            HasCursorEnteredTriggerRectangle = !wasCursorInsideTriggerRectangle && isCursorInsideTriggerRectangle,
            HasCursorLeftTriggerRectangle = wasCursorInsideTriggerRectangle && !isCursorInsideTriggerRectangle
        };
    }

    private static ScreenRectangle CreateNavigatorTriggerRectangle(ScreenRectangle displayMonitorBounds, TriggerRectangleSettings triggerRectangleSettings)
    {
        var width = Math.Clamp(
            (int)Math.Round(displayMonitorBounds.Width * triggerRectangleSettings.Width, MidpointRounding.AwayFromZero),
            1,
            displayMonitorBounds.Width);
        var height = Math.Clamp(
            (int)Math.Round(displayMonitorBounds.Height * triggerRectangleSettings.Height, MidpointRounding.AwayFromZero),
            1,
            displayMonitorBounds.Height);
        var right = Math.Clamp(
            displayMonitorBounds.Left + (int)Math.Round(displayMonitorBounds.Width * (triggerRectangleSettings.Left + triggerRectangleSettings.Width), MidpointRounding.AwayFromZero),
            displayMonitorBounds.Left + width,
            displayMonitorBounds.Right);
        var bottom = Math.Clamp(
            displayMonitorBounds.Top + (int)Math.Round(displayMonitorBounds.Height * (triggerRectangleSettings.Top + triggerRectangleSettings.Height), MidpointRounding.AwayFromZero),
            displayMonitorBounds.Top + height,
            displayMonitorBounds.Bottom);
        return new(right - width, bottom - height, right, bottom);
    }

    private static DesktopEdgeKind GetActiveDesktopEdge(
        DisplayMonitorInfo[] displayMonitors,
        DisplayMonitorInfo? currentDisplayMonitor,
        ScreenPoint currentCursorPosition,
        DesktopEdgeIgnoreZoneSettings desktopEdgeIgnoreZoneSettings)
    {
        if (currentDisplayMonitor is null || displayMonitors.Length == 0)
            return DesktopEdgeKind.None;

        if (!IsCursorWithinDesktopEdgeActiveVerticalRange(currentDisplayMonitor.MonitorBounds, currentCursorPosition, desktopEdgeIgnoreZoneSettings))
            return DesktopEdgeKind.None;

        var leftmostDisplayEdge = displayMonitors.Min(displayMonitorInfo => displayMonitorInfo.MonitorBounds.Left);
        if (currentDisplayMonitor.MonitorBounds.Left == leftmostDisplayEdge && currentCursorPosition.X == leftmostDisplayEdge)
            return DesktopEdgeKind.LeftOuterDisplayEdge;

        var rightmostDisplayEdge = displayMonitors.Max(displayMonitorInfo => displayMonitorInfo.MonitorBounds.Right);
        if (currentDisplayMonitor.MonitorBounds.Right == rightmostDisplayEdge && currentCursorPosition.X == rightmostDisplayEdge - 1)
            return DesktopEdgeKind.RightOuterDisplayEdge;

        return DesktopEdgeKind.None;
    }

    private static bool IsCursorWithinDesktopEdgeActiveVerticalRange(
        ScreenRectangle monitorBounds,
        ScreenPoint currentCursorPosition,
        DesktopEdgeIgnoreZoneSettings desktopEdgeIgnoreZoneSettings)
    {
        var topIgnoreHeight = (int)Math.Round(monitorBounds.Height * (desktopEdgeIgnoreZoneSettings.TopIgnorePercentage / 100d), MidpointRounding.AwayFromZero);
        var bottomIgnoreHeight = (int)Math.Round(monitorBounds.Height * (desktopEdgeIgnoreZoneSettings.BottomIgnorePercentage / 100d), MidpointRounding.AwayFromZero);
        var activeTopBoundary = Math.Clamp(monitorBounds.Top + topIgnoreHeight, monitorBounds.Top, monitorBounds.Bottom - 1);
        var activeBottomBoundary = Math.Clamp(monitorBounds.Bottom - bottomIgnoreHeight, monitorBounds.Top + 1, monitorBounds.Bottom);
        return currentCursorPosition.Y >= activeTopBoundary && currentCursorPosition.Y < activeBottomBoundary;
    }

    private static DesktopEdgeAvailabilityStatus GetDesktopEdgeAvailabilityStatus(
        DeskBorderSettings settings,
        int displayMonitorCount,
        bool hasCurrentDisplayMonitor,
        bool isCursorClipped,
        string? currentForegroundProcessName)
    {
        if (!settings.IsDeskBorderEnabled)
            return DesktopEdgeAvailabilityStatus.DisabledByDeskBorderSetting;

        if (isCursorClipped)
            return DesktopEdgeAvailabilityStatus.DisabledByCursorClipping;

        if (!hasCurrentDisplayMonitor)
            return DesktopEdgeAvailabilityStatus.CursorOutsideDisplayEnvironment;

        if (displayMonitorCount > 1 && settings.MultiDisplayBehavior == MultiDisplayBehavior.DisableInMultiDisplayEnvironment)
            return DesktopEdgeAvailabilityStatus.DisabledInMultiDisplayEnvironment;

        if (!string.IsNullOrWhiteSpace(currentForegroundProcessName)
            && settings.BlacklistedProcessNames.Contains(currentForegroundProcessName, StringComparer.OrdinalIgnoreCase))
        {
            return DesktopEdgeAvailabilityStatus.DisabledByBlacklistedProcess;
        }

        return DesktopEdgeAvailabilityStatus.Enabled;
    }

    private static bool HasStateChanged(DesktopEdgeMonitoringState previousState, DesktopEdgeMonitoringState currentState)
    {
        if (previousState.CursorPosition != currentState.CursorPosition)
            return true;

        if (previousState.CursorClippingState != currentState.CursorClippingState)
            return true;

        if (previousState.ModifierKeySnapshot != currentState.ModifierKeySnapshot)
            return true;

        if (previousState.CurrentDisplayMonitor != currentState.CurrentDisplayMonitor)
            return true;

        if (previousState.DesktopEdgeAvailabilityStatus != currentState.DesktopEdgeAvailabilityStatus)
            return true;

        if (previousState.ActiveDesktopEdge != currentState.ActiveDesktopEdge)
            return true;

        if (previousState.HasCursorEnteredDesktopEdge != currentState.HasCursorEnteredDesktopEdge)
            return true;

        if (previousState.HasCursorLeftDesktopEdge != currentState.HasCursorLeftDesktopEdge)
            return true;

        if (previousState.IsSwitchDesktopModifierSatisfied != currentState.IsSwitchDesktopModifierSatisfied)
            return true;

        if (previousState.IsCreateDesktopModifierSatisfied != currentState.IsCreateDesktopModifierSatisfied)
            return true;

        if (previousState.NavigatorTriggerState != currentState.NavigatorTriggerState)
            return true;

        return !HaveSameDisplayMonitors(previousState.DisplayMonitors, currentState.DisplayMonitors);
    }

    private static bool HaveSameDisplayMonitors(DisplayMonitorInfo[] previousDisplayMonitors, DisplayMonitorInfo[] currentDisplayMonitors)
    {
        if (ReferenceEquals(previousDisplayMonitors, currentDisplayMonitors))
            return true;

        if (previousDisplayMonitors.Length != currentDisplayMonitors.Length)
            return false;

        for (var index = 0; index < previousDisplayMonitors.Length; index++)
        {
            if (previousDisplayMonitors[index] != currentDisplayMonitors[index])
                return false;
        }

        return true;
    }

    private static DisplayMonitorInfo? FindCurrentDisplayMonitor(DisplayMonitorInfo[] displayMonitors, ScreenPoint currentCursorPosition)
    {
        foreach (var displayMonitor in displayMonitors)
        {
            if (displayMonitor.MonitorBounds.Contains(currentCursorPosition))
                return displayMonitor;
        }

        return null;
    }

    private async Task RunMonitoringLoopAsync(CancellationToken cancellationToken)
    {
        using var periodicTimer = new PeriodicTimer(s_defaultPollingInterval);
        while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
            Refresh();
    }

    private void OnSettingsServiceSettingsChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        Refresh();
    }
}
