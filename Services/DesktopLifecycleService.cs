using DeskBorder.Helpers;
using DeskBorder.Models;

namespace DeskBorder.Services;

public sealed class DesktopLifecycleService(
    IDesktopEdgeMonitorService desktopEdgeMonitorService,
    IHotkeyService hotkeyService,
    ILocalizationService localizationService,
    INavigatorService navigatorService,
    ISettingsService settingsService,
    IToastService toastService,
    IVirtualDesktopService virtualDesktopService) : IDesktopLifecycleService
{
    private const int DesktopEdgeTriggerRearmDistanceInPixels = 24;

    private readonly IDesktopEdgeMonitorService _desktopEdgeMonitorService = desktopEdgeMonitorService;
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
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        _isDesktopEdgeActivationArmed = true;
        _desktopEdgeMonitorService.MonitoringStateChanged -= OnDesktopEdgeMonitorServiceMonitoringStateChanged;
        _hotkeyService.HotkeyInvoked -= OnHotkeyServiceHotkeyInvoked;
        _localizationService.LanguageChanged -= OnLocalizationServiceLanguageChanged;
        _settingsService.SettingsChanged -= OnSettingsServiceSettingsChanged;

        await CancelPendingDesktopDeletionAsync();
        await _desktopEdgeMonitorService.StopAsync();
        await UiThreadHelper.ExecuteAsync(_navigatorService.Hide);
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

    private static bool IsCurrentDesktopRightOuter(VirtualDesktopWorkspaceSnapshot workspaceSnapshot) => workspaceSnapshot.DesktopEntries.Length > 0
        && workspaceSnapshot.CurrentDesktopNumber == workspaceSnapshot.DesktopEntries.Length;

    private async Task CancelPendingDesktopDeletionAsync()
    {
        _pendingDesktopDeletionCancellationTokenSource?.Cancel();
        if (_pendingDesktopDeletionTask is not null)
        {
            try { await _pendingDesktopDeletionTask; }
            catch (OperationCanceledException) { }
        }

        _pendingDesktopDeletionCancellationTokenSource?.Dispose();
        _pendingDesktopDeletionCancellationTokenSource = null;
        _pendingDesktopDeletionTask = null;
        await _toastService.DismissAsync();
    }

    private DesktopNavigationResult HandleEdgeActivation(DesktopEdgeMonitoringState currentState)
    {
        if (!currentState.IsDesktopEdgeAvailable || currentState.ActiveDesktopEdge == DesktopEdgeKind.None)
            return CreateNoOperationNavigationResult(_virtualDesktopService.GetWorkspaceSnapshot());

        var currentSettings = _settingsService.Settings;
        var desktopSwitchDirection = ConvertToDesktopSwitchDirection(currentState.ActiveDesktopEdge);
        var currentWorkspaceSnapshot = _virtualDesktopService.GetWorkspaceSnapshot();
        var canCreateDesktop = desktopSwitchDirection == DesktopSwitchDirection.Next
            && currentSettings.IsDesktopCreationEnabled
            && currentState.IsCreateDesktopModifierSatisfied
            && IsCurrentDesktopRightOuter(currentWorkspaceSnapshot);

        if (currentState.IsSwitchDesktopModifierSatisfied)
        {
            var switchResult = _virtualDesktopService.SwitchDesktop(desktopSwitchDirection);
            if (switchResult.OperationStatus != VirtualDesktopOperationStatus.NoAdjacentDesktop || !canCreateDesktop)
                return switchResult;

            return _virtualDesktopService.CreateDesktopAndSwitch(desktopSwitchDirection);
        }

        if (canCreateDesktop)
            return _virtualDesktopService.CreateDesktopAndSwitch(desktopSwitchDirection);

        return CreateNoOperationNavigationResult(currentWorkspaceSnapshot);
    }

    private async Task HandleNavigationResultAsync(DesktopNavigationResult desktopNavigationResult, CancellationToken cancellationToken = default)
    {
        if (!desktopNavigationResult.IsSuccessful)
            return;

        await UiThreadHelper.ExecuteAsync(_navigatorService.RefreshPreview);
        if (desktopNavigationResult.NavigationActionKind == DesktopNavigationActionKind.CreatedAndSwitched)
            return;

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

            await DeleteDesktopAsync(pendingDesktopDeletion);
            return;
        }

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

    private async Task DeleteDesktopAsync(PendingDesktopDeletion pendingDesktopDeletion)
    {
        var desktopDeletionResult = _virtualDesktopService.DeleteDesktop(pendingDesktopDeletion.DesktopIdentifier, pendingDesktopDeletion.FallbackDesktopIdentifier);
        if (desktopDeletionResult.IsSuccessful)
            await UiThreadHelper.ExecuteAsync(_navigatorService.RefreshPreview);
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
            await HandleNavigationResultAsync(desktopNavigationResult);
        }
        finally { _operationSemaphore.Release(); }
    }

    private void OnHotkeyServiceHotkeyInvoked(object? sender, HotkeyInvokedEventArgs hotkeyInvokedEventArgs) => _ = HandleHotkeyServiceHotkeyInvokedAsync(hotkeyInvokedEventArgs);

    private async Task HandleHotkeyServiceHotkeyInvokedAsync(HotkeyInvokedEventArgs hotkeyInvokedEventArgs)
    {
        if (!IsRunning)
            return;

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
