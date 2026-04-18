namespace DeskBorder.Services;

public interface IDeskBorderRuntimeService
{
    event EventHandler? StateChanged;

    bool IsRunning { get; }

    bool IsSuspended { get; }

    string StatusMessage { get; }

    Task StartAsync();

    Task StopAsync();

    Task SetRunningStateAsync(bool isRunning);

    Task<IAsyncDisposable> CreateSuspensionAsync();
}
