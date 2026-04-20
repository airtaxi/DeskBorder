using DeskBorder.Helpers;
using System.Threading;

namespace DeskBorder.Services;

public sealed class DeskBorderRuntimeService(IDesktopLifecycleService desktopLifecycleService, IFileLogService fileLogService) : IDeskBorderRuntimeService
{
    private readonly IDesktopLifecycleService _desktopLifecycleService = desktopLifecycleService;
    private readonly IFileLogService _fileLogService = fileLogService;
    private readonly SemaphoreSlim _stateTransitionSemaphore = new(1, 1);
    private bool _isLifecycleRunning;
    private int _suspensionCount;

    public event EventHandler? StateChanged;

    public bool IsRunning { get; private set; }

    public bool IsSuspended => _suspensionCount > 0;

    public string StatusMessage => IsSuspended
        ? LocalizedResourceAccessor.GetString("Runtime.Status.Paused")
        : IsRunning
            ? LocalizedResourceAccessor.GetString("Runtime.Status.Running")
            : LocalizedResourceAccessor.GetString("Runtime.Status.Stopped");

    public Task StartAsync() => SetRunningStateAsync(true);

    public Task StopAsync() => SetRunningStateAsync(false);

    public async Task<IAsyncDisposable> CreateSuspensionAsync()
    {
        await _stateTransitionSemaphore.WaitAsync();
        try
        {
            _suspensionCount++;
            _fileLogService.WriteInformation(nameof(DeskBorderRuntimeService), $"Created runtime suspension. SuspensionCount={_suspensionCount}.");
            await ApplyEffectiveRunningStateAsync();
        }
        finally { _stateTransitionSemaphore.Release(); }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return new RuntimeSuspension(this);
    }

    public async Task SetRunningStateAsync(bool isRunning)
    {
        await _stateTransitionSemaphore.WaitAsync();
        try
        {
            _fileLogService.WriteInformation(nameof(DeskBorderRuntimeService), $"Requested runtime state change. IsRunning={isRunning}, IsSuspended={IsSuspended}.");
            if (IsRunning == isRunning)
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            IsRunning = isRunning;
            await ApplyEffectiveRunningStateAsync();
        }
        finally { _stateTransitionSemaphore.Release(); }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ApplyEffectiveRunningStateAsync()
    {
        var shouldRunLifecycle = IsRunning && !IsSuspended;
        if (_isLifecycleRunning == shouldRunLifecycle)
            return;

        if (shouldRunLifecycle)
        {
            try
            {
                await _desktopLifecycleService.StartAsync();
            }
            catch (Exception exception)
            {
                _fileLogService.WriteError(nameof(DeskBorderRuntimeService), "Failed to start desktop lifecycle service.", exception);
                return;
            }
        }
        else
        {
            try
            {
                await _desktopLifecycleService.StopAsync();
            }
            catch (Exception exception)
            {
                _fileLogService.WriteError(nameof(DeskBorderRuntimeService), "Failed to stop desktop lifecycle service.", exception);
            }
        }

        _isLifecycleRunning = shouldRunLifecycle;
        _fileLogService.WriteInformation(nameof(DeskBorderRuntimeService), $"Applied effective lifecycle state. LifecycleRunning={_isLifecycleRunning}.");
    }

    private async ValueTask ReleaseSuspensionAsync()
    {
        await _stateTransitionSemaphore.WaitAsync();
        try
        {
            if (_suspensionCount == 0)
                return;

            _suspensionCount--;
            _fileLogService.WriteInformation(nameof(DeskBorderRuntimeService), $"Released runtime suspension. SuspensionCount={_suspensionCount}.");
            await ApplyEffectiveRunningStateAsync();
        }
        finally { _stateTransitionSemaphore.Release(); }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class RuntimeSuspension(DeskBorderRuntimeService deskBorderRuntimeService) : IAsyncDisposable
    {
        private readonly DeskBorderRuntimeService _deskBorderRuntimeService = deskBorderRuntimeService;
        private bool _isDisposed;

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            await _deskBorderRuntimeService.ReleaseSuspensionAsync();
        }
    }
}
