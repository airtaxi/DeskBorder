using DeskBorder.Models;

namespace DeskBorder.Services;

public interface IForegroundWindowFullscreenService
{
    ForegroundWindowFullscreenState GetForegroundWindowFullscreenState();

    ForegroundWindowFullscreenState GetForegroundWindowFullscreenState(DisplayMonitorInfo[] displayMonitors);

    bool ShouldDisableDesktopSwitchingAndCreation(ForegroundWindowFullscreenState foregroundWindowFullscreenState, DeskBorderSettings settings);
}
