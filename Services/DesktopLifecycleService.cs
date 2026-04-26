using DeskBorder.Helpers;
using DeskBorder.Models;
using System.ComponentModel;

namespace DeskBorder.Services;

public sealed class DesktopLifecycleService(
    IDesktopEdgeMonitorService desktopEdgeMonitorService,
    IFileLogService fileLogService,
    IHotkeyService hotkeyService,
    ILocalizationService localizationService,
    INavigatorService navigatorService,
    ISettingsService settingsService,
    IToastService toastService,
    IVirtualDesktopService virtualDesktopService) : IDesktopLifecycleService
{
    private const int DesktopEdgeTriggerRearmDistanceInPixels = 24;
    private const int DesktopSwitchMouseLocationApplyAttemptCount = 5;
    private static readonly TimeSpan s_desktopSwitchMouseLocationApplyRetryDelay = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan s_desktopSwitchMouseLocationVerificationDelay = TimeSpan.FromMilliseconds(40);

    private readonly IDesktopEdgeMonitorService _desktopEdgeMonitorService = desktopEdgeMonitorService;
    private readonly IFileLogService _fileLogService = fileLogService;
    private readonly IHotkeyService _hotkeyService = hotkeyService;
    private readonly ILocalizationService _localizationService = localizationService;
    private readonly INavigatorService _navigatorService = navigatorService;
    private readonly ISettingsService _settingsService = settingsService;
    private readonly IToastService _toastService = toastService;
    private readonly IVirtualDesktopService _virtualDesktopService = virtualDesktopService;
    private readonly SemaphoreSlim _operationSemaphore = new(1, 1);
    private CancellationTokenSource? _pendingDesktopDeletionCancellationTokenSource;
    private Task? _pendingDesktopDeletionTask;
    private bool _isDesktopEdgeActivationArmed = true;
    private DesktopEdgeKind _lastTriggeredDesktopEdge = DesktopEdgeKind.None;

    public bool IsRunning { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            return;

        _fileLogService.WriteInformation(nameof(DesktopLifecycleService), "Starting desktop lifecycle service.");
        _isDesktopEdgeActivationArmed = true;
        _lastTriggeredDesktopEdge = DesktopEdgeKind.None;
        if (!_hotkeyService.IsInitialized)
            _hotkeyService.Initialize();

        _desktopEdgeMonitorService.MonitoringStateChanged += OnDesktopEdgeMonitorServiceMonitoringStateChanged;
        _hotkeyService.HotkeyInvoked += OnHotkeyServiceHotkeyInvoked;
        _localizationService.LanguageChanged += OnLocalizationServiceLanguageChanged;
        _settingsService.SettingsChanged += OnSettingsServiceSettingsChanged;

        await RefreshNavigatorAsync();
        await _desktopEdgeMonitorService.StartAsync(cancellationToken);
        IsRunning = true;
        _fileLogService.WriteInformation(nameof(DesktopLifecycleService), "Desktop lifecycle service started.");
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
            return;

        _fileLogService.WriteInformation(nameof(DesktopLifecycleService), "Stopping desktop lifecycle service.");
        IsRunning = false;
        _isDesktopEdgeActivationArmed = true;
        _lastTriggeredDesktopEdge = DesktopEdgeKind.None;
        _desktopEdgeMonitorService.MonitoringStateChanged -= OnDesktopEdgeMonitorServiceMonitoringStateChanged;
        _hotkeyService.HotkeyInvoked -= OnHotkeyServiceHotkeyInvoked;
        _localizationService.LanguageChanged -= OnLocalizationServiceLanguageChanged;
        _settingsService.SettingsChanged -= OnSettingsServiceSettingsChanged;

        await CancelPendingDesktopDeletionAsync();
        await _desktopEdgeMonitorService.StopAsync();
        await UiThreadHelper.ExecuteAsync(_navigatorService.Hide);
        _fileLogService.WriteInformation(nameof(DesktopLifecycleService), "Desktop lifecycle service stopped.");
    }

    private static DesktopSwitchDirection ConvertToDesktopSwitchDirection(DesktopEdgeKind desktopEdgeKind, bool isVerticalDesktopSwitchDirectionReversed) => desktopEdgeKind switch
    {
        DesktopEdgeKind.LeftOuterDisplayEdge => DesktopSwitchDirection.Previous,
        DesktopEdgeKind.RightOuterDisplayEdge => DesktopSwitchDirection.Next,
        DesktopEdgeKind.TopDisplayEdge => isVerticalDesktopSwitchDirectionReversed ? DesktopSwitchDirection.Next : DesktopSwitchDirection.Previous,
        DesktopEdgeKind.BottomDisplayEdge => isVerticalDesktopSwitchDirectionReversed ? DesktopSwitchDirection.Previous : DesktopSwitchDirection.Next,
        _ => throw new InvalidOperationException("The requested desktop edge kind is not supported.")
    };

    private static DesktopNavigationResult CreateNoOperationNavigationResult(VirtualDesktopWorkspaceSnapshot workspaceSnapshot) => new()
    {
        PreviousWorkspaceSnapshot = workspaceSnapshot,
        CurrentWorkspaceSnapshot = workspaceSnapshot,
        SourceDesktopIdentifier = workspaceSnapshot.CurrentDesktopIdentifier
    };

    private static bool IsInwardSwitch(DesktopNavigationResult desktopNavigationResult)
    {
        var sourceDesktopEntry = desktopNavigationResult.PreviousWorkspaceSnapshot.DesktopEntries.FirstOrDefault(desktopEntry => string.Equals(desktopEntry.DesktopIdentifier, desktopNavigationResult.SourceDesktopIdentifier, StringComparison.Ordinal));
        var targetDesktopEntry = desktopNavigationResult.CurrentWorkspaceSnapshot.DesktopEntries.FirstOrDefault(desktopEntry => string.Equals(desktopEntry.DesktopIdentifier, desktopNavigationResult.TargetDesktopIdentifier, StringComparison.Ordinal));
        if (sourceDesktopEntry is null || targetDesktopEntry is null)
            return false;

        return sourceDesktopEntry.IsLeftOuterDesktop && targetDesktopEntry.DesktopNumber == sourceDesktopEntry.DesktopNumber + 1
            || sourceDesktopEntry.IsRightOuterDesktop && targetDesktopEntry.DesktopNumber == sourceDesktopEntry.DesktopNumber - 1;
    }

    private static bool HasPointerMovedAwayFromDesktopEdge(DesktopEdgeMonitoringState currentState, DesktopEdgeKind lastTriggeredDesktopEdge)
    {
        if (currentState.CurrentDisplayMonitor is null || currentState.DisplayMonitors.Length == 0)
            return false;

        return lastTriggeredDesktopEdge switch
        {
            DesktopEdgeKind.LeftOuterDisplayEdge => currentState.CursorPosition.X >= currentState.DisplayMonitors.Min(displayMonitor => displayMonitor.MonitorBounds.Left) + DesktopEdgeTriggerRearmDistanceInPixels,
            DesktopEdgeKind.RightOuterDisplayEdge => currentState.CursorPosition.X <= currentState.DisplayMonitors.Max(displayMonitor => displayMonitor.MonitorBounds.Right) - DesktopEdgeTriggerRearmDistanceInPixels - 1,
            DesktopEdgeKind.TopDisplayEdge => currentState.CursorPosition.Y >= currentState.CurrentDisplayMonitor.MonitorBounds.Top + DesktopEdgeTriggerRearmDistanceInPixels,
            DesktopEdgeKind.BottomDisplayEdge => currentState.CursorPosition.Y <= currentState.CurrentDisplayMonitor.MonitorBounds.Bottom - DesktopEdgeTriggerRearmDistanceInPixels - 1,
            _ => false
        };
    }

    private static bool HaveDisplayMonitorsChanged(DisplayMonitorInfo[] previousDisplayMonitors, DisplayMonitorInfo[] currentDisplayMonitors)
    {
        if (ReferenceEquals(previousDisplayMonitors, currentDisplayMonitors))
            return false;

        if (previousDisplayMonitors.Length != currentDisplayMonitors.Length)
            return true;

        for (var index = 0; index < previousDisplayMonitors.Length; index++)
        {
            if (previousDisplayMonitors[index] != currentDisplayMonitors[index])
                return true;
        }

        return false;
    }

    private static bool IsCurrentDesktopLeftOuter(VirtualDesktopWorkspaceSnapshot workspaceSnapshot) => workspaceSnapshot.CurrentDesktopNumber == 1;

    private static bool IsCurrentDesktopRightOuter(VirtualDesktopWorkspaceSnapshot workspaceSnapshot) => workspaceSnapshot.DesktopEntries.Length > 0
        && workspaceSnapshot.CurrentDesktopNumber == workspaceSnapshot.DesktopEntries.Length;

    private static bool IsCurrentDesktopOuterForDesktopSwitchDirection(VirtualDesktopWorkspaceSnapshot workspaceSnapshot, DesktopSwitchDirection desktopSwitchDirection) => desktopSwitchDirection switch
    {
        DesktopSwitchDirection.Previous => IsCurrentDesktopLeftOuter(workspaceSnapshot),
        DesktopSwitchDirection.Next => IsCurrentDesktopRightOuter(workspaceSnapshot),
        _ => false
    };

    private static ScreenRectangle CreateCombinedMonitorBounds(DisplayMonitorInfo[] displayMonitors)
    {
        var left = displayMonitors.Min(displayMonitor => displayMonitor.MonitorBounds.Left);
        var top = displayMonitors.Min(displayMonitor => displayMonitor.MonitorBounds.Top);
        var right = displayMonitors.Max(displayMonitor => displayMonitor.MonitorBounds.Right);
        var bottom = displayMonitors.Max(displayMonitor => displayMonitor.MonitorBounds.Bottom);
        return new(left, top, right, bottom);
    }

    private static DesktopSwitchMouseLocationContext CreateDesktopSwitchMouseLocationContext(DesktopEdgeMonitoringState currentState, DesktopSwitchDirection desktopSwitchDirection) => new(
        currentState.DisplayMonitors,
        currentState.CurrentDisplayMonitor,
        currentState.CursorPosition,
        desktopSwitchDirection,
        currentState.ActiveDesktopEdge);

    private static ScreenPoint CreateScreenRectangleCenterPoint(ScreenRectangle screenRectangle) => new(
        screenRectangle.Left + screenRectangle.Width / 2,
        screenRectangle.Top + screenRectangle.Height / 2);

    private static DisplayMonitorInfo? FindDisplayMonitor(DisplayMonitorInfo[] displayMonitors, ScreenPoint screenPoint) => displayMonitors.FirstOrDefault(displayMonitor => displayMonitor.MonitorBounds.Contains(screenPoint));

    private static DisplayMonitorInfo? FindPrimaryDisplayMonitor(DisplayMonitorInfo[] displayMonitors) => displayMonitors.FirstOrDefault(displayMonitor => displayMonitor.IsPrimaryDisplay);

    private static ScreenPoint? TryCreateHorizontalOppositeSideMouseLocation(DesktopSwitchMouseLocationContext desktopSwitchMouseLocationContext)
    {
        if (desktopSwitchMouseLocationContext.DisplayMonitors.Length == 0)
            return null;

        var leftmostDisplayEdge = desktopSwitchMouseLocationContext.DisplayMonitors.Min(displayMonitor => displayMonitor.MonitorBounds.Left);
        var rightmostDisplayEdge = desktopSwitchMouseLocationContext.DisplayMonitors.Max(displayMonitor => displayMonitor.MonitorBounds.Right);
        var newX = desktopSwitchMouseLocationContext.DesktopSwitchDirection == DesktopSwitchDirection.Next
            ? leftmostDisplayEdge + DesktopEdgeTriggerRearmDistanceInPixels
            : rightmostDisplayEdge - DesktopEdgeTriggerRearmDistanceInPixels;
        var newY = MapCursorVerticalPosition(
            desktopSwitchMouseLocationContext.DisplayMonitors,
            desktopSwitchMouseLocationContext.InputDisplayMonitor,
            desktopSwitchMouseLocationContext.InputCursorPosition.Y,
            newX);
        return new(newX, newY);
    }

    private static ScreenPoint? TryCreateOppositeSideMouseLocation(DesktopSwitchMouseLocationContext desktopSwitchMouseLocationContext) => desktopSwitchMouseLocationContext.TriggeredDesktopEdge switch
    {
        DesktopEdgeKind.TopDisplayEdge => TryCreateVerticalOppositeSideMouseLocation(desktopSwitchMouseLocationContext, isTopEdgeTriggered: true),
        DesktopEdgeKind.BottomDisplayEdge => TryCreateVerticalOppositeSideMouseLocation(desktopSwitchMouseLocationContext, isTopEdgeTriggered: false),
        _ => TryCreateHorizontalOppositeSideMouseLocation(desktopSwitchMouseLocationContext)
    };

    private static int MapCursorVerticalPosition(DisplayMonitorInfo[] displayMonitors, DisplayMonitorInfo? sourceMonitor, int cursorY, int targetX)
    {
        if (sourceMonitor is null) return cursorY;

        var targetMonitor = displayMonitors.FirstOrDefault(monitor => targetX >= monitor.MonitorBounds.Left && targetX < monitor.MonitorBounds.Right);
        if (targetMonitor is null || targetMonitor == sourceMonitor) return cursorY;

        var relativeVerticalPosition = (double)(cursorY - sourceMonitor.MonitorBounds.Top) / sourceMonitor.MonitorBounds.Height;
        var mappedY = targetMonitor.MonitorBounds.Top + (int)Math.Round(relativeVerticalPosition * targetMonitor.MonitorBounds.Height, MidpointRounding.AwayFromZero);
        return Math.Clamp(mappedY, targetMonitor.MonitorBounds.Top, targetMonitor.MonitorBounds.Bottom - 1);
    }

    private static ScreenPoint? TryCreateVerticalOppositeSideMouseLocation(DesktopSwitchMouseLocationContext desktopSwitchMouseLocationContext, bool isTopEdgeTriggered)
    {
        if (desktopSwitchMouseLocationContext.InputDisplayMonitor is not { } inputDisplayMonitor)
            return null;

        var newY = isTopEdgeTriggered
            ? Math.Max(inputDisplayMonitor.MonitorBounds.Top, inputDisplayMonitor.MonitorBounds.Bottom - DesktopEdgeTriggerRearmDistanceInPixels)
            : Math.Min(inputDisplayMonitor.MonitorBounds.Bottom - 1, inputDisplayMonitor.MonitorBounds.Top + DesktopEdgeTriggerRearmDistanceInPixels);
        var newX = Math.Clamp(
            desktopSwitchMouseLocationContext.InputCursorPosition.X,
            inputDisplayMonitor.MonitorBounds.Left,
            inputDisplayMonitor.MonitorBounds.Right - 1);
        return new(newX, newY);
    }

    private static ScreenPoint? TryResolveMouseLocationAfterDesktopSwitch(DesktopSwitchMouseLocationOption desktopSwitchMouseLocationOption, DesktopSwitchMouseLocationContext desktopSwitchMouseLocationContext) => desktopSwitchMouseLocationOption switch
    {
        DesktopSwitchMouseLocationOption.OppositeSide => TryCreateOppositeSideMouseLocation(desktopSwitchMouseLocationContext),
        DesktopSwitchMouseLocationOption.VirtualScreenCenter => desktopSwitchMouseLocationContext.DisplayMonitors.Length == 0
            ? null
            : CreateScreenRectangleCenterPoint(CreateCombinedMonitorBounds(desktopSwitchMouseLocationContext.DisplayMonitors)),
        DesktopSwitchMouseLocationOption.PrimaryMonitorCenter => FindPrimaryDisplayMonitor(desktopSwitchMouseLocationContext.DisplayMonitors) is { } primaryDisplayMonitor
            ? CreateScreenRectangleCenterPoint(primaryDisplayMonitor.MonitorBounds)
            : null,
        DesktopSwitchMouseLocationOption.TargetMonitorCenter => TryCreateOppositeSideMouseLocation(desktopSwitchMouseLocationContext) is { } targetMouseLocation
            && FindDisplayMonitor(desktopSwitchMouseLocationContext.DisplayMonitors, targetMouseLocation) is { } targetDisplayMonitor
                ? CreateScreenRectangleCenterPoint(targetDisplayMonitor.MonitorBounds)
                : null,
        DesktopSwitchMouseLocationOption.InputMonitorCenter => desktopSwitchMouseLocationContext.InputDisplayMonitor is { } inputDisplayMonitor
            ? CreateScreenRectangleCenterPoint(inputDisplayMonitor.MonitorBounds)
            : null,
        DesktopSwitchMouseLocationOption.DoNotMove => desktopSwitchMouseLocationContext.InputCursorPosition,
        _ => null
    };

    private static bool HasActivationModifierBecomeSatisfiedWhileRemainingOnDesktopEdge(DesktopEdgeMonitoringState previousState, DesktopEdgeMonitoringState currentState)
        => previousState.ActiveDesktopEdge == currentState.ActiveDesktopEdge
        && currentState.ActiveDesktopEdge != DesktopEdgeKind.None
        && ((!previousState.IsSwitchDesktopModifierSatisfied && currentState.IsSwitchDesktopModifierSatisfied)
            || (!previousState.IsCreateDesktopModifierSatisfied && currentState.IsCreateDesktopModifierSatisfied));

    private static bool ShouldHandleDesktopEdgeActivation(DesktopEdgeMonitoringState previousState, DesktopEdgeMonitoringState currentState, bool isDesktopEdgeActivationArmed)
        => isDesktopEdgeActivationArmed
        && currentState.ActiveDesktopEdge != DesktopEdgeKind.None
        && (currentState.HasCursorEnteredDesktopEdge || HasActivationModifierBecomeSatisfiedWhileRemainingOnDesktopEdge(previousState, currentState));

    private static string FormatLastWindowsErrorDetails(int lastWindowsErrorCode) => $"LastWindowsErrorCode={lastWindowsErrorCode} (0x{lastWindowsErrorCode:X8}, {new Win32Exception(lastWindowsErrorCode).Message})";

    private static string FormatCursorClippingDetails()
    {
        try
        {
            var cursorClippingState = MouseHelper.GetCursorClippingState();
            return $"CursorClipped={cursorClippingState.IsCursorClipped}, ClippingLeft={cursorClippingState.ClippingRectangle.Left}, ClippingTop={cursorClippingState.ClippingRectangle.Top}, ClippingRight={cursorClippingState.ClippingRectangle.Right}, ClippingBottom={cursorClippingState.ClippingRectangle.Bottom}, VirtualScreenLeft={cursorClippingState.VirtualScreenBounds.Left}, VirtualScreenTop={cursorClippingState.VirtualScreenBounds.Top}, VirtualScreenRight={cursorClippingState.VirtualScreenBounds.Right}, VirtualScreenBottom={cursorClippingState.VirtualScreenBounds.Bottom}";
        }
        catch (InvalidOperationException exception) { return $"CursorClippingStateUnavailable={exception.Message}"; }
    }

    private DesktopSwitchMouseLocationContext? TryCreateHotkeyDesktopSwitchMouseLocationContext(DesktopSwitchDirection desktopSwitchDirection)
    {
        try
        {
            var inputCursorPosition = MouseHelper.GetCurrentCursorPosition();
            var displayMonitors = MouseHelper.GetDisplayMonitors();
            return new(
                displayMonitors,
                FindDisplayMonitor(displayMonitors, inputCursorPosition),
                inputCursorPosition,
                desktopSwitchDirection,
                DesktopEdgeKind.None);
        }
        catch (InvalidOperationException exception)
        {
            _fileLogService.WriteWarning(nameof(DesktopLifecycleService), $"Failed to capture cursor context for hotkey desktop switch. Direction={desktopSwitchDirection}.", exception);
            return null;
        }
    }

    private static async Task<DesktopSwitchMouseLocationApplyResult> ApplyResolvedMouseLocationAfterDesktopSwitchAsync(ScreenPoint targetMouseLocation)
    {
        var lastMouseLocationApplyResult = default(DesktopSwitchMouseLocationApplyResult);
        for (var attemptNumber = 1; attemptNumber <= DesktopSwitchMouseLocationApplyAttemptCount; attemptNumber++)
        {
            if (!MouseHelper.TrySetCursorPosition(targetMouseLocation, out var setCursorPositionLastWindowsErrorCode))
                lastMouseLocationApplyResult = new(attemptNumber, false, false, false, null, setCursorPositionLastWindowsErrorCode, 0);
            else
            {
                await Task.Delay(s_desktopSwitchMouseLocationVerificationDelay);
                if (!MouseHelper.TryGetCurrentCursorPosition(out var actualMouseLocation, out var getCursorPositionLastWindowsErrorCode))
                    lastMouseLocationApplyResult = new(attemptNumber, true, false, false, null, 0, getCursorPositionLastWindowsErrorCode);
                else
                {
                    lastMouseLocationApplyResult = new(attemptNumber, true, true, actualMouseLocation == targetMouseLocation, actualMouseLocation, 0, 0);
                    if (lastMouseLocationApplyResult.IsSuccessful) return lastMouseLocationApplyResult;
                }
            }

            if (attemptNumber < DesktopSwitchMouseLocationApplyAttemptCount) await Task.Delay(s_desktopSwitchMouseLocationApplyRetryDelay);
        }

        return lastMouseLocationApplyResult;
    }

    private async Task TryApplyMouseLocationAfterDesktopSwitchAsync(
        DesktopNavigationResult desktopNavigationResult,
        DesktopSwitchMouseLocationOption desktopSwitchMouseLocationOption,
        DesktopSwitchMouseLocationContext? desktopSwitchMouseLocationContext,
        string triggerSource)
    {
        if (!desktopNavigationResult.IsSuccessful
            || desktopNavigationResult.NavigationActionKind is not (DesktopNavigationActionKind.Switched or DesktopNavigationActionKind.CreatedAndSwitched)
            || desktopSwitchMouseLocationOption == DesktopSwitchMouseLocationOption.DoNotMove
            || desktopSwitchMouseLocationContext is null)
        {
            return;
        }

        var targetMouseLocation = TryResolveMouseLocationAfterDesktopSwitch(desktopSwitchMouseLocationOption, desktopSwitchMouseLocationContext.Value);
        if (targetMouseLocation is null)
        {
            _fileLogService.WriteWarning(nameof(DesktopLifecycleService), $"Failed to resolve mouse location after desktop switch. TriggerSource={triggerSource}, Option={desktopSwitchMouseLocationOption}.");
            return;
        }

        var mouseLocationApplyResult = await ApplyResolvedMouseLocationAfterDesktopSwitchAsync(targetMouseLocation.Value);
        if (mouseLocationApplyResult.IsSuccessful)
        {
            _fileLogService.WriteInformation(nameof(DesktopLifecycleService), $"Applied mouse location after desktop switch. TriggerSource={triggerSource}, Option={desktopSwitchMouseLocationOption}, RequestedX={targetMouseLocation.Value.X}, RequestedY={targetMouseLocation.Value.Y}, ActualX={mouseLocationApplyResult.ActualMouseLocation!.Value.X}, ActualY={mouseLocationApplyResult.ActualMouseLocation!.Value.Y}, AttemptNumber={mouseLocationApplyResult.AttemptNumber}.");
            return;
        }

        if (!mouseLocationApplyResult.DidSetCursorPosition)
        {
            _fileLogService.WriteError(nameof(DesktopLifecycleService), $"Failed to apply mouse location after desktop switch. TriggerSource={triggerSource}, Option={desktopSwitchMouseLocationOption}, RequestedX={targetMouseLocation.Value.X}, RequestedY={targetMouseLocation.Value.Y}, AttemptNumber={mouseLocationApplyResult.AttemptNumber}, {FormatLastWindowsErrorDetails(mouseLocationApplyResult.SetCursorPositionLastWindowsErrorCode)}, {FormatCursorClippingDetails()}.");
            return;
        }

        if (!mouseLocationApplyResult.DidReadActualMouseLocation)
        {
            _fileLogService.WriteWarning(nameof(DesktopLifecycleService), $"Could not verify mouse location after desktop switch. TriggerSource={triggerSource}, Option={desktopSwitchMouseLocationOption}, RequestedX={targetMouseLocation.Value.X}, RequestedY={targetMouseLocation.Value.Y}, AttemptNumber={mouseLocationApplyResult.AttemptNumber}, GetCurrentCursorPosition{FormatLastWindowsErrorDetails(mouseLocationApplyResult.GetCursorPositionLastWindowsErrorCode)}, {FormatCursorClippingDetails()}.");
            return;
        }

        _fileLogService.WriteWarning(nameof(DesktopLifecycleService), $"Mouse location after desktop switch was constrained or changed after SetCursorPos. TriggerSource={triggerSource}, Option={desktopSwitchMouseLocationOption}, RequestedX={targetMouseLocation.Value.X}, RequestedY={targetMouseLocation.Value.Y}, ActualX={mouseLocationApplyResult.ActualMouseLocation!.Value.X}, ActualY={mouseLocationApplyResult.ActualMouseLocation!.Value.Y}, AttemptNumber={mouseLocationApplyResult.AttemptNumber}, {FormatCursorClippingDetails()}.");
    }

    private async Task CancelPendingDesktopDeletionAsync()
    {
        var hadPendingDesktopDeletion = _pendingDesktopDeletionCancellationTokenSource is not null || _pendingDesktopDeletionTask is not null;
        _pendingDesktopDeletionCancellationTokenSource?.Cancel();
        if (_pendingDesktopDeletionTask is not null)
        {
            try { await _pendingDesktopDeletionTask; }
            catch (OperationCanceledException)
            {
                _fileLogService.WriteInformation(nameof(DesktopLifecycleService), "Pending desktop deletion task was canceled.");
            }
        }

        _pendingDesktopDeletionCancellationTokenSource?.Dispose();
        _pendingDesktopDeletionCancellationTokenSource = null;
        _pendingDesktopDeletionTask = null;
        if (hadPendingDesktopDeletion)
            await _toastService.DismissAsync();
    }

    private void ConsumeKeyboardModifiersAfterDesktopAction(DesktopNavigationResult desktopNavigationResult, KeyboardModifierKeys keyboardModifierKeys)
    {
        if (!desktopNavigationResult.IsSuccessful
            || desktopNavigationResult.NavigationActionKind is not (DesktopNavigationActionKind.Switched or DesktopNavigationActionKind.CreatedAndSwitched)
            || keyboardModifierKeys == KeyboardModifierKeys.None)
            return;

        try
        {
            MouseHelper.ConsumePressedKeyboardModifierKeys(keyboardModifierKeys);
            _fileLogService.WriteInformation(nameof(DesktopLifecycleService), $"Consumed modifier keys after desktop action. ModifierKeys={keyboardModifierKeys}, Action={desktopNavigationResult.NavigationActionKind}.");
        }
        catch (InvalidOperationException exception) { _fileLogService.WriteWarning(nameof(DesktopLifecycleService), $"Failed to consume modifier keys after desktop action. ModifierKeys={keyboardModifierKeys}, Action={desktopNavigationResult.NavigationActionKind}.", exception); }
    }

    private bool ShouldSkipDesktopCreationWhenCurrentDesktopIsEmpty(DeskBorderSettings currentSettings, VirtualDesktopWorkspaceSnapshot currentWorkspaceSnapshot)
    {
        if (!currentSettings.IsDesktopCreationSkippedWhenCurrentDesktopIsEmpty
            || string.IsNullOrWhiteSpace(currentWorkspaceSnapshot.CurrentDesktopIdentifier)
            || !_virtualDesktopService.IsDesktopEmpty(currentWorkspaceSnapshot.CurrentDesktopIdentifier))
        {
            return false;
        }

        _fileLogService.WriteInformation(nameof(DesktopLifecycleService), $"Skipped desktop creation because the current desktop '{currentWorkspaceSnapshot.CurrentDesktopIdentifier}' is empty.");
        return true;
    }

    private DesktopEdgeActivationEvaluation EvaluateDesktopEdgeActivation(DesktopEdgeMonitoringState currentState, DeskBorderSettings currentSettings)
    {
        if (!currentState.IsDesktopEdgeAvailable || currentState.ActiveDesktopEdge == DesktopEdgeKind.None)
            return new(false, false, CreateNoOperationNavigationResult(_virtualDesktopService.GetWorkspaceSnapshot()), DesktopSwitchDirection.Previous);

        var desktopSwitchDirection = ConvertToDesktopSwitchDirection(currentState.ActiveDesktopEdge, currentSettings.IsVerticalDesktopSwitchDirectionReversed);
        var currentWorkspaceSnapshot = _virtualDesktopService.GetWorkspaceSnapshot();
        var canCreateDesktop = currentState.IsCreateDesktopModifierSatisfied
            && currentSettings.IsDesktopCreationEnabled
            && IsCurrentDesktopOuterForDesktopSwitchDirection(currentWorkspaceSnapshot, desktopSwitchDirection)
            && !ShouldSkipDesktopCreationWhenCurrentDesktopIsEmpty(currentSettings, currentWorkspaceSnapshot);
        var shouldAttemptActivation = currentState.IsSwitchDesktopModifierSatisfied || canCreateDesktop;
        return new(shouldAttemptActivation, canCreateDesktop, CreateNoOperationNavigationResult(currentWorkspaceSnapshot), desktopSwitchDirection);
    }

    private bool ShouldCreateDesktopAfterFailedSwitch(
        DeskBorderSettings currentSettings,
        DesktopNavigationResult switchDesktopNavigationResult,
        DesktopSwitchDirection desktopSwitchDirection,
        bool isDesktopCreationRequested)
    {
        if (!isDesktopCreationRequested
            || !currentSettings.IsDesktopCreationEnabled
            || switchDesktopNavigationResult.OperationStatus != VirtualDesktopOperationStatus.NoAdjacentDesktop)
        {
            return false;
        }

        var currentWorkspaceSnapshot = switchDesktopNavigationResult.PreviousWorkspaceSnapshot;
        return IsCurrentDesktopOuterForDesktopSwitchDirection(currentWorkspaceSnapshot, desktopSwitchDirection)
            && !ShouldSkipDesktopCreationWhenCurrentDesktopIsEmpty(currentSettings, currentWorkspaceSnapshot);
    }

    private DesktopNavigationResult SwitchDesktopWithOptionalCreation(
        DeskBorderSettings currentSettings,
        DesktopSwitchDirection desktopSwitchDirection,
        bool isDesktopCreationRequested)
    {
        var switchDesktopNavigationResult = _virtualDesktopService.SwitchDesktop(desktopSwitchDirection);
        if (!ShouldCreateDesktopAfterFailedSwitch(currentSettings, switchDesktopNavigationResult, desktopSwitchDirection, isDesktopCreationRequested))
            return switchDesktopNavigationResult;

        return _virtualDesktopService.CreateDesktopAndSwitch(desktopSwitchDirection);
    }

    private DesktopNavigationResult HandleEdgeActivation(DesktopEdgeMonitoringState currentState, DeskBorderSettings currentSettings, DesktopEdgeActivationEvaluation desktopEdgeActivationEvaluation)
    {
        if (!desktopEdgeActivationEvaluation.ShouldAttemptActivation)
            return desktopEdgeActivationEvaluation.NoOperationNavigationResult;

        if (currentState.IsSwitchDesktopModifierSatisfied)
        {
            var switchOrCreateDesktopNavigationResult = SwitchDesktopWithOptionalCreation(currentSettings, desktopEdgeActivationEvaluation.DesktopSwitchDirection, currentState.IsCreateDesktopModifierSatisfied);
            ConsumeKeyboardModifiersAfterDesktopAction(switchOrCreateDesktopNavigationResult, currentSettings.SwitchDesktopModifierSettings.RequiredKeyboardModifierKeys);
            return switchOrCreateDesktopNavigationResult;
        }

        if (desktopEdgeActivationEvaluation.CanCreateDesktop)
        {
            var createDesktopAndSwitchResult = _virtualDesktopService.CreateDesktopAndSwitch(desktopEdgeActivationEvaluation.DesktopSwitchDirection);
            ConsumeKeyboardModifiersAfterDesktopAction(createDesktopAndSwitchResult, currentSettings.CreateDesktopModifierSettings.RequiredKeyboardModifierKeys);
            return createDesktopAndSwitchResult;
        }

        return desktopEdgeActivationEvaluation.NoOperationNavigationResult;
    }

    private async Task HandleNavigationResultAsync(DesktopNavigationResult desktopNavigationResult, CancellationToken cancellationToken = default)
    {
        if (!desktopNavigationResult.IsSuccessful)
        {
            _fileLogService.WriteWarning(nameof(DesktopLifecycleService), $"Desktop navigation did not succeed. Status={desktopNavigationResult.OperationStatus}.");
            return;
        }

        await UiThreadHelper.ExecuteAsync(_navigatorService.RefreshPreview);
        if (desktopNavigationResult.NavigationActionKind == DesktopNavigationActionKind.CreatedAndSwitched)
        {
            _fileLogService.WriteInformation(nameof(DesktopLifecycleService), "Created a new desktop and switched to it.");
            return;
        }

        if (!IsInwardSwitch(desktopNavigationResult)
            || string.IsNullOrWhiteSpace(desktopNavigationResult.SourceDesktopIdentifier)
            || string.IsNullOrWhiteSpace(desktopNavigationResult.TargetDesktopIdentifier))
        {
            return;
        }

        var autoDeletionValidationResult = _virtualDesktopService.EvaluateAutoDeletion(desktopNavigationResult.SourceDesktopIdentifier, desktopNavigationResult.TargetDesktopIdentifier);
        if (!autoDeletionValidationResult.CanAutoDelete)
            return;

        var sourceDesktopEntry = desktopNavigationResult.PreviousWorkspaceSnapshot.DesktopEntries.FirstOrDefault(desktopEntry => string.Equals(desktopEntry.DesktopIdentifier, desktopNavigationResult.SourceDesktopIdentifier, StringComparison.Ordinal));
        if (sourceDesktopEntry is null)
            return;

        var pendingDesktopDeletion = new PendingDesktopDeletion
        {
            DesktopIdentifier = sourceDesktopEntry.DesktopIdentifier,
            DesktopDisplayName = sourceDesktopEntry.DisplayName,
            FallbackDesktopIdentifier = desktopNavigationResult.TargetDesktopIdentifier,
            UndoDuration = TimeSpan.FromSeconds(_settingsService.Settings.AutoDeleteWarningTimeoutSeconds)
        };

        if (!_settingsService.Settings.IsAutoDeleteWarningEnabled)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            _fileLogService.WriteInformation(nameof(DesktopLifecycleService), $"Deleting desktop immediately without warning. DesktopIdentifier={pendingDesktopDeletion.DesktopIdentifier}.");
            await DeleteDesktopAsync(pendingDesktopDeletion, _settingsService.Settings.IsAutoDeleteCompletionToastEnabled);
            return;
        }

        _fileLogService.WriteInformation(nameof(DesktopLifecycleService), $"Scheduling pending desktop deletion. DesktopIdentifier={pendingDesktopDeletion.DesktopIdentifier}.");
        await SchedulePendingDesktopDeletionAsync(pendingDesktopDeletion, cancellationToken);
    }

    private async Task RefreshNavigatorAsync()
    {
        var currentMonitoringState = _desktopEdgeMonitorService.CurrentState;
        await UiThreadHelper.ExecuteAsync(() =>
        {
            var navigatorSettings = _settingsService.Settings.NavigatorSettings;
            _navigatorService.RefreshPreview();
            _navigatorService.UpdateTriggerAreaState(navigatorSettings.IsTriggerAreaEnabled, navigatorSettings.TriggerRectangle);
            _navigatorService.UpdateTriggerAreaPointerState(currentMonitoringState.NavigatorTriggerState.IsCursorInsideTriggerRectangle);
        });
    }

    private async Task RunPendingDesktopDeletionAsync(PendingDesktopDeletion pendingDesktopDeletion, CancellationToken cancellationToken)
    {
        var toastPresentationResult = await _toastService.ShowToastAsync(new WarningToastPresentationOptions
        {
            Title = LocalizedResourceAccessor.GetString("Toast.AutoDelete.Title"),
            Message = LocalizedResourceAccessor.GetFormattedString("Toast.AutoDelete.MessageFormat", pendingDesktopDeletion.DesktopDisplayName),
            ActionCardTitle = LocalizedResourceAccessor.GetString("Toast.AutoDelete.ActionCardTitle"),
            ActionButtonText = LocalizedResourceAccessor.GetString("Toast.AutoDelete.Action"),
            Duration = pendingDesktopDeletion.UndoDuration,
            WindowWidth = 420,
            WindowHeight = 170
        }, cancellationToken);
        if (toastPresentationResult.ResultKind != ToastPresentationResultKind.TimedOut || cancellationToken.IsCancellationRequested)
            return;

        await _operationSemaphore.WaitAsync(cancellationToken);
        try { await DeleteDesktopAsync(pendingDesktopDeletion); }
        finally { _operationSemaphore.Release(); }
    }

    private async Task DeleteDesktopAsync(PendingDesktopDeletion pendingDesktopDeletion, bool shouldShowCompletionToast = false)
    {
        var desktopDeletionResult = _virtualDesktopService.DeleteDesktop(pendingDesktopDeletion.DesktopIdentifier, pendingDesktopDeletion.FallbackDesktopIdentifier);
        if (desktopDeletionResult.IsSuccessful)
        {
            _fileLogService.WriteInformation(nameof(DesktopLifecycleService), $"Deleted desktop. DesktopIdentifier={pendingDesktopDeletion.DesktopIdentifier}, FallbackDesktopIdentifier={pendingDesktopDeletion.FallbackDesktopIdentifier}.");
            await UiThreadHelper.ExecuteAsync(_navigatorService.RefreshPreview);
            if (shouldShowCompletionToast)
                _ = ShowAutoDeleteCompletionToastAsync(pendingDesktopDeletion);

            return;
        }

        _fileLogService.WriteWarning(nameof(DesktopLifecycleService), $"Desktop deletion failed. Status={desktopDeletionResult.OperationStatus}, DesktopIdentifier={pendingDesktopDeletion.DesktopIdentifier}.");
    }

    private async Task ShowAutoDeleteCompletionToastAsync(PendingDesktopDeletion pendingDesktopDeletion)
    {
        _fileLogService.WriteInformation(nameof(DesktopLifecycleService), $"Showing auto-delete completion toast. DesktopIdentifier={pendingDesktopDeletion.DesktopIdentifier}.");
        await _toastService.ShowToastAsync(new HotkeyToastPresentationOptions
        {
            Title = LocalizedResourceAccessor.GetString("Toast.AutoDelete.Completed.Title"),
            Message = LocalizedResourceAccessor.GetFormattedString("Toast.AutoDelete.Completed.MessageFormat", pendingDesktopDeletion.DesktopDisplayName),
            Duration = TimeSpan.FromSeconds(1),
            WindowWidth = 360,
            WindowHeight = 100
        });
    }

    private async Task SchedulePendingDesktopDeletionAsync(PendingDesktopDeletion pendingDesktopDeletion, CancellationToken cancellationToken)
    {
        await CancelPendingDesktopDeletionAsync();
        _pendingDesktopDeletionCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pendingDesktopDeletionTask = RunPendingDesktopDeletionAsync(pendingDesktopDeletion, _pendingDesktopDeletionCancellationTokenSource.Token);
    }

    private void OnLocalizationServiceLanguageChanged(object? sender, EventArgs eventArguments) => _ = RefreshNavigatorAsync();

    private void OnDesktopEdgeMonitorServiceMonitoringStateChanged(object? sender, DesktopEdgeMonitoringStateChangedEventArgs desktopEdgeMonitoringStateChangedEventArgs) => _ = HandleDesktopEdgeMonitorServiceMonitoringStateChangedAsync(desktopEdgeMonitoringStateChangedEventArgs);

    private async Task HandleDesktopEdgeMonitorServiceMonitoringStateChangedAsync(DesktopEdgeMonitoringStateChangedEventArgs desktopEdgeMonitoringStateChangedEventArgs)
    {
        if (!IsRunning)
            return;

        var previousState = desktopEdgeMonitoringStateChangedEventArgs.PreviousState;
        var currentState = desktopEdgeMonitoringStateChangedEventArgs.CurrentState;
        if (_navigatorService.IsVisible
            && HaveDisplayMonitorsChanged(previousState.DisplayMonitors, currentState.DisplayMonitors))
        {
            await UiThreadHelper.ExecuteAsync(_navigatorService.RefreshPreview);
        }

        await UiThreadHelper.ExecuteAsync(() => _navigatorService.UpdateTriggerAreaPointerState(currentState.NavigatorTriggerState.IsCursorInsideTriggerRectangle));
        if (!_isDesktopEdgeActivationArmed && HasPointerMovedAwayFromDesktopEdge(currentState, _lastTriggeredDesktopEdge))
        {
            _isDesktopEdgeActivationArmed = true;
            _lastTriggeredDesktopEdge = DesktopEdgeKind.None;
        }

        if (!ShouldHandleDesktopEdgeActivation(previousState, currentState, _isDesktopEdgeActivationArmed))
            return;

        await _operationSemaphore.WaitAsync();
        try
        {
            await CancelPendingDesktopDeletionAsync();
            var currentSettings = _settingsService.Settings;
            var desktopEdgeActivationEvaluation = EvaluateDesktopEdgeActivation(currentState, currentSettings);
            if (!desktopEdgeActivationEvaluation.ShouldAttemptActivation)
                return;

            _isDesktopEdgeActivationArmed = false;
            _lastTriggeredDesktopEdge = currentState.ActiveDesktopEdge;
            var desktopNavigationResult = HandleEdgeActivation(currentState, currentSettings, desktopEdgeActivationEvaluation);
            if (desktopNavigationResult.NavigationActionKind != DesktopNavigationActionKind.None)
            {
                _fileLogService.WriteInformation(nameof(DesktopLifecycleService), $"Handled desktop edge activation. Action={desktopNavigationResult.NavigationActionKind}, Status={desktopNavigationResult.OperationStatus}.");
                await TryApplyMouseLocationAfterDesktopSwitchAsync(
                    desktopNavigationResult,
                    currentSettings.DesktopSwitchMouseLocationSettings.DesktopEdgeTriggeredMouseLocationOption,
                    CreateDesktopSwitchMouseLocationContext(currentState, desktopEdgeActivationEvaluation.DesktopSwitchDirection),
                    "DesktopEdge");
            }
            await HandleNavigationResultAsync(desktopNavigationResult);
        }
        finally { _operationSemaphore.Release(); }
    }

    private async void OnHotkeyServiceHotkeyInvoked(object? sender, HotkeyInvokedEventArgs hotkeyInvokedEventArgs)
    {
        try { await HandleHotkeyServiceHotkeyInvokedAsync(hotkeyInvokedEventArgs); }
        catch (Exception exception) { _fileLogService.WriteError(nameof(DesktopLifecycleService), $"Handling hotkey action failed. Action={hotkeyInvokedEventArgs.HotkeyActionType}.", exception); }
    }

    private async Task HandleHotkeyServiceHotkeyInvokedAsync(HotkeyInvokedEventArgs hotkeyInvokedEventArgs)
    {
        if (!IsRunning)
            return;

        _fileLogService.WriteInformation(nameof(DesktopLifecycleService), $"Handling hotkey action. Action={hotkeyInvokedEventArgs.HotkeyActionType}.");
        var currentSettings = _settingsService.Settings;
        await _operationSemaphore.WaitAsync();
        try
        {
            switch (hotkeyInvokedEventArgs.HotkeyActionType)
            {
                case HotkeyActionType.SwitchToPreviousDesktop:
                    await CancelPendingDesktopDeletionAsync();
                    var switchToPreviousDesktopMouseLocationContext = currentSettings.DesktopSwitchMouseLocationSettings.HotkeyTriggeredMouseLocationOption == DesktopSwitchMouseLocationOption.DoNotMove
                        ? null
                        : TryCreateHotkeyDesktopSwitchMouseLocationContext(DesktopSwitchDirection.Previous);
                    var switchToPreviousDesktopNavigationResult = SwitchDesktopWithOptionalCreation(currentSettings, DesktopSwitchDirection.Previous, true);
                    await TryApplyMouseLocationAfterDesktopSwitchAsync(
                        switchToPreviousDesktopNavigationResult,
                        currentSettings.DesktopSwitchMouseLocationSettings.HotkeyTriggeredMouseLocationOption,
                        switchToPreviousDesktopMouseLocationContext,
                        "Hotkey");
                    await HandleNavigationResultAsync(switchToPreviousDesktopNavigationResult);
                    return;

                case HotkeyActionType.SwitchToNextDesktop:
                    await CancelPendingDesktopDeletionAsync();
                    var switchToNextDesktopMouseLocationContext = currentSettings.DesktopSwitchMouseLocationSettings.HotkeyTriggeredMouseLocationOption == DesktopSwitchMouseLocationOption.DoNotMove
                        ? null
                        : TryCreateHotkeyDesktopSwitchMouseLocationContext(DesktopSwitchDirection.Next);
                    var switchToNextDesktopNavigationResult = SwitchDesktopWithOptionalCreation(currentSettings, DesktopSwitchDirection.Next, true);
                    await TryApplyMouseLocationAfterDesktopSwitchAsync(
                        switchToNextDesktopNavigationResult,
                        currentSettings.DesktopSwitchMouseLocationSettings.HotkeyTriggeredMouseLocationOption,
                        switchToNextDesktopMouseLocationContext,
                        "Hotkey");
                    await HandleNavigationResultAsync(switchToNextDesktopNavigationResult);
                    return;

                case HotkeyActionType.MoveFocusedWindowToPreviousDesktop:
                    await CancelPendingDesktopDeletionAsync();
                    var moveFocusedWindowToPreviousDesktopMouseLocationContext = currentSettings.DesktopSwitchMouseLocationSettings.HotkeyTriggeredMouseLocationOption == DesktopSwitchMouseLocationOption.DoNotMove
                        ? null
                        : TryCreateHotkeyDesktopSwitchMouseLocationContext(DesktopSwitchDirection.Previous);
                    var moveFocusedWindowToPreviousDesktopNavigationResult = _virtualDesktopService.MoveFocusedWindowToAdjacentDesktop(DesktopSwitchDirection.Previous);
                    await TryApplyMouseLocationAfterDesktopSwitchAsync(
                        moveFocusedWindowToPreviousDesktopNavigationResult,
                        currentSettings.DesktopSwitchMouseLocationSettings.HotkeyTriggeredMouseLocationOption,
                        moveFocusedWindowToPreviousDesktopMouseLocationContext,
                        "Hotkey");
                    await HandleNavigationResultAsync(moveFocusedWindowToPreviousDesktopNavigationResult);
                    return;

                case HotkeyActionType.MoveFocusedWindowToNextDesktop:
                    await CancelPendingDesktopDeletionAsync();
                    var moveFocusedWindowToNextDesktopMouseLocationContext = currentSettings.DesktopSwitchMouseLocationSettings.HotkeyTriggeredMouseLocationOption == DesktopSwitchMouseLocationOption.DoNotMove
                        ? null
                        : TryCreateHotkeyDesktopSwitchMouseLocationContext(DesktopSwitchDirection.Next);
                    var moveFocusedWindowToNextDesktopNavigationResult = _virtualDesktopService.MoveFocusedWindowToAdjacentDesktop(DesktopSwitchDirection.Next);
                    await TryApplyMouseLocationAfterDesktopSwitchAsync(
                        moveFocusedWindowToNextDesktopNavigationResult,
                        currentSettings.DesktopSwitchMouseLocationSettings.HotkeyTriggeredMouseLocationOption,
                        moveFocusedWindowToNextDesktopMouseLocationContext,
                        "Hotkey");
                    await HandleNavigationResultAsync(moveFocusedWindowToNextDesktopNavigationResult);
                    return;

                case HotkeyActionType.ToggleNavigator:
                    await UiThreadHelper.ExecuteAsync(_navigatorService.ToggleOverlay);
                    return;

                default:
                    return;
            }
        }
        finally { _operationSemaphore.Release(); }
    }

    private void OnSettingsServiceSettingsChanged(object? sender, EventArgs eventArguments) => _ = HandleSettingsServiceSettingsChangedAsync();

    private async Task HandleSettingsServiceSettingsChangedAsync()
    {
        if (!IsRunning)
            return;

        await _operationSemaphore.WaitAsync();
        try
        {
            var currentSettings = _settingsService.Settings;
            if (!currentSettings.IsAutoDeleteEnabled || !currentSettings.IsAutoDeleteWarningEnabled)
                await CancelPendingDesktopDeletionAsync();

            await RefreshNavigatorAsync();
        }
        finally { _operationSemaphore.Release(); }
    }

    private readonly record struct DesktopSwitchMouseLocationContext(
        DisplayMonitorInfo[] DisplayMonitors,
        DisplayMonitorInfo? InputDisplayMonitor,
        ScreenPoint InputCursorPosition,
        DesktopSwitchDirection DesktopSwitchDirection,
        DesktopEdgeKind TriggeredDesktopEdge);

    private readonly record struct DesktopSwitchMouseLocationApplyResult(
        int AttemptNumber,
        bool DidSetCursorPosition,
        bool DidReadActualMouseLocation,
        bool WasTargetMouseLocationApplied,
        ScreenPoint? ActualMouseLocation,
        int SetCursorPositionLastWindowsErrorCode,
        int GetCursorPositionLastWindowsErrorCode)
    {
        public bool IsSuccessful => DidSetCursorPosition && DidReadActualMouseLocation && WasTargetMouseLocationApplied;
    }

    private readonly record struct DesktopEdgeActivationEvaluation(
        bool ShouldAttemptActivation,
        bool CanCreateDesktop,
        DesktopNavigationResult NoOperationNavigationResult,
        DesktopSwitchDirection DesktopSwitchDirection);
}
