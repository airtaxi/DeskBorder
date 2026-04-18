namespace DeskBorder.Services;

public interface IDesktopLifecycleService
{
    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();
}
