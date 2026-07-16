using CommunityToolkit.Mvvm.ComponentModel;
using DeskBorder.Helpers;
using DeskBorder.Models;

namespace DeskBorder.ViewModels;

public sealed partial class ProcessDesktopPlacementRuleViewModel : ObservableObject
{
    [ObservableProperty]
    public partial DateTimeOffset CreatedAt { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? ExpiresAt { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsDisabledBecauseTargetDesktopIsMissing { get; set; }

    [ObservableProperty]
    public partial bool IsLifetimeProcessRunning { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPersistentRule))]
    public partial ProcessDesktopPlacementRuleLifetime Lifetime { get; set; } = ProcessDesktopPlacementRuleLifetime.Permanent;

    [ObservableProperty]
    public partial string ProcessName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RuleLifetimeStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetDesktopDisplayText))]
    public partial int TargetDesktopNumber { get; set; }

    public bool IsPersistentRule => Lifetime == ProcessDesktopPlacementRuleLifetime.Permanent;

    public string TargetDesktopDisplayText => SettingsDisplayFormatter.FormatDesktopDisplayName(TargetDesktopNumber);

    public ProcessDesktopPlacementRuleSettings CreateSettings(bool shouldPreserveMissingTargetDisabledFlag) => new()
    {
        IsEnabled = IsEnabled,
        IsDisabledBecauseTargetDesktopIsMissing = shouldPreserveMissingTargetDisabledFlag && IsDisabledBecauseTargetDesktopIsMissing,
        ProcessName = ProcessName,
        TargetDesktopNumber = TargetDesktopNumber
    };

    public static ProcessDesktopPlacementRuleViewModel Load(ProcessDesktopPlacementRuleSettings processDesktopPlacementRuleSettings)
    {
        var processDesktopPlacementRuleViewModel = new ProcessDesktopPlacementRuleViewModel
        {
            IsEnabled = processDesktopPlacementRuleSettings.IsEnabled,
            IsDisabledBecauseTargetDesktopIsMissing = processDesktopPlacementRuleSettings.IsDisabledBecauseTargetDesktopIsMissing,
            ProcessName = processDesktopPlacementRuleSettings.ProcessName,
            TargetDesktopNumber = processDesktopPlacementRuleSettings.TargetDesktopNumber,
            Lifetime = ProcessDesktopPlacementRuleLifetime.Permanent
        };
        processDesktopPlacementRuleViewModel.RefreshLifetimeStatus(DateTimeOffset.UtcNow);
        return processDesktopPlacementRuleViewModel;
    }

    public static ProcessDesktopPlacementRuleViewModel Load(ProcessDesktopPlacementTemporaryRuleSnapshot temporaryRuleSnapshot)
    {
        var processDesktopPlacementRuleViewModel = Load(temporaryRuleSnapshot.Rule);
        processDesktopPlacementRuleViewModel.Lifetime = temporaryRuleSnapshot.Lifetime;
        processDesktopPlacementRuleViewModel.ExpiresAt = temporaryRuleSnapshot.ExpiresAt;
        processDesktopPlacementRuleViewModel.CreatedAt = temporaryRuleSnapshot.CreatedAt;
        processDesktopPlacementRuleViewModel.IsLifetimeProcessRunning = temporaryRuleSnapshot.IsProcessRunning;
        processDesktopPlacementRuleViewModel.RefreshLifetimeStatus(DateTimeOffset.UtcNow);
        return processDesktopPlacementRuleViewModel;
    }

    public void RefreshLifetimeStatus(DateTimeOffset currentTimestamp)
    {
        var ruleLifetimeStatusText = Lifetime switch
        {
            ProcessDesktopPlacementRuleLifetime.Timed => FormatTimedLifetimeStatus(currentTimestamp),
            ProcessDesktopPlacementRuleLifetime.UntilProcessExit => LocalizedResourceAccessor.GetString(IsLifetimeProcessRunning ? "Settings.ProcessDesktopPlacement.RuleLifetime.UntilProcessExitRunning" : "Settings.ProcessDesktopPlacement.RuleLifetime.UntilProcessExitNotRunning"),
            _ => LocalizedResourceAccessor.GetString("Settings.ProcessDesktopPlacement.RuleLifetime.Permanent")
        };
        RuleLifetimeStatusText = IsDisabledBecauseTargetDesktopIsMissing ? LocalizedResourceAccessor.GetFormattedString("Settings.ProcessDesktopPlacement.RuleDisabledBecauseTargetDesktopIsMissingFormat", ruleLifetimeStatusText) : ruleLifetimeStatusText;
    }

    public void UpdateFromSettings(ProcessDesktopPlacementRuleSettings processDesktopPlacementRuleSettings)
    {
        IsEnabled = processDesktopPlacementRuleSettings.IsEnabled;
        IsDisabledBecauseTargetDesktopIsMissing = processDesktopPlacementRuleSettings.IsDisabledBecauseTargetDesktopIsMissing;
        ProcessName = processDesktopPlacementRuleSettings.ProcessName;
        TargetDesktopNumber = processDesktopPlacementRuleSettings.TargetDesktopNumber;
        Lifetime = ProcessDesktopPlacementRuleLifetime.Permanent;
        ExpiresAt = null;
        CreatedAt = default;
        IsLifetimeProcessRunning = true;
        RefreshLifetimeStatus(DateTimeOffset.UtcNow);
    }

    public void UpdateFromTemporaryRule(ProcessDesktopPlacementTemporaryRuleSnapshot temporaryRuleSnapshot)
    {
        IsEnabled = temporaryRuleSnapshot.Rule.IsEnabled;
        IsDisabledBecauseTargetDesktopIsMissing = temporaryRuleSnapshot.Rule.IsDisabledBecauseTargetDesktopIsMissing;
        TargetDesktopNumber = temporaryRuleSnapshot.Rule.TargetDesktopNumber;
        Lifetime = temporaryRuleSnapshot.Lifetime;
        ExpiresAt = temporaryRuleSnapshot.ExpiresAt;
        CreatedAt = temporaryRuleSnapshot.CreatedAt;
        IsLifetimeProcessRunning = temporaryRuleSnapshot.IsProcessRunning;
        RefreshLifetimeStatus(DateTimeOffset.UtcNow);
    }

    private string FormatTimedLifetimeStatus(DateTimeOffset currentTimestamp)
    {
        if (!ExpiresAt.HasValue) return LocalizedResourceAccessor.GetString("Settings.ProcessDesktopPlacement.RuleLifetime.TimedExpired");

        var remainingDuration = ExpiresAt.Value - currentTimestamp;
        if (remainingDuration <= TimeSpan.Zero) return LocalizedResourceAccessor.GetString("Settings.ProcessDesktopPlacement.RuleLifetime.TimedExpired");

        var displayDuration = TimeSpan.FromSeconds(Math.Ceiling(remainingDuration.TotalSeconds));
        var durationText = displayDuration.TotalHours >= 1 ? $"{(int)displayDuration.TotalHours:00}:{displayDuration.Minutes:00}:{displayDuration.Seconds:00}" : $"{displayDuration.Minutes:00}:{displayDuration.Seconds:00}";
        return LocalizedResourceAccessor.GetFormattedString("Settings.ProcessDesktopPlacement.RuleLifetime.TimedRemainingFormat", durationText);
    }
}
