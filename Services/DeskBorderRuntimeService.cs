using DeskBorder.Helpers;

namespace DeskBorder.Services;

public sealed class DeskBorderRuntimeService(IDesktopLifecycleService desktopLifecycleService) : IDeskBorderRuntimeService
{
    private readonly IDesktopLifecycleService _desktopLifecycleService = desktopLifecycleService;

    public event EventHandler? StateChanged;

    public bool IsRunning { get; private set; }

    public string StatusMessage => IsRunning
        ? LocalizedResourceAccessor.GetString("Runtime.Status.Running")
        : LocalizedResourceAccessor.GetString("Runtime.Status.Stopped");

    public Task StartAsync() => SetRunningStateAsync(true);

    public Task StopAsync() => SetRunningStateAsync(false);

    public async Task SetRunningStateAsync(bool isRunning)
    {
        if (IsRunning == isRunning)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        IsRunning = isRunning;
        if (isRunning)
            await _desktopLifecycleService.StartAsync();
        else
            await _desktopLifecycleService.StopAsync();

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
