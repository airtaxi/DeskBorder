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

    private static ScreenRectangle CreateCombinedMonitorBounds(DisplayMonitorInfo[] displayMonitors)
    {
        var left = displayMonitors.Min(displayMonitor => displayMonitor.MonitorBounds.Left);
        var top = displayMonitors.Min(displayMonitor => displayMonitor.MonitorBounds.Top);
        var right = displayMonitors.Max(displayMonitor => displayMonitor.MonitorBounds.Right);
        var bottom = displayMonitors.Max(displayMonitor => displayMonitor.MonitorBounds.Bottom);
        return new(left, top, right, bottom);
    }

    private static DesktopSwitchMouseLocationContext CreateDesktopSwitchMouseLocationContext(DesktopEdgeMonitoringState currentState) => new(
        currentState.DisplayMonitors,
        currentState.CurrentDisplayMonitor,
        currentState.CursorPosition,
        ConvertToDesktopSwitchDirection(currentState.ActiveDesktopEdge));

    private static ScreenPoint CreateScreenRectangleCenterPoint(ScreenRectangle screenRectangle) => new(
        screenRectangle.Left + screenRectangle.Width / 2,
        screenRectangle.Top + screenRectangle.Height / 2);

    private static DisplayMonitorInfo? FindDisplayMonitor(DisplayMonitorInfo[] displayMonitors, ScreenPoint screenPoint) => displayMonitors.FirstOrDefault(displayMonitor => displayMonitor.MonitorBounds.Contains(screenPoint));

    private static DisplayMonitorInfo? FindPrimaryDisplayMonitor(DisplayMonitorInfo[] displayMonitors) => displayMonitors.FirstOrDefault(displayMonitor => displayMonitor.IsPrimaryDisplay);

    private static ScreenPoint? TryCreateOppositeSideMouseLocation(DesktopSwitchMouseLocationContext desktopSwitchMouseLocationContext)
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

    private static int MapCursorVerticalPosition(DisplayMonitorInfo[] displayMonitors, DisplayMonitorInfo? sourceMonitor, int cursorY, int targetX)
    {
        if (sourceMonitor is null) return cursorY;

        var targetMonitor = displayMonitors.FirstOrDefault(monitor => targetX >= monitor.MonitorBounds.Left && targetX < monitor.MonitorBounds.Right);
        if (targetMonitor is null || targetMonitor == sourceMonitor) return cursorY;

        var relativeVerticalPosition = (double)(cursorY - sourceMonitor.MonitorBounds.Top) / sourceMonitor.MonitorBounds.Height;
        var mappedY = targetMonitor.MonitorBounds.Top + (int)Math.Round(relativeVerticalPosition * targetMonitor.MonitorBounds.Height, MidpointRounding.AwayFromZero);
        return Math.Clamp(mappedY, targetMonitor.MonitorBounds.Top, targetMonitor.MonitorBounds.Bottom - 1);
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
                desktopSwitchDirection);
        }
        catch (InvalidOperationException exception)
        {
            _fileLogService.WriteWarning(nameof(DesktopLifecycleService), $"Failed to capture cursor context for hotkey desktop switch. Direction={desktopSwitchDirection}.", exception);
            return null;
        }
    }

    private void TryApplyMouseLocationAfterDesktopSwitch(
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

        if (!MouseHelper.TrySetCursorPosition(targetMouseLocation.Value))
        {
            _fileLogService.WriteWarning(nameof(DesktopLifecycleService), $"Failed to apply mouse location after desktop switch. TriggerSource={triggerSource}, Option={desktopSwitchMouseLocationOption}, X={targetMouseLocation.Value.X}, Y={targetMouseLocation.Value.Y}.");
            return;
        }

        _fileLogService.WriteInformation(nameof(DesktopLifecycleService), $"Applied mouse location after desktop switch. TriggerSource={triggerSource}, Option={desktopSwitchMouseLocationOption}, X={targetMouseLocation.Value.X}, Y={targetMouseLocation.Value.Y}.");
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
                || desktopSwitchDirection == DesktopSwitchDirection.Previous && IsCurrentDesktopLeftOuter(currentWorkspaceSnapshot))
            && !ShouldSkipDesktopCreationWhenCurrentDesktopIsEmpty(currentSettings, currentWorkspaceSnapshot);

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
            var currentSettings = _settingsService.Settings;
            var desktopNavigationResult = HandleEdgeActivation(currentState);
            if (desktopNavigationResult.NavigationActionKind != DesktopNavigationActionKind.None)
            {
                _fileLogService.WriteInformation(nameof(DesktopLifecycleService), $"Handled desktop edge activation. Action={desktopNavigationResult.NavigationActionKind}, Status={desktopNavigationResult.OperationStatus}.");
                TryApplyMouseLocationAfterDesktopSwitch(
                    desktopNavigationResult,
                    currentSettings.DesktopSwitchMouseLocationSettings.DesktopEdgeTriggeredMouseLocationOption,
                    CreateDesktopSwitchMouseLocationContext(currentState),
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
                    var switchToPreviousDesktopNavigationResult = _virtualDesktopService.SwitchDesktop(DesktopSwitchDirection.Previous);
                    TryApplyMouseLocationAfterDesktopSwitch(
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
                    var switchToNextDesktopNavigationResult = _virtualDesktopService.SwitchDesktop(DesktopSwitchDirection.Next);
                    TryApplyMouseLocationAfterDesktopSwitch(
                        switchToNextDesktopNavigationResult,
                        currentSettings.DesktopSwitchMouseLocationSettings.HotkeyTriggeredMouseLocationOption,
                        switchToNextDesktopMouseLocationContext,
                        "Hotkey");
                    await HandleNavigationResultAsync(switchToNextDesktopNavigationResult);
                    return;

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

    private readonly record struct DesktopSwitchMouseLocationContext(
        DisplayMonitorInfo[] DisplayMonitors,
        DisplayMonitorInfo? InputDisplayMonitor,
        ScreenPoint InputCursorPosition,
        DesktopSwitchDirection DesktopSwitchDirection);
}
