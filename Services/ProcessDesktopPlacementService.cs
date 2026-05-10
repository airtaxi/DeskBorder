using DeskBorder.Interop;
using DeskBorder.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeskBorder.Services;

public sealed class ProcessDesktopPlacementService(
    ISettingsService settingsService,
    IFileLogService fileLogService,
    IVirtualDesktopService virtualDesktopService) : IProcessDesktopPlacementService
{
    private const uint ShutdownWindowEventHookMessage = Win32.WindowApplicationMessage + 20;
    private static readonly TimeSpan s_eventDrivenRefreshDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan s_fallbackPollingInterval = TimeSpan.FromMilliseconds(750);
    private readonly IFileLogService _fileLogService = fileLogService;
    private readonly ISettingsService _settingsService = settingsService;
    private readonly object _eventDrivenRefreshLock = new();
    private readonly List<TemporaryProcessDesktopPlacementRule> _temporaryRules = [];
    private readonly Lock _temporaryRulesLock = new();
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private readonly IVirtualDesktopService _virtualDesktopService = virtualDesktopService;
    private readonly ManualResetEventSlim _windowEventHookReadySignal = new(false);
    private int _eventDrivenRefreshRequestVersion;
    private Task? _eventDrivenRefreshTask;
    private Task? _fallbackPollingTask;
    private nint _windowEventHookHandle;
    private Win32.WinEventProcedure? _windowEventHookCallback;
    private Thread? _windowEventHookThread;
    private uint _windowEventHookThreadIdentifier;
    private CancellationTokenSource? _monitoringCancellationTokenSource;
    private HashSet<uint> _knownProcessIdentifiers = [];
    private HashSet<nint> _knownWindowHandles = [];

    public event EventHandler? TemporaryRulesChanged;

    public bool IsRunning { get; private set; }

    public void AddTemporaryRule(ProcessDesktopPlacementRuleSettings processDesktopPlacementRule, ProcessDesktopPlacementTemporaryRuleLifetime lifetime, TimeSpan? duration = null)
    {
        var expiresAt = lifetime == ProcessDesktopPlacementTemporaryRuleLifetime.Timed
            ? DateTimeOffset.UtcNow + (duration ?? TimeSpan.FromMinutes(30))
            : (DateTimeOffset?)null;
        var temporaryRule = new TemporaryProcessDesktopPlacementRule(processDesktopPlacementRule, lifetime, expiresAt, DateTimeOffset.UtcNow);
        lock (_temporaryRulesLock)
        {
            _temporaryRules.RemoveAll(rule => string.Equals(rule.Rule.ProcessName, processDesktopPlacementRule.ProcessName, StringComparison.OrdinalIgnoreCase));
            _temporaryRules.Add(temporaryRule);
        }

        _fileLogService.WriteInformation(nameof(ProcessDesktopPlacementService), $"Added temporary process desktop placement rule. ProcessName={processDesktopPlacementRule.ProcessName}, Lifetime={lifetime}, ExpiresAt={expiresAt}.");
        OnTemporaryRulesChanged();
    }

    public async Task ApplyRuleToWindowAsync(nint windowHandle, ProcessDesktopPlacementRuleSettings processDesktopPlacementRule)
    {
        if (windowHandle == 0) return;

        var currentSettings = _settingsService.Settings;
        var result = _virtualDesktopService.PlaceWindowsOnDesktop(
            [windowHandle],
            processDesktopPlacementRule,
            currentSettings.ProcessDesktopPlacementSettings.ShouldSwitchToTargetDesktopAfterPlacement,
            ShouldCreateMissingTargetDesktopForPlacement(currentSettings, isPersistentRule: false));
        await HandlePlacementResultAsync(processDesktopPlacementRule, result, isPersistentRule: false);
    }

    public ProcessDesktopPlacementWindowSnapshot? GetForegroundWindowSnapshot() => _virtualDesktopService.GetForegroundProcessDesktopPlacementWindowSnapshot();

    public IReadOnlyList<ProcessDesktopPlacementTemporaryRuleSnapshot> GetTemporaryRules()
    {
        var currentTimestamp = DateTimeOffset.UtcNow;
        TemporaryProcessDesktopPlacementRule[] temporaryRulesSnapshot;
        var wereTemporaryRulesPruned = false;
        lock (_temporaryRulesLock)
        {
            wereTemporaryRulesPruned = PruneExpiredTemporaryRules(currentTimestamp);
            temporaryRulesSnapshot = [.. _temporaryRules];
        }

        if (wereTemporaryRulesPruned) OnTemporaryRulesChanged();

        return [.. temporaryRulesSnapshot.Select(CreateTemporaryRuleSnapshot)];
    }

    public bool RemoveTemporaryRule(string processName)
    {
        var normalizedProcessName = processName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedProcessName)) return false;

        var wasRemoved = false;
        lock (_temporaryRulesLock)
        {
            wasRemoved = _temporaryRules.RemoveAll(rule => string.Equals(rule.Rule.ProcessName, normalizedProcessName, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        if (!wasRemoved) return false;

        _fileLogService.WriteInformation(nameof(ProcessDesktopPlacementService), $"Removed temporary process desktop placement rule. ProcessName={normalizedProcessName}.");
        OnTemporaryRulesChanged();
        return true;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return Task.CompletedTask;

        _fileLogService.WriteInformation(nameof(ProcessDesktopPlacementService), "Starting process desktop placement monitoring.");
        IsRunning = true;
        var initialWindowSnapshots = _virtualDesktopService.GetProcessDesktopPlacementWindowSnapshots();
        _knownProcessIdentifiers = [.. initialWindowSnapshots.Select(windowSnapshot => windowSnapshot.ProcessIdentifier).Where(processIdentifier => processIdentifier != 0)];
        _knownWindowHandles = [.. initialWindowSnapshots.Select(windowSnapshot => windowSnapshot.WindowHandle)];
        _settingsService.SettingsChanged += OnSettingsServiceSettingsChanged;
        _monitoringCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _fallbackPollingTask = RunFallbackPollingLoopAsync(_monitoringCancellationTokenSource.Token);
        StartWindowEventHook();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        IsRunning = false;
        _settingsService.SettingsChanged -= OnSettingsServiceSettingsChanged;
        _monitoringCancellationTokenSource?.Cancel();
        TryPostWindowEventHookShutdownMessage();
        try { await (_fallbackPollingTask ?? Task.CompletedTask); }
        catch (OperationCanceledException) { }

        try { await (_eventDrivenRefreshTask ?? Task.CompletedTask); }
        catch (OperationCanceledException) { }
        finally
        {
            StopWindowEventHook();
            _monitoringCancellationTokenSource?.Dispose();
            _monitoringCancellationTokenSource = null;
            _fallbackPollingTask = null;
            _eventDrivenRefreshTask = null;
            _knownProcessIdentifiers.Clear();
            _knownWindowHandles.Clear();
            _fileLogService.WriteInformation(nameof(ProcessDesktopPlacementService), "Stopped process desktop placement monitoring.");
        }
    }

    public bool UpdateTemporaryRuleTarget(string processName, ProcessDesktopPlacementTargetSnapshot targetSnapshot)
    {
        var normalizedProcessName = processName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedProcessName)) return false;

        var wasUpdated = false;
        lock (_temporaryRulesLock)
        {
            for (var index = 0; index < _temporaryRules.Count; index++)
            {
                if (!string.Equals(_temporaryRules[index].Rule.ProcessName, normalizedProcessName, StringComparison.OrdinalIgnoreCase)) continue;

                _temporaryRules[index] = _temporaryRules[index] with
                {
                    Rule = _temporaryRules[index].Rule with
                    {
                        IsDisabledBecauseTargetDesktopIsMissing = false,
                        TargetDesktopIdentifier = targetSnapshot.DesktopIdentifier,
                        TargetDesktopNumber = targetSnapshot.DesktopNumber,
                        TargetDesktopDisplayName = targetSnapshot.DisplayName
                    }
                };
                wasUpdated = true;
            }
        }

        if (!wasUpdated) return false;

        _fileLogService.WriteInformation(nameof(ProcessDesktopPlacementService), $"Updated temporary process desktop placement rule target. ProcessName={normalizedProcessName}, TargetDesktopNumber={targetSnapshot.DesktopNumber}.");
        OnTemporaryRulesChanged();
        return true;
    }

    private static ProcessDesktopPlacementTemporaryRuleSnapshot CreateTemporaryRuleSnapshot(TemporaryProcessDesktopPlacementRule temporaryRule) => new()
    {
        Rule = temporaryRule.Rule,
        Lifetime = temporaryRule.Lifetime,
        ExpiresAt = temporaryRule.ExpiresAt,
        CreatedAt = temporaryRule.CreatedAt,
        IsProcessRunning = !HasProcessExited(temporaryRule.Rule.ProcessName)
    };

    private static string FormatLastWindowsErrorDetails(int lastWindowsErrorCode) => $"ErrorCode={lastWindowsErrorCode} (0x{lastWindowsErrorCode:X8}, {new Win32Exception(lastWindowsErrorCode).Message})";

    private static bool HasProcessExited(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName);
            try { return processes.Length == 0; }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        catch (InvalidOperationException) { return true; }
    }

    private static bool HasProcessExited(uint processIdentifier)
    {
        try
        {
            using var process = Process.GetProcessById((int)processIdentifier);
            return process.HasExited;
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch (Win32Exception) { return true; }
    }

    private static bool IsRuleTargetAlreadySatisfied(ProcessDesktopPlacementWindowSnapshot windowSnapshot, ProcessDesktopPlacementRuleSettings processDesktopPlacementRule)
        => windowSnapshot.DesktopNumber == Math.Max(1, processDesktopPlacementRule.TargetDesktopNumber)
        || windowSnapshot.DesktopNumber <= 0 && string.Equals(windowSnapshot.DesktopIdentifier, processDesktopPlacementRule.TargetDesktopIdentifier, StringComparison.OrdinalIgnoreCase);

    private static bool IsWindowEventCandidate(nint windowHandle)
    {
        if (windowHandle == 0 || windowHandle == Win32.GetShellWindow()) return false;

        if (Win32.GetAncestor(windowHandle, Win32.GetAncestorRootFlag) != windowHandle) return false;

        return Win32.IsWindowVisible(windowHandle) && !Win32.IsIconic(windowHandle);
    }

    private static bool ShouldCreateMissingTargetDesktopForPlacement(DeskBorderSettings currentSettings, bool isPersistentRule)
    {
        var processDesktopPlacementSettings = currentSettings.ProcessDesktopPlacementSettings;
        return processDesktopPlacementSettings.ShouldCreateMissingTargetDesktop
            && (!isPersistentRule || !processDesktopPlacementSettings.ShouldDisableRuleWhenTargetDesktopIsMissing);
    }

    private static ProcessDesktopPlacementRuleSettings ApplyPlacementResultTarget(ProcessDesktopPlacementRuleSettings processDesktopPlacementRule, ProcessDesktopPlacementResult processDesktopPlacementResult) => processDesktopPlacementRule with
    {
        IsDisabledBecauseTargetDesktopIsMissing = false,
        TargetDesktopIdentifier = processDesktopPlacementResult.TargetDesktopIdentifier ?? processDesktopPlacementRule.TargetDesktopIdentifier,
        TargetDesktopNumber = processDesktopPlacementResult.TargetDesktopNumber,
        TargetDesktopDisplayName = processDesktopPlacementResult.TargetDesktopDisplayName ?? processDesktopPlacementRule.TargetDesktopDisplayName
    };

    private TemporaryProcessDesktopPlacementRule[] GetActiveTemporaryRulesSnapshot()
    {
        var currentTimestamp = DateTimeOffset.UtcNow;
        var wereTemporaryRulesPruned = false;
        TemporaryProcessDesktopPlacementRule[] temporaryRulesSnapshot;
        lock (_temporaryRulesLock)
        {
            wereTemporaryRulesPruned = PruneExpiredTemporaryRules(currentTimestamp);
            temporaryRulesSnapshot = [.. _temporaryRules];
        }

        if (wereTemporaryRulesPruned) OnTemporaryRulesChanged();

        return temporaryRulesSnapshot;
    }

    private ProcessDesktopPlacementRuleSettings? TryFindRuleForWindow(ProcessDesktopPlacementWindowSnapshot windowSnapshot, DeskBorderSettings currentSettings, out bool isPersistentRule)
    {
        isPersistentRule = false;
        var activeTemporaryRules = GetActiveTemporaryRulesSnapshot();
        for (var index = activeTemporaryRules.Length - 1; index >= 0; index--)
        {
            if (!string.Equals(activeTemporaryRules[index].Rule.ProcessName, windowSnapshot.ProcessName, StringComparison.OrdinalIgnoreCase)) continue;

            return activeTemporaryRules[index].Rule;
        }

        if (!currentSettings.ProcessDesktopPlacementSettings.IsEnabled) return null;

        foreach (var persistentRule in currentSettings.ProcessDesktopPlacementSettings.Rules)
        {
            if (!persistentRule.IsEnabled
                || persistentRule.IsDisabledBecauseTargetDesktopIsMissing
                || !string.Equals(persistentRule.ProcessName, windowSnapshot.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            isPersistentRule = true;
            return persistentRule;
        }

        return null;
    }

    private async Task HandlePlacementResultAsync(ProcessDesktopPlacementRuleSettings processDesktopPlacementRule, ProcessDesktopPlacementResult processDesktopPlacementResult, bool isPersistentRule)
    {
        if (!processDesktopPlacementResult.WasTargetDesktopCreated
            && processDesktopPlacementResult.OperationStatus != ProcessDesktopPlacementOperationStatus.TargetDesktopNotFound)
        {
            return;
        }

        var currentSettings = _settingsService.Settings;
        if (processDesktopPlacementResult.IsSuccessful
            && !string.IsNullOrWhiteSpace(processDesktopPlacementResult.TargetDesktopIdentifier)
            && ShouldUpdateRuleTarget(processDesktopPlacementRule, processDesktopPlacementResult))
        {
            UpdateTemporaryRulesByTargetDesktopNumber(
                processDesktopPlacementRule.TargetDesktopNumber,
                rule => ApplyPlacementResultTarget(rule, processDesktopPlacementResult));
            if (currentSettings.ProcessDesktopPlacementSettings.Rules.Any(rule => Math.Max(1, rule.TargetDesktopNumber) == Math.Max(1, processDesktopPlacementRule.TargetDesktopNumber)))
            {
                await UpdatePersistentRulesByTargetDesktopNumberAsync(
                    currentSettings,
                    processDesktopPlacementRule.TargetDesktopNumber,
                    rule => ApplyPlacementResultTarget(rule, processDesktopPlacementResult));
            }

            return;
        }

        if (isPersistentRule
            && processDesktopPlacementResult.OperationStatus == ProcessDesktopPlacementOperationStatus.TargetDesktopNotFound
            && currentSettings.ProcessDesktopPlacementSettings.ShouldDisableRuleWhenTargetDesktopIsMissing)
        {
            await UpdatePersistentRulesByTargetDesktopNumberAsync(
                currentSettings,
                processDesktopPlacementRule.TargetDesktopNumber,
                rule => rule with { IsDisabledBecauseTargetDesktopIsMissing = true });
        }
    }

    private static PendingProcessDesktopPlacementOperation? FindPendingPlacementOperation(
        IReadOnlyList<PendingProcessDesktopPlacementOperation> placementOperations,
        ProcessDesktopPlacementRuleSettings processDesktopPlacementRule,
        bool isPersistentRule)
    {
        foreach (var placementOperation in placementOperations)
        {
            var isMatchingPlacementOperation = placementOperation.IsPersistentRule == isPersistentRule
                && string.Equals(placementOperation.ProcessDesktopPlacementRule.ProcessName, processDesktopPlacementRule.ProcessName, StringComparison.OrdinalIgnoreCase)
                && Math.Max(1, placementOperation.ProcessDesktopPlacementRule.TargetDesktopNumber) == Math.Max(1, processDesktopPlacementRule.TargetDesktopNumber);
            if (isMatchingPlacementOperation) return placementOperation;
        }

        return null;
    }

    private static void AddPendingPlacementOperation(
        List<PendingProcessDesktopPlacementOperation> placementOperations,
        ProcessDesktopPlacementWindowSnapshot windowSnapshot,
        ProcessDesktopPlacementRuleSettings processDesktopPlacementRule,
        bool isPersistentRule)
    {
        var placementOperation = FindPendingPlacementOperation(placementOperations, processDesktopPlacementRule, isPersistentRule);
        if (placementOperation is null)
        {
            placementOperation = new(processDesktopPlacementRule, isPersistentRule);
            placementOperations.Add(placementOperation);
        }

        placementOperation.WindowHandles.Add(windowSnapshot.WindowHandle);
    }

    private void TryQueuePlacementOperation(
        List<PendingProcessDesktopPlacementOperation> placementOperations,
        ProcessDesktopPlacementWindowSnapshot windowSnapshot,
        DeskBorderSettings currentSettings)
    {
        if (TryFindRuleForWindow(windowSnapshot, currentSettings, out var isPersistentRule) is not { } processDesktopPlacementRule) return;

        if (IsRuleTargetAlreadySatisfied(windowSnapshot, processDesktopPlacementRule)) return;

        AddPendingPlacementOperation(placementOperations, windowSnapshot, processDesktopPlacementRule, isPersistentRule);
    }

    private async Task ApplyPlacementOperationsAsync(IReadOnlyList<PendingProcessDesktopPlacementOperation> placementOperations, DeskBorderSettings currentSettings)
    {
        var shouldSwitchToFirstMovedTargetDesktop = currentSettings.ProcessDesktopPlacementSettings.ShouldSwitchToTargetDesktopAfterPlacement;
        var hasSwitchedToTargetDesktop = false;
        foreach (var placementOperation in placementOperations)
        {
            var originalProcessDesktopPlacementRule = placementOperation.ProcessDesktopPlacementRule;
            var processDesktopPlacementResult = _virtualDesktopService.PlaceWindowsOnDesktop(
                placementOperation.WindowHandles,
                originalProcessDesktopPlacementRule,
                shouldSwitchToFirstMovedTargetDesktop && !hasSwitchedToTargetDesktop,
                ShouldCreateMissingTargetDesktopForPlacement(currentSettings, placementOperation.IsPersistentRule));
            hasSwitchedToTargetDesktop |= processDesktopPlacementResult.DidSwitchToTargetDesktop;
            if (processDesktopPlacementResult.IsSuccessful && !string.IsNullOrWhiteSpace(processDesktopPlacementResult.TargetDesktopIdentifier)) UpdatePendingPlacementOperationTargets(placementOperations, originalProcessDesktopPlacementRule.TargetDesktopNumber, processDesktopPlacementResult);

            await HandlePlacementResultAsync(
                originalProcessDesktopPlacementRule,
                processDesktopPlacementResult,
                placementOperation.IsPersistentRule);
        }
    }

    private async Task RefreshAsync()
    {
        if (!IsRunning) return;

        await _refreshSemaphore.WaitAsync();
        try
        {
            var currentSettings = _settingsService.Settings;
            var currentWindowSnapshots = _virtualDesktopService.GetProcessDesktopPlacementWindowSnapshots();
            var currentProcessIdentifiers = currentWindowSnapshots
                .Select(windowSnapshot => windowSnapshot.ProcessIdentifier)
                .Where(processIdentifier => processIdentifier != 0)
                .ToHashSet();
            var currentWindowHandles = currentWindowSnapshots.Select(windowSnapshot => windowSnapshot.WindowHandle).ToHashSet();
            var placementOperations = new List<PendingProcessDesktopPlacementOperation>();
            _knownProcessIdentifiers.RemoveWhere(HasProcessExited);
            _knownWindowHandles.IntersectWith(currentWindowHandles);
            var knownProcessIdentifiersBeforeRefresh = _knownProcessIdentifiers.ToHashSet();
            foreach (var windowSnapshot in currentWindowSnapshots)
            {
                if (!_knownWindowHandles.Add(windowSnapshot.WindowHandle)) continue;

                var isNewProcessInstance = windowSnapshot.ProcessIdentifier != 0
                    && !knownProcessIdentifiersBeforeRefresh.Contains(windowSnapshot.ProcessIdentifier);
                if (!currentSettings.ProcessDesktopPlacementSettings.ShouldApplyRulesWhenProcessStarts && isNewProcessInstance) continue;

                TryQueuePlacementOperation(placementOperations, windowSnapshot, currentSettings);
            }

            _knownProcessIdentifiers.UnionWith(currentProcessIdentifiers);
            await ApplyPlacementOperationsAsync(placementOperations, currentSettings);
        }
        finally { _refreshSemaphore.Release(); }
    }

    private void QueueEventDrivenRefresh()
    {
        if (!IsRunning || _monitoringCancellationTokenSource is null) return;

        var cancellationToken = _monitoringCancellationTokenSource.Token;
        lock (_eventDrivenRefreshLock)
        {
            _eventDrivenRefreshRequestVersion++;
            if (_eventDrivenRefreshTask is { IsCompleted: false }) return;

            _eventDrivenRefreshTask = RunEventDrivenRefreshPumpAsync(cancellationToken);
        }
    }

    private async Task RunEventDrivenRefreshPumpAsync(CancellationToken cancellationToken)
    {
        var observedRequestVersion = Volatile.Read(ref _eventDrivenRefreshRequestVersion);
        while (true)
        {
            try
            {
                await Task.Delay(s_eventDrivenRefreshDelay, cancellationToken);
                await RefreshAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception exception) { _fileLogService.WriteWarning(nameof(ProcessDesktopPlacementService), "Process desktop placement event-driven refresh failed.", exception); }

            lock (_eventDrivenRefreshLock)
            {
                var latestRequestVersion = _eventDrivenRefreshRequestVersion;
                if (latestRequestVersion == observedRequestVersion)
                {
                    _eventDrivenRefreshTask = null;
                    return;
                }

                observedRequestVersion = latestRequestVersion;
            }
        }
    }

    private async Task RunFallbackPollingLoopAsync(CancellationToken cancellationToken)
    {
        using var periodicTimer = new PeriodicTimer(s_fallbackPollingInterval);
        while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
        {
            try { await RefreshAsync(); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { _fileLogService.WriteWarning(nameof(ProcessDesktopPlacementService), "Process desktop placement fallback polling refresh failed.", exception); }
        }
    }

    private void RunWindowEventHookMessageLoopOnBackgroundThread()
    {
        try
        {
            _windowEventHookThreadIdentifier = Win32.GetCurrentThreadId();
            _ = Win32.PeekMessage(out _, 0, 0, 0, Win32.PeekMessageNoRemove);
            if (_windowEventHookCallback is null)
            {
                _fileLogService.WriteWarning(nameof(ProcessDesktopPlacementService), "Process desktop placement window event hook callback was unavailable.");
                return;
            }

            _windowEventHookHandle = Win32.SetWinEventHook(
                Win32.EventObjectShow,
                Win32.EventObjectShow,
                0,
                _windowEventHookCallback,
                0,
                0,
                Win32.WinEventOutOfContext | Win32.WinEventSkipOwnProcess);
            if (_windowEventHookHandle == 0)
            {
                _fileLogService.WriteWarning(nameof(ProcessDesktopPlacementService), $"Failed to install process desktop placement window event hook. {FormatLastWindowsErrorDetails(Marshal.GetLastWin32Error())}. Fallback polling remains active.");
                return;
            }

            _fileLogService.WriteInformation(nameof(ProcessDesktopPlacementService), "Installed process desktop placement window event hook.");
            _windowEventHookReadySignal.Set();
            while (true)
            {
                var messageResult = Win32.GetMessage(out var nativeMessage, 0, 0, 0);
                if (messageResult == 0) return;
                if (messageResult < 0)
                {
                    _fileLogService.WriteWarning(nameof(ProcessDesktopPlacementService), $"Process desktop placement window event hook message loop failed. {FormatLastWindowsErrorDetails(Marshal.GetLastWin32Error())}.");
                    return;
                }

                if (nativeMessage.Message != ShutdownWindowEventHookMessage) continue;

                Win32.PostQuitMessage(0);
            }
        }
        catch (Exception exception)
        {
            _fileLogService.WriteError(nameof(ProcessDesktopPlacementService), $"Process desktop placement window event hook failed unexpectedly. ExceptionHResult=0x{exception.HResult:X8}, {FormatLastWindowsErrorDetails(Marshal.GetLastWin32Error())}.", exception);
        }
        finally
        {
            if (_windowEventHookHandle != 0 && !Win32.UnhookWinEvent(_windowEventHookHandle)) _fileLogService.WriteWarning(nameof(ProcessDesktopPlacementService), $"Failed to uninstall process desktop placement window event hook. {FormatLastWindowsErrorDetails(Marshal.GetLastWin32Error())}.");

            _windowEventHookHandle = 0;
            _windowEventHookThreadIdentifier = 0;
            _windowEventHookReadySignal.Set();
        }
    }

    private void OnWindowEventHookCallback(nint _, uint eventType, nint windowHandle, int objectIdentifier, int childIdentifier, uint __, uint ___)
    {
        var isMatchingWindowEvent = eventType == Win32.EventObjectShow
            && objectIdentifier == Win32.ObjectIdentifierWindow
            && childIdentifier == Win32.ChildIdentifierSelf
            && IsWindowEventCandidate(windowHandle);
        if (!isMatchingWindowEvent) return;

        QueueEventDrivenRefresh();
    }

    private void StartWindowEventHook()
    {
        _windowEventHookReadySignal.Reset();
        _windowEventHookCallback = OnWindowEventHookCallback;
        _windowEventHookThread = new Thread(RunWindowEventHookMessageLoopOnBackgroundThread)
        {
            IsBackground = true,
            Name = "DeskBorder Process Desktop Placement Window Event Hook"
        };
        _windowEventHookThread.SetApartmentState(ApartmentState.STA);
        _windowEventHookThread.Start();
        if (!_windowEventHookReadySignal.Wait(TimeSpan.FromSeconds(5))) _fileLogService.WriteWarning(nameof(ProcessDesktopPlacementService), "Timed out while waiting for the process desktop placement window event hook to start. Fallback polling remains active.");
    }

    private void StopWindowEventHook()
    {
        if (_windowEventHookThread is null) return;

        if (!_windowEventHookThread.Join(TimeSpan.FromSeconds(2))) _fileLogService.WriteWarning(nameof(ProcessDesktopPlacementService), "Timed out while stopping the process desktop placement window event hook.");

        _windowEventHookThread = null;
        _windowEventHookCallback = null;
    }

    private void TryPostWindowEventHookShutdownMessage()
    {
        if (_windowEventHookThreadIdentifier == 0) return;

        if (!Win32.PostThreadMessage(_windowEventHookThreadIdentifier, ShutdownWindowEventHookMessage, 0, 0)) _fileLogService.WriteWarning(nameof(ProcessDesktopPlacementService), $"Failed to schedule process desktop placement window event hook shutdown. {FormatLastWindowsErrorDetails(Marshal.GetLastWin32Error())}.");
    }

    private void OnSettingsServiceSettingsChanged(object? _, EventArgs __)
    {
        _ = RefreshAfterSettingsChangedAsync();
    }

    private void OnTemporaryRulesChanged() => TemporaryRulesChanged?.Invoke(this, EventArgs.Empty);

    private bool PruneExpiredTemporaryRules(DateTimeOffset currentTimestamp)
        => _temporaryRules.RemoveAll(rule =>
            rule.ExpiresAt <= currentTimestamp
            || rule.Lifetime == ProcessDesktopPlacementTemporaryRuleLifetime.UntilProcessExit && HasProcessExited(rule.Rule.ProcessName)) > 0;

    private async Task RefreshAfterSettingsChangedAsync()
    {
        try { await RefreshAsync(); }
        catch (Exception exception) { _fileLogService.WriteWarning(nameof(ProcessDesktopPlacementService), "Process desktop placement settings refresh failed.", exception); }
    }

    private static void UpdatePendingPlacementOperationTargets(
        IReadOnlyList<PendingProcessDesktopPlacementOperation> placementOperations,
        int targetDesktopNumber,
        ProcessDesktopPlacementResult processDesktopPlacementResult)
    {
        foreach (var placementOperation in placementOperations)
        {
            if (Math.Max(1, placementOperation.ProcessDesktopPlacementRule.TargetDesktopNumber) != Math.Max(1, targetDesktopNumber)) continue;

            placementOperation.ProcessDesktopPlacementRule = ApplyPlacementResultTarget(
                placementOperation.ProcessDesktopPlacementRule,
                processDesktopPlacementResult);
        }
    }

    private static bool ShouldUpdateRuleTarget(ProcessDesktopPlacementRuleSettings processDesktopPlacementRule, ProcessDesktopPlacementResult processDesktopPlacementResult)
        => !string.Equals(processDesktopPlacementRule.TargetDesktopIdentifier, processDesktopPlacementResult.TargetDesktopIdentifier, StringComparison.OrdinalIgnoreCase)
        || Math.Max(1, processDesktopPlacementRule.TargetDesktopNumber) != Math.Max(1, processDesktopPlacementResult.TargetDesktopNumber)
        || !string.Equals(processDesktopPlacementRule.TargetDesktopDisplayName, processDesktopPlacementResult.TargetDesktopDisplayName, StringComparison.Ordinal);

    private async Task UpdatePersistentRulesByTargetDesktopNumberAsync(DeskBorderSettings currentSettings, int targetDesktopNumber, Func<ProcessDesktopPlacementRuleSettings, ProcessDesktopPlacementRuleSettings> updateRule)
    {
        var updatedRules = currentSettings.ProcessDesktopPlacementSettings.Rules
            .Select(rule => Math.Max(1, rule.TargetDesktopNumber) == Math.Max(1, targetDesktopNumber) ? updateRule(rule) : rule)
            .ToArray();
        await _settingsService.UpdateSettingsAsync(currentSettings with
        {
            ProcessDesktopPlacementSettings = currentSettings.ProcessDesktopPlacementSettings with
            {
                Rules = updatedRules
            }
        });
    }

    private void UpdateTemporaryRulesByTargetDesktopNumber(int targetDesktopNumber, Func<ProcessDesktopPlacementRuleSettings, ProcessDesktopPlacementRuleSettings> updateRule)
    {
        lock (_temporaryRulesLock)
        {
            for (var index = 0; index < _temporaryRules.Count; index++)
            {
                if (Math.Max(1, _temporaryRules[index].Rule.TargetDesktopNumber) != Math.Max(1, targetDesktopNumber)) continue;

                _temporaryRules[index] = _temporaryRules[index] with
                {
                    Rule = updateRule(_temporaryRules[index].Rule)
                };
            }
        }
    }

    private sealed record TemporaryProcessDesktopPlacementRule(
        ProcessDesktopPlacementRuleSettings Rule,
        ProcessDesktopPlacementTemporaryRuleLifetime Lifetime,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset CreatedAt);

    private sealed class PendingProcessDesktopPlacementOperation(ProcessDesktopPlacementRuleSettings processDesktopPlacementRule, bool isPersistentRule)
    {
        public ProcessDesktopPlacementRuleSettings ProcessDesktopPlacementRule { get; set; } = processDesktopPlacementRule;

        public bool IsPersistentRule { get; } = isPersistentRule;

        public List<nint> WindowHandles { get; } = [];
    }
}
