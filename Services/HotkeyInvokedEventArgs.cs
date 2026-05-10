namespace DeskBorder.Services;

public enum HotkeyActionType
{
    ToggleDeskBorderEnabled,
    SwitchToPreviousDesktop,
    SwitchToNextDesktop,
    MoveFocusedWindowToPreviousDesktop,
    MoveFocusedWindowToNextDesktop,
    ToggleNavigator,
    ShowProcessDesktopPlacementQuickConfiguration,
}

public sealed class HotkeyInvokedEventArgs(HotkeyActionType hotkeyActionType) : EventArgs
{
    public HotkeyActionType HotkeyActionType { get; } = hotkeyActionType;
}
