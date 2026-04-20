using DeskBorder.Helpers;
using DeskBorder.Models;
using System.Collections.Concurrent;

namespace DeskBorder.Services;

public sealed class DesktopEdgeMonitorService(ISettingsService settingsService, IFileLogService fileLogService) : IDesktopEdgeMonitorService, IDisposable
{
    private static readonly TimeSpan s_defaultPollingInterval = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan s_refreshFailureLoggingWindow = TimeSpan.FromSeconds(2);
    private static readonly ConcurrentDictionary<string, string> s_autoBlacklistedGameBarExecutablePaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> s_persistingGameBarExecutablePaths = new(StringComparer.OrdinalIgnoreCase);

    private readonly IFileLogService _fileLogService = fileLogService;
    private readonly ISettingsService _settingsService = settingsService;
    private CancellationTokenSource? _monitoringCancellationTokenSource;
    private Task? _monitoringTask;
    private bool _isDisposed;
    private DateTimeOffset _lastRefreshFailureLoggedAt;
    private string? _lastRefreshFailureSignature;

    public event EventHandler<DesktopEdgeMonitoringStateChangedEventArgs>? MonitoringStateChanged;

    public bool IsMonitoring => _monitoringTask is not null;

    public DesktopEdgeMonitoringState CurrentState { get; private set; } = new();

    public DesktopEdgeMonitoringState CaptureCurrentState()
    {
        var currentSettings = _settingsService.Settings;
        var currentCursorPosition = MouseHelper.GetCurrentCursorPosition();
        var cursorClippingState = MouseHelper.GetCursorClippingState();
        var foregroundProcessSnapshot = MouseHelper.GetForegroundProcessSnapshot();
        var modifierKeySnapshot = MouseHelper.GetModifierKeySnapshot();
        var mouseButtonSnapshot = MouseHelper.GetMouseButtonSnapshot();
        var displayMonitors = MouseHelper.GetDisplayMonitors();
        var currentDisplayMonitor = FindCurrentDisplayMonitor(displayMonitors, currentCursorPosition);
        var isForegroundProcessGameBarRecognizedGame = !string.IsNullOrWhiteSpace(foregroundProcessSnapshot.ExecutablePath)
            && MouseHelper.IsGameBarRecognizedGame(foregroundProcessSnapshot.ExecutablePath);
        QueueGameBarRecognizedProcessAutoBlacklistPersistence(foregroundProcessSnapshot, currentSettings, isForegroundProcessGameBarRecognizedGame);
        var isForegroundProcessBlacklisted = IsForegroundProcessBlacklisted(
            currentSettings.BlacklistedProcessNames,
            currentSettings.WhitelistedProcessNames,
            foregroundProcessSnapshot,
            isForegroundProcessGameBarRecognizedGame);
        var desktopEdgeAvailabilityStatus = GetDesktopEdgeAvailabilityStatus(
            currentSettings,
            displayMonitors.Length,
            currentDisplayMonitor is not null,
            cursorClippingState.IsCursorClipped,
            mouseButtonSnapshot.IsAnyButtonPressed,
            isForegroundProcessBlacklisted);
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
            MouseButtonSnapshot = mouseButtonSnapshot,
            ForegroundProcessSnapshot = foregroundProcessSnapshot,
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

        _fileLogService.WriteInformation(nameof(DesktopEdgeMonitorService), "Disposing desktop edge monitor service.");
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

        LogAvailabilityStatusChange(previousState, currentState);
        MonitoringStateChanged?.Invoke(this, new(previousState, currentState));
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_monitoringTask is not null)
            return Task.CompletedTask;

        _fileLogService.WriteInformation(nameof(DesktopEdgeMonitorService), "Starting desktop edge monitoring.");
        _settingsService.SettingsChanged += OnSettingsServiceSettingsChanged;
        try { Refresh(); }
        catch (Exception exception) { LogRefreshFailure("Initial refresh failed.", exception); }
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

        try
        {
            await _monitoringTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _fileLogService.WriteWarning(nameof(DesktopEdgeMonitorService), "Monitoring loop completed with an exception during stop.", exception);
        }
        finally
        {
            _monitoringCancellationTokenSource?.Dispose();
            _monitoringCancellationTokenSource = null;
            _monitoringTask = null;
            _fileLogService.WriteInformation(nameof(DesktopEdgeMonitorService), "Stopped desktop edge monitoring.");
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
        bool isAnyMouseButtonPressed,
        bool isForegroundProcessBlacklisted)
    {
        if (!settings.IsDeskBorderEnabled)
            return DesktopEdgeAvailabilityStatus.DisabledByDeskBorderSetting;

        if (isCursorClipped)
            return DesktopEdgeAvailabilityStatus.DisabledByCursorClipping;

        if (isAnyMouseButtonPressed)
            return DesktopEdgeAvailabilityStatus.DisabledByPressedMouseButton;

        if (!hasCurrentDisplayMonitor)
            return DesktopEdgeAvailabilityStatus.CursorOutsideDisplayEnvironment;

        if (displayMonitorCount > 1 && settings.MultiDisplayBehavior == MultiDisplayBehavior.DisableInMultiDisplayEnvironment)
            return DesktopEdgeAvailabilityStatus.DisabledInMultiDisplayEnvironment;

        if (isForegroundProcessBlacklisted)
            return DesktopEdgeAvailabilityStatus.DisabledByBlacklistedProcess;

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

        if (previousState.MouseButtonSnapshot != currentState.MouseButtonSnapshot)
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
        {
            try { Refresh(); }
            catch (Exception exception) { LogRefreshFailure("Monitoring refresh failed.", exception); }
        }
    }

    private void OnSettingsServiceSettingsChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        try { Refresh(); }
        catch (Exception exception) { LogRefreshFailure("Settings change refresh failed.", exception); }
    }

    private void LogRefreshFailure(string message, Exception exception)
    {
        var currentTimestamp = DateTimeOffset.UtcNow;
        var exceptionSignature = $"{exception.GetType().FullName}:{exception.Message}";
        if (string.Equals(_lastRefreshFailureSignature, exceptionSignature, StringComparison.Ordinal)
            && currentTimestamp - _lastRefreshFailureLoggedAt < s_refreshFailureLoggingWindow)
        {
            return;
        }

        _lastRefreshFailureSignature = exceptionSignature;
        _lastRefreshFailureLoggedAt = currentTimestamp;
        _fileLogService.WriteWarning(nameof(DesktopEdgeMonitorService), message, exception);
    }

    private void LogAvailabilityStatusChange(DesktopEdgeMonitoringState previousState, DesktopEdgeMonitoringState currentState)
    {
        if (previousState.DesktopEdgeAvailabilityStatus == currentState.DesktopEdgeAvailabilityStatus)
            return;

        if (currentState.DesktopEdgeAvailabilityStatus == DesktopEdgeAvailabilityStatus.DisabledByBlacklistedProcess
            && !string.IsNullOrWhiteSpace(currentState.ForegroundProcessSnapshot.ExecutablePath))
        {
            var wasAutoBlacklistedByGameBar = s_autoBlacklistedGameBarExecutablePaths.ContainsKey(currentState.ForegroundProcessSnapshot.ExecutablePath);
            if (wasAutoBlacklistedByGameBar)
            {
                _fileLogService.WriteInformation(
                    nameof(DesktopEdgeMonitorService),
                    $"Desktop edge monitoring availability changed to {currentState.DesktopEdgeAvailabilityStatus}. ForegroundExecutablePath={currentState.ForegroundProcessSnapshot.ExecutablePath}.");
                return;
            }
        }

        _fileLogService.WriteInformation(
            nameof(DesktopEdgeMonitorService),
            $"Desktop edge monitoring availability changed from {previousState.DesktopEdgeAvailabilityStatus} to {currentState.DesktopEdgeAvailabilityStatus}.");
    }

    private bool IsForegroundProcessBlacklisted(
        IReadOnlyList<string> blacklistedProcessNames,
        IReadOnlyList<string> whitelistedProcessNames,
        ForegroundProcessSnapshot foregroundProcessSnapshot,
        bool isForegroundProcessGameBarRecognizedGame)
    {
        if (!string.IsNullOrWhiteSpace(foregroundProcessSnapshot.ProcessName)
            && whitelistedProcessNames.Contains(foregroundProcessSnapshot.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(foregroundProcessSnapshot.ProcessName)
            && blacklistedProcessNames.Contains(foregroundProcessSnapshot.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!isForegroundProcessGameBarRecognizedGame
            || string.IsNullOrWhiteSpace(foregroundProcessSnapshot.ProcessName)
            || string.IsNullOrWhiteSpace(foregroundProcessSnapshot.ExecutablePath))
        {
            return false;
        }

        if (s_autoBlacklistedGameBarExecutablePaths.TryAdd(foregroundProcessSnapshot.ExecutablePath, foregroundProcessSnapshot.ProcessName))
        {
            _fileLogService.WriteInformation(
                nameof(DesktopEdgeMonitorService),
                $"Auto-registered Game Bar recognized foreground process to the runtime blacklist. ProcessName={foregroundProcessSnapshot.ProcessName}, ExecutablePath={foregroundProcessSnapshot.ExecutablePath}.");
        }

        return true;
    }

    private void QueueGameBarRecognizedProcessAutoBlacklistPersistence(
        ForegroundProcessSnapshot foregroundProcessSnapshot,
        DeskBorderSettings currentSettings,
        bool isForegroundProcessGameBarRecognizedGame)
    {
        if (!isForegroundProcessGameBarRecognizedGame
            || string.IsNullOrWhiteSpace(foregroundProcessSnapshot.ProcessName)
            || string.IsNullOrWhiteSpace(foregroundProcessSnapshot.ExecutablePath))
        {
            return;
        }

        if (currentSettings.WhitelistedProcessNames.Contains(foregroundProcessSnapshot.ProcessName, StringComparer.OrdinalIgnoreCase)
            || currentSettings.BlacklistedProcessNames.Contains(foregroundProcessSnapshot.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        if (!s_persistingGameBarExecutablePaths.TryAdd(foregroundProcessSnapshot.ExecutablePath, 0))
            return;

        _ = PersistGameBarRecognizedProcessToBlacklistAsync(foregroundProcessSnapshot);
    }

    private async Task PersistGameBarRecognizedProcessToBlacklistAsync(ForegroundProcessSnapshot foregroundProcessSnapshot)
    {
        try
        {
            var currentSettings = _settingsService.Settings;
            if (string.IsNullOrWhiteSpace(foregroundProcessSnapshot.ProcessName)
                || string.IsNullOrWhiteSpace(foregroundProcessSnapshot.ExecutablePath)
                || currentSettings.WhitelistedProcessNames.Contains(foregroundProcessSnapshot.ProcessName, StringComparer.OrdinalIgnoreCase)
                || currentSettings.BlacklistedProcessNames.Contains(foregroundProcessSnapshot.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            await _settingsService.UpdateSettingsAsync(currentSettings with
            {
                BlacklistedProcessNames = [.. currentSettings.BlacklistedProcessNames, foregroundProcessSnapshot.ProcessName]
            });
            _fileLogService.WriteInformation(
                nameof(DesktopEdgeMonitorService),
                $"Persisted Game Bar recognized foreground process to blacklist. ProcessName={foregroundProcessSnapshot.ProcessName}, ExecutablePath={foregroundProcessSnapshot.ExecutablePath}.");
        }
        catch (ArgumentException exception)
        {
            _fileLogService.WriteWarning(nameof(DesktopEdgeMonitorService), "Failed to persist Game Bar recognized foreground process to blacklist because the process name was invalid.", exception);
        }
        catch (InvalidOperationException exception)
        {
            _fileLogService.WriteWarning(nameof(DesktopEdgeMonitorService), "Failed to persist Game Bar recognized foreground process to blacklist because settings update was rejected.", exception);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(foregroundProcessSnapshot.ExecutablePath))
                _ = s_persistingGameBarExecutablePaths.TryRemove(foregroundProcessSnapshot.ExecutablePath, out _);
        }
    }
}
