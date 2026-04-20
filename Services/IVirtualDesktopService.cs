using DeskBorder.Models;

namespace DeskBorder.Services;

public interface IVirtualDesktopService
{
    VirtualDesktopWorkspaceSnapshot GetWorkspaceSnapshot();

    NavigatorPreviewSnapshot GetNavigatorPreviewSnapshot(DisplayMonitorInfo targetDisplayMonitor);

    DesktopNavigationResult SwitchDesktop(DesktopSwitchDirection desktopSwitchDirection);

    DesktopNavigationResult MoveFocusedWindowToAdjacentDesktop(DesktopSwitchDirection desktopSwitchDirection);

    DesktopNavigationResult CreateDesktopAndSwitch(DesktopSwitchDirection desktopSwitchDirection);

    DesktopNavigationResult SwitchToDesktop(string desktopIdentifier);

    bool IsDesktopEmpty(string desktopIdentifier);

    DesktopAutoDeletionValidationResult EvaluateAutoDeletion(string sourceDesktopIdentifier, string targetDesktopIdentifier);

    DesktopDeletionResult DeleteDesktop(string desktopIdentifier, string fallbackDesktopIdentifier);
}
