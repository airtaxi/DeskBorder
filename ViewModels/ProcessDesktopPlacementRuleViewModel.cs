using CommunityToolkit.Mvvm.ComponentModel;
using DeskBorder.Helpers;
using DeskBorder.Models;

namespace DeskBorder.ViewModels;

public enum ProcessDesktopPlacementRuleViewModelLifetime
{
    Permanent,
    UntilProcessExit,
    Timed,
}

public sealed partial class ProcessDesktopPlacementRuleViewModel : ObservableObject
{
    [ObservableProperty]
    public partial DateTimeOffset CreatedAt { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? ExpiresAt { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessNameDisplayText))]
    public partial bool IsDisabledBecauseTargetDesktopIsMissing { get; set; }

    [ObservableProperty]
    public partial bool IsLifetimeProcessRunning { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPersistentRule))]
    public partial ProcessDesktopPlacementRuleViewModelLifetime Lifetime { get; set; } = ProcessDesktopPlacementRuleViewModelLifetime.Permanent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessNameDisplayText))]
    public partial string ProcessName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RuleLifetimeStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TargetDesktopDisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TargetDesktopIdentifier { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int TargetDesktopNumber { get; set; }

    public bool IsPersistentRule => Lifetime == ProcessDesktopPlacementRuleViewModelLifetime.Permanent;

    public string ProcessNameDisplayText => IsDisabledBecauseTargetDesktopIsMissing
        ? LocalizedResourceAccessor.GetFormattedString("Settings.ProcessDesktopPlacement.RuleDisabledBecauseTargetDesktopIsMissingFormat", ProcessName)
        : ProcessName;

    public ProcessDesktopPlacementRuleSettings CreateSettings() => new()
    {
        IsEnabled = IsEnabled,
        IsDisabledBecauseTargetDesktopIsMissing = IsDisabledBecauseTargetDesktopIsMissing,
        ProcessName = ProcessName,
        TargetDesktopIdentifier = TargetDesktopIdentifier,
        TargetDesktopNumber = TargetDesktopNumber,
        TargetDesktopDisplayName = TargetDesktopDisplayName
    };

    public static ProcessDesktopPlacementRuleViewModel Load(ProcessDesktopPlacementRuleSettings processDesktopPlacementRuleSettings)
    {
        var processDesktopPlacementRuleViewModel = new ProcessDesktopPlacementRuleViewModel
        {
            IsEnabled = processDesktopPlacementRuleSettings.IsEnabled,
            IsDisabledBecauseTargetDesktopIsMissing = processDesktopPlacementRuleSettings.IsDisabledBecauseTargetDesktopIsMissing,
            ProcessName = processDesktopPlacementRuleSettings.ProcessName,
            TargetDesktopIdentifier = processDesktopPlacementRuleSettings.TargetDesktopIdentifier,
            TargetDesktopNumber = processDesktopPlacementRuleSettings.TargetDesktopNumber,
            TargetDesktopDisplayName = processDesktopPlacementRuleSettings.TargetDesktopDisplayName,
            Lifetime = ProcessDesktopPlacementRuleViewModelLifetime.Permanent
        };
        processDesktopPlacementRuleViewModel.RefreshLifetimeStatus(DateTimeOffset.UtcNow);
        return processDesktopPlacementRuleViewModel;
    }

    public static ProcessDesktopPlacementRuleViewModel Load(ProcessDesktopPlacementTemporaryRuleSnapshot temporaryRuleSnapshot)
    {
        var processDesktopPlacementRuleViewModel = Load(temporaryRuleSnapshot.Rule);
        processDesktopPlacementRuleViewModel.Lifetime = temporaryRuleSnapshot.Lifetime switch
        {
            ProcessDesktopPlacementTemporaryRuleLifetime.UntilProcessExit => ProcessDesktopPlacementRuleViewModelLifetime.UntilProcessExit,
            ProcessDesktopPlacementTemporaryRuleLifetime.Timed => ProcessDesktopPlacementRuleViewModelLifetime.Timed,
            _ => ProcessDesktopPlacementRuleViewModelLifetime.Permanent
        };
        processDesktopPlacementRuleViewModel.ExpiresAt = temporaryRuleSnapshot.ExpiresAt;
        processDesktopPlacementRuleViewModel.CreatedAt = temporaryRuleSnapshot.CreatedAt;
        processDesktopPlacementRuleViewModel.IsLifetimeProcessRunning = temporaryRuleSnapshot.IsProcessRunning;
        processDesktopPlacementRuleViewModel.RefreshLifetimeStatus(DateTimeOffset.UtcNow);
        return processDesktopPlacementRuleViewModel;
    }

    public void RefreshLifetimeStatus(DateTimeOffset currentTimestamp)
    {
        OnPropertyChanged(nameof(ProcessNameDisplayText));
        RuleLifetimeStatusText = Lifetime switch
        {
            ProcessDesktopPlacementRuleViewModelLifetime.Timed => FormatTimedLifetimeStatus(currentTimestamp),
            ProcessDesktopPlacementRuleViewModelLifetime.UntilProcessExit => LocalizedResourceAccessor.GetString(IsLifetimeProcessRunning
                ? "Settings.ProcessDesktopPlacement.RuleLifetime.UntilProcessExitRunning"
                : "Settings.ProcessDesktopPlacement.RuleLifetime.UntilProcessExitNotRunning"),
            _ => LocalizedResourceAccessor.GetString("Settings.ProcessDesktopPlacement.RuleLifetime.Permanent")
        };
    }

    public void SetTargetDesktop(ProcessDesktopPlacementTargetSnapshot targetSnapshot)
    {
        IsDisabledBecauseTargetDesktopIsMissing = false;
        TargetDesktopIdentifier = targetSnapshot.DesktopIdentifier;
        TargetDesktopNumber = targetSnapshot.DesktopNumber;
        TargetDesktopDisplayName = targetSnapshot.DisplayName;
    }

    public void UpdateFromSettings(ProcessDesktopPlacementRuleSettings processDesktopPlacementRuleSettings)
    {
        IsEnabled = processDesktopPlacementRuleSettings.IsEnabled;
        IsDisabledBecauseTargetDesktopIsMissing = processDesktopPlacementRuleSettings.IsDisabledBecauseTargetDesktopIsMissing;
        ProcessName = processDesktopPlacementRuleSettings.ProcessName;
        TargetDesktopIdentifier = processDesktopPlacementRuleSettings.TargetDesktopIdentifier;
        TargetDesktopNumber = processDesktopPlacementRuleSettings.TargetDesktopNumber;
        TargetDesktopDisplayName = processDesktopPlacementRuleSettings.TargetDesktopDisplayName;
        Lifetime = ProcessDesktopPlacementRuleViewModelLifetime.Permanent;
        ExpiresAt = null;
        CreatedAt = default;
        IsLifetimeProcessRunning = true;
        RefreshLifetimeStatus(DateTimeOffset.UtcNow);
    }

    public void UpdateFromTemporaryRule(ProcessDesktopPlacementTemporaryRuleSnapshot temporaryRuleSnapshot)
    {
        IsEnabled = temporaryRuleSnapshot.Rule.IsEnabled;
        IsDisabledBecauseTargetDesktopIsMissing = temporaryRuleSnapshot.Rule.IsDisabledBecauseTargetDesktopIsMissing;
        TargetDesktopIdentifier = temporaryRuleSnapshot.Rule.TargetDesktopIdentifier;
        TargetDesktopNumber = temporaryRuleSnapshot.Rule.TargetDesktopNumber;
        TargetDesktopDisplayName = temporaryRuleSnapshot.Rule.TargetDesktopDisplayName;
        Lifetime = temporaryRuleSnapshot.Lifetime switch
        {
            ProcessDesktopPlacementTemporaryRuleLifetime.UntilProcessExit => ProcessDesktopPlacementRuleViewModelLifetime.UntilProcessExit,
            ProcessDesktopPlacementTemporaryRuleLifetime.Timed => ProcessDesktopPlacementRuleViewModelLifetime.Timed,
            _ => ProcessDesktopPlacementRuleViewModelLifetime.Permanent
        };
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
        var durationText = displayDuration.TotalHours >= 1
            ? $"{(int)displayDuration.TotalHours:00}:{displayDuration.Minutes:00}:{displayDuration.Seconds:00}"
            : $"{displayDuration.Minutes:00}:{displayDuration.Seconds:00}";
        return LocalizedResourceAccessor.GetFormattedString("Settings.ProcessDesktopPlacement.RuleLifetime.TimedRemainingFormat", durationText);
    }
}
