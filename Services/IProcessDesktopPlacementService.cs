using DeskBorder.Models;

namespace DeskBorder.Services;

public interface IProcessDesktopPlacementService
{
    event EventHandler? TemporaryRulesChanged;

    bool IsRunning { get; }

    void AddTemporaryRule(ProcessDesktopPlacementRuleSettings processDesktopPlacementRule, ProcessDesktopPlacementTemporaryRuleLifetime lifetime, TimeSpan? duration = null);

    Task ApplyRuleToWindowAsync(nint windowHandle, ProcessDesktopPlacementRuleSettings processDesktopPlacementRule);

    ProcessDesktopPlacementWindowSnapshot? GetForegroundWindowSnapshot();

    IReadOnlyList<ProcessDesktopPlacementTemporaryRuleSnapshot> GetTemporaryRules();

    bool RemoveTemporaryRule(string processName);

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();

    bool UpdateTemporaryRuleTarget(string processName, ProcessDesktopPlacementTargetSnapshot targetSnapshot);
}
