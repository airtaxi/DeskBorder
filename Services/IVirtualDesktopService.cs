using DeskBorder.Models;

namespace DeskBorder.Services;

public interface IVirtualDesktopService
{
    VirtualDesktopWorkspaceSnapshot GetWorkspaceSnapshot();

    NavigatorPreviewSnapshot GetNavigatorPreviewSnapshot(DisplayMonitorInfo targetDisplayMonitor);

    ProcessDesktopPlacementTargetSnapshot GetCurrentProcessDesktopPlacementTarget();

    ProcessDesktopPlacementTargetSnapshot GetProcessDesktopPlacementTarget(int desktopNumber);

    ProcessDesktopPlacementWindowSnapshot? GetForegroundProcessDesktopPlacementWindowSnapshot();

    ProcessDesktopPlacementWindowSnapshot[] GetProcessDesktopPlacementWindowSnapshots();

    ProcessDesktopPlacementResult PlaceWindowsOnDesktop(IReadOnlyList<nint> windowHandles, ProcessDesktopPlacementRuleSettings processDesktopPlacementRule, bool shouldSwitchToTargetDesktop, bool shouldCreateMissingTargetDesktop);

    DesktopNavigationResult SwitchDesktop(DesktopSwitchDirection desktopSwitchDirection);

    DesktopNavigationResult MoveFocusedWindowToAdjacentDesktop(DesktopSwitchDirection desktopSwitchDirection);

    DesktopNavigationResult CreateDesktopAndSwitch(DesktopSwitchDirection desktopSwitchDirection);

    DesktopNavigationResult SwitchToDesktop(string desktopIdentifier);

    bool IsDesktopEmpty(string desktopIdentifier);

    DesktopAutoDeletionValidationResult EvaluateAutoDeletion(string sourceDesktopIdentifier, string targetDesktopIdentifier);

    DesktopDeletionResult DeleteDesktop(string desktopIdentifier, string fallbackDesktopIdentifier);
}
