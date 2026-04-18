namespace DeskBorder.Services;

public interface IDeskBorderRuntimeService
{
    event EventHandler? StateChanged;

    bool IsRunning { get; }

    string StatusMessage { get; }

    Task StartAsync();

    Task StopAsync();

    Task SetRunningStateAsync(bool isRunning);
}
