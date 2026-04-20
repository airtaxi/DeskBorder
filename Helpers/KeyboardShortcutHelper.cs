using DeskBorder.Models;
using Windows.System;

namespace DeskBorder.Helpers;

public static class KeyboardShortcutHelper
{
    private const KeyboardModifierKeys WindowsDesktopSwitchKeyboardModifierKeys = KeyboardModifierKeys.Control | KeyboardModifierKeys.Windows;

    public static (KeyboardModifierKeys RequiredKeyboardModifierKeys, KeyboardShortcutTriggerType TriggerType, VirtualKey Key) CreateKeyboardShortcutIdentity(KeyboardShortcutSettings keyboardShortcutSettings) => (
        keyboardShortcutSettings.RequiredKeyboardModifierKeys,
        keyboardShortcutSettings.TriggerType,
        keyboardShortcutSettings.TriggerType == KeyboardShortcutTriggerType.VirtualKey ? keyboardShortcutSettings.Key : VirtualKey.None);

    public static bool IsKeyboardShortcutSpecified(KeyboardShortcutSettings keyboardShortcutSettings) => keyboardShortcutSettings.TriggerType != KeyboardShortcutTriggerType.VirtualKey
        || keyboardShortcutSettings.Key != VirtualKey.None;

    public static bool IsReservedByWindowsDesktopSwitchHotkey(KeyboardShortcutSettings keyboardShortcutSettings) => keyboardShortcutSettings.TriggerType == KeyboardShortcutTriggerType.VirtualKey
        && keyboardShortcutSettings.RequiredKeyboardModifierKeys == WindowsDesktopSwitchKeyboardModifierKeys
        && (keyboardShortcutSettings.Key == VirtualKey.Left || keyboardShortcutSettings.Key == VirtualKey.Right);
}
