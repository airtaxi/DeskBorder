using DeskBorder.Models;

namespace DeskBorder.Helpers;

public static class ProcessDesktopPlacementRuleStateHelper
{
    public static ProcessDesktopPlacementRuleSettings[] CreateRulesWithMissingTargetDisabledState(
        IReadOnlyList<ProcessDesktopPlacementRuleSettings> processDesktopPlacementRules,
        bool shouldDisableRuleWhenTargetDesktopIsMissing,
        int desktopCount,
        out bool hasRuleChanged)
    {
        var normalizedDesktopCount = Math.Max(1, desktopCount);
        var updatedRules = new ProcessDesktopPlacementRuleSettings[processDesktopPlacementRules.Count];
        hasRuleChanged = false;
        for (var index = 0; index < processDesktopPlacementRules.Count; index++)
        {
            var processDesktopPlacementRule = processDesktopPlacementRules[index];
            var isDisabledBecauseTargetDesktopIsMissing = ShouldDisableBecauseTargetDesktopIsMissing(
                shouldDisableRuleWhenTargetDesktopIsMissing,
                processDesktopPlacementRule.TargetDesktopNumber,
                normalizedDesktopCount);
            if (processDesktopPlacementRule.IsDisabledBecauseTargetDesktopIsMissing == isDisabledBecauseTargetDesktopIsMissing)
            {
                updatedRules[index] = processDesktopPlacementRule;
                continue;
            }

            updatedRules[index] = processDesktopPlacementRule with { IsDisabledBecauseTargetDesktopIsMissing = isDisabledBecauseTargetDesktopIsMissing };
            hasRuleChanged = true;
        }

        return updatedRules;
    }

    public static bool ShouldDisableBecauseTargetDesktopIsMissing(
        bool shouldDisableRuleWhenTargetDesktopIsMissing,
        int targetDesktopNumber,
        int desktopCount)
        => shouldDisableRuleWhenTargetDesktopIsMissing
            && Math.Max(1, targetDesktopNumber) > Math.Max(1, desktopCount);
}
