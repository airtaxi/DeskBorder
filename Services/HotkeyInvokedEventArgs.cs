namespace DeskBorder.Services;

public enum HotkeyActionType
{
    ToggleDeskBorderEnabled,
    MoveFocusedWindowToPreviousDesktop,
    MoveFocusedWindowToNextDesktop,
    ToggleNavigator,
}

public sealed class HotkeyInvokedEventArgs(HotkeyActionType hotkeyActionType) : EventArgs
{
    public HotkeyActionType HotkeyActionType { get; } = hotkeyActionType;
}
