using DeskBorder.Helpers;
using DeskBorder.Models;

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

    public bool IsRunning { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            return;

        _fileLogService.WriteInformation(nameof(DesktopLifecycleService), "Starting desktop lifecycle service.");
        _isDesktopEdgeActivationArmed = true;
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
        _desktopEdgeMonitorService.MonitoringStateChanged -= OnDesktopEdgeMonitorServiceMonitoringStateChanged;
        _hotkeyService.HotkeyInvoked -= OnHotkeyServiceHotkeyInvoked;
        _localizationService.LanguageChanged -= OnLocalizationServiceLanguageChanged;
        _settingsService.SettingsChanged -= OnSettingsServiceSettingsChanged;

        await CancelPendingDesktopDeletionAsync();
        await _desktopEdgeMonitorService.StopAsync();
        await UiThreadHelper.ExecuteAsync(_navigatorService.Hide);
        _fileLogService.WriteInformation(nameof(DesktopLifecycleService), "Desktop lifecycle service stopped.");
    }

    private static DesktopSwitchDirection ConvertToDesktopSwitchDirection(DesktopEdgeKind desktopEdgeKind) => desktopEdgeKind switch
    {
        DesktopEdgeKind.LeftOuterDisplayEdge => DesktopSwitchDirection.Previous,
        DesktopEdgeKind.RightOuterDisplayEdge => DesktopSwitchDirection.Next,
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

    private static bool HasPointerMovedAwayFromDesktopEdge(DesktopEdgeMonitoringState currentState)
    {
        if (currentState.CurrentDisplayMonitor is null || currentState.DisplayMonitors.Length == 0)
            return false;

        var leftmostDisplayEdge = currentState.DisplayMonitors.Min(displayMonitor => displayMonitor.MonitorBounds.Left);
        var rightmostDisplayEdge = currentState.DisplayMonitors.Max(displayMonitor => displayMonitor.MonitorBounds.Right) - 1;
        return currentState.CursorPosition.X >= leftmostDisplayEdge + DesktopEdgeTriggerRearmDistanceInPixels
            && currentState.CursorPosition.X <= rightmostDisplayEdge - DesktopEdgeTriggerRearmDistanceInPixels;
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

    private static void MoveMouseToOppositeEdgeRearmBoundary(DesktopEdgeMonitoringState currentState)
    {
        if (currentState.DisplayMonitors.Length == 0 || currentState.ActiveDesktopEdge == DesktopEdgeKind.None)
            return;

        var leftmostDisplayEdge = currentState.DisplayMonitors.Min(displayMonitor => displayMonitor.MonitorBounds.Left);
        var rightmostDisplayEdge = currentState.DisplayMonitors.Max(displayMonitor => displayMonitor.MonitorBounds.Right);
        var newX = currentState.ActiveDesktopEdge == DesktopEdgeKind.RightOuterDisplayEdge
            ? leftmostDisplayEdge + DesktopEdgeTriggerRearmDistanceInPixels
            : rightmostDisplayEdge - DesktopEdgeTriggerRearmDistanceInPixels;
        var newY = MapCursorVerticalPosition(currentState.DisplayMonitors, currentState.CurrentDisplayMonitor, currentState.CursorPosition.Y, newX);
        MouseHelper.TrySetCursorPosition(new(newX, newY));
    }

    private static int MapCursorVerticalPosition(DisplayMonitorInfo[] displayMonitors, DisplayMonitorInfo? sourceMonitor, int cursorY, int targetX)
    {
        if (sourceMonitor is null) return cursorY;

        var targetMonitor = displayMonitors.FirstOrDefault(monitor => targetX >= monitor.MonitorBounds.Left && targetX < monitor.MonitorBounds.Right);
        if (targetMonitor is null || targetMonitor == sourceMonitor) return cursorY;

        var relativeVerticalPosition = (double)(cursorY - sourceMonitor.MonitorBounds.Top) / sourceMonitor.MonitorBounds.Height;
        var mappedY = targetMonitor.MonitorBounds.Top + (int)Math.Round(relativeVerticalPosition * targetMonitor.MonitorBounds.Height, MidpointRounding.AwayFromZero);
        return Math.Clamp(mappedY, targetMonitor.MonitorBounds.Top, targetMonitor.MonitorBounds.Bottom - 1);
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

    private DesktopNavigationResult HandleEdgeActivation(DesktopEdgeMonitoringState currentState)
    {
        if (!currentState.IsDesktopEdgeAvailable || currentState.ActiveDesktopEdge == DesktopEdgeKind.None)
            return CreateNoOperationNavigationResult(_virtualDesktopService.GetWorkspaceSnapshot());

        var currentSettings = _settingsService.Settings;
        var desktopSwitchDirection = ConvertToDesktopSwitchDirection(currentState.ActiveDesktopEdge);
        var currentWorkspaceSnapshot = _virtualDesktopService.GetWorkspaceSnapshot();
        var canCreateDesktop = currentSettings.IsDesktopCreationEnabled
            && currentState.IsCreateDesktopModifierSatisfied
            && (desktopSwitchDirection == DesktopSwitchDirection.Next && IsCurrentDesktopRightOuter(currentWorkspaceSnapshot)
                || desktopSwitchDirection == DesktopSwitchDirection.Previous && IsCurrentDesktopLeftOuter(currentWorkspaceSnapshot));

        if (currentState.IsSwitchDesktopModifierSatisfied)
        {
            var switchResult = _virtualDesktopService.SwitchDesktop(desktopSwitchDirection);
            if (switchResult.OperationStatus != VirtualDesktopOperationStatus.NoAdjacentDesktop || !canCreateDesktop)
            {
                ConsumeKeyboardModifiersAfterDesktopAction(switchResult, currentSettings.SwitchDesktopModifierSettings.RequiredKeyboardModifierKeys);
                return switchResult;
            }

            var createDesktopAndSwitchResult = _virtualDesktopService.CreateDesktopAndSwitch(desktopSwitchDirection);
            ConsumeKeyboardModifiersAfterDesktopAction(createDesktopAndSwitchResult, currentSettings.SwitchDesktopModifierSettings.RequiredKeyboardModifierKeys);
            return createDesktopAndSwitchResult;
        }

        if (canCreateDesktop)
        {
            var createDesktopAndSwitchResult = _virtualDesktopService.CreateDesktopAndSwitch(desktopSwitchDirection);
            ConsumeKeyboardModifiersAfterDesktopAction(createDesktopAndSwitchResult, currentSettings.CreateDesktopModifierSettings.RequiredKeyboardModifierKeys);
            return createDesktopAndSwitchResult;
        }

        return CreateNoOperationNavigationResult(currentWorkspaceSnapshot);
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

        var currentState = desktopEdgeMonitoringStateChangedEventArgs.CurrentState;
        if (_navigatorService.IsVisible
            && HaveDisplayMonitorsChanged(desktopEdgeMonitoringStateChangedEventArgs.PreviousState.DisplayMonitors, currentState.DisplayMonitors))
        {
            await UiThreadHelper.ExecuteAsync(_navigatorService.RefreshPreview);
        }

        await UiThreadHelper.ExecuteAsync(() => _navigatorService.UpdateTriggerAreaPointerState(currentState.NavigatorTriggerState.IsCursorInsideTriggerRectangle));
        if (!_isDesktopEdgeActivationArmed && HasPointerMovedAwayFromDesktopEdge(currentState))
            _isDesktopEdgeActivationArmed = true;

        if (!currentState.HasCursorEnteredDesktopEdge || !_isDesktopEdgeActivationArmed)
            return;

        _isDesktopEdgeActivationArmed = false;
        await _operationSemaphore.WaitAsync();
        try
        {
            await CancelPendingDesktopDeletionAsync();
            var desktopNavigationResult = HandleEdgeActivation(currentState);
            if (desktopNavigationResult.NavigationActionKind != DesktopNavigationActionKind.None)
            {
                _fileLogService.WriteInformation(nameof(DesktopLifecycleService), $"Handled desktop edge activation. Action={desktopNavigationResult.NavigationActionKind}, Status={desktopNavigationResult.OperationStatus}.");
                MoveMouseToOppositeEdgeRearmBoundary(currentState);
            }
            await HandleNavigationResultAsync(desktopNavigationResult);
        }
        finally { _operationSemaphore.Release(); }
    }

    private void OnHotkeyServiceHotkeyInvoked(object? sender, HotkeyInvokedEventArgs hotkeyInvokedEventArgs) => _ = HandleHotkeyServiceHotkeyInvokedAsync(hotkeyInvokedEventArgs);

    private async Task HandleHotkeyServiceHotkeyInvokedAsync(HotkeyInvokedEventArgs hotkeyInvokedEventArgs)
    {
        if (!IsRunning)
            return;

        _fileLogService.WriteInformation(nameof(DesktopLifecycleService), $"Handling hotkey action. Action={hotkeyInvokedEventArgs.HotkeyActionType}.");
        await _operationSemaphore.WaitAsync();
        try
        {
            switch (hotkeyInvokedEventArgs.HotkeyActionType)
            {
                case HotkeyActionType.MoveFocusedWindowToPreviousDesktop:
                    await CancelPendingDesktopDeletionAsync();
                    await HandleNavigationResultAsync(_virtualDesktopService.MoveFocusedWindowToAdjacentDesktop(DesktopSwitchDirection.Previous));
                    return;

                case HotkeyActionType.MoveFocusedWindowToNextDesktop:
                    await CancelPendingDesktopDeletionAsync();
                    await HandleNavigationResultAsync(_virtualDesktopService.MoveFocusedWindowToAdjacentDesktop(DesktopSwitchDirection.Next));
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
}
