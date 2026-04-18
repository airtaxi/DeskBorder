using DeskBorder.Models;

namespace DeskBorder.Services;

public interface IDesktopEdgeMonitorService
{
    event EventHandler<DesktopEdgeMonitoringStateChangedEventArgs>? MonitoringStateChanged;

    bool IsMonitoring { get; }

    DesktopEdgeMonitoringState CurrentState { get; }

    DesktopEdgeMonitoringState CaptureCurrentState();

    void Refresh();

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();
}

public sealed class DesktopEdgeMonitoringStateChangedEventArgs(DesktopEdgeMonitoringState previousState, DesktopEdgeMonitoringState currentState) : EventArgs
{
    public DesktopEdgeMonitoringState PreviousState { get; } = previousState;

    public DesktopEdgeMonitoringState CurrentState { get; } = currentState;
}
