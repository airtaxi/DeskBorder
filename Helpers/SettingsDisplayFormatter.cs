using DeskBorder.Models;
using System.Globalization;
using Windows.System;

namespace DeskBorder.Helpers;

public static class SettingsDisplayFormatter
{
    public static string FormatAppLanguagePreference(AppLanguagePreference appLanguagePreference) => appLanguagePreference switch
    {
        AppLanguagePreference.System => LocalizedResourceAccessor.GetString("Settings.AppLanguage.System"),
        AppLanguagePreference.Korean => LocalizedResourceAccessor.GetString("Settings.AppLanguage.Korean"),
        AppLanguagePreference.English => LocalizedResourceAccessor.GetString("Settings.AppLanguage.English"),
        AppLanguagePreference.Japanese => LocalizedResourceAccessor.GetString("Settings.AppLanguage.Japanese"),
        AppLanguagePreference.ChineseSimplified => LocalizedResourceAccessor.GetString("Settings.AppLanguage.ChineseSimplified"),
        AppLanguagePreference.ChineseTraditional => LocalizedResourceAccessor.GetString("Settings.AppLanguage.ChineseTraditional"),
        _ => appLanguagePreference.ToString()
    };

    public static string FormatApplicationThemePreference(ApplicationThemePreference applicationThemePreference) => applicationThemePreference switch
    {
        ApplicationThemePreference.System => LocalizedResourceAccessor.GetString("Settings.ApplicationTheme.System"),
        ApplicationThemePreference.Light => LocalizedResourceAccessor.GetString("Settings.ApplicationTheme.Light"),
        ApplicationThemePreference.Dark => LocalizedResourceAccessor.GetString("Settings.ApplicationTheme.Dark"),
        _ => applicationThemePreference.ToString()
    };

    public static string FormatDesktopEdgeAvailabilityStatus(DesktopEdgeAvailabilityStatus desktopEdgeAvailabilityStatus) => desktopEdgeAvailabilityStatus switch
    {
        DesktopEdgeAvailabilityStatus.Enabled => LocalizedResourceAccessor.GetString("DesktopEdgeAvailability.Enabled"),
        DesktopEdgeAvailabilityStatus.DisabledByDeskBorderSetting => LocalizedResourceAccessor.GetString("DesktopEdgeAvailability.DisabledByDeskBorderSetting"),
        DesktopEdgeAvailabilityStatus.DisabledByCursorClipping => LocalizedResourceAccessor.GetString("DesktopEdgeAvailability.DisabledByCursorClipping"),
        DesktopEdgeAvailabilityStatus.DisabledInMultiDisplayEnvironment => LocalizedResourceAccessor.GetString("DesktopEdgeAvailability.DisabledInMultiDisplayEnvironment"),
        DesktopEdgeAvailabilityStatus.CursorOutsideDisplayEnvironment => LocalizedResourceAccessor.GetString("DesktopEdgeAvailability.CursorOutsideDisplayEnvironment"),
        DesktopEdgeAvailabilityStatus.DisabledByBlacklistedProcess => LocalizedResourceAccessor.GetString("DesktopEdgeAvailability.DisabledByBlacklistedProcess"),
        DesktopEdgeAvailabilityStatus.DisabledByPressedMouseButton => LocalizedResourceAccessor.GetString("DesktopEdgeAvailability.DisabledByPressedMouseButton"),
        _ => desktopEdgeAvailabilityStatus.ToString()
    };

    public static string FormatDesktopEdgeKind(DesktopEdgeKind desktopEdgeKind) => desktopEdgeKind switch
    {
        DesktopEdgeKind.None => LocalizedResourceAccessor.GetString("DesktopEdgeKind.None"),
        DesktopEdgeKind.LeftOuterDisplayEdge => LocalizedResourceAccessor.GetString("DesktopEdgeKind.LeftOuterDisplayEdge"),
        DesktopEdgeKind.RightOuterDisplayEdge => LocalizedResourceAccessor.GetString("DesktopEdgeKind.RightOuterDisplayEdge"),
        _ => desktopEdgeKind.ToString()
    };

    public static string FormatDesktopSwitchMouseLocationOption(DesktopSwitchMouseLocationOption desktopSwitchMouseLocationOption) => desktopSwitchMouseLocationOption switch
    {
        DesktopSwitchMouseLocationOption.OppositeSide => LocalizedResourceAccessor.GetString("DesktopSwitchMouseLocationOption.OppositeSide"),
        DesktopSwitchMouseLocationOption.VirtualScreenCenter => LocalizedResourceAccessor.GetString("DesktopSwitchMouseLocationOption.VirtualScreenCenter"),
        DesktopSwitchMouseLocationOption.PrimaryMonitorCenter => LocalizedResourceAccessor.GetString("DesktopSwitchMouseLocationOption.PrimaryMonitorCenter"),
        DesktopSwitchMouseLocationOption.TargetMonitorCenter => LocalizedResourceAccessor.GetString("DesktopSwitchMouseLocationOption.TargetMonitorCenter"),
        DesktopSwitchMouseLocationOption.InputMonitorCenter => LocalizedResourceAccessor.GetString("DesktopSwitchMouseLocationOption.InputMonitorCenter"),
        DesktopSwitchMouseLocationOption.DoNotMove => LocalizedResourceAccessor.GetString("DesktopSwitchMouseLocationOption.DoNotMove"),
        _ => desktopSwitchMouseLocationOption.ToString()
    };

    public static string FormatKeyboardModifierKeys(KeyboardModifierKeys keyboardModifierKeys)
    {
        if (keyboardModifierKeys == KeyboardModifierKeys.None)
            return LocalizedResourceAccessor.GetString("Common.None");

        var keyboardModifierNames = new List<string>(4);
        if (keyboardModifierKeys.HasFlag(KeyboardModifierKeys.Control))
            keyboardModifierNames.Add(LocalizedResourceAccessor.GetString("KeyboardModifier.Control"));

        if (keyboardModifierKeys.HasFlag(KeyboardModifierKeys.Shift))
            keyboardModifierNames.Add(LocalizedResourceAccessor.GetString("KeyboardModifier.Shift"));

        if (keyboardModifierKeys.HasFlag(KeyboardModifierKeys.Alternate))
            keyboardModifierNames.Add(LocalizedResourceAccessor.GetString("KeyboardModifier.Alt"));

        if (keyboardModifierKeys.HasFlag(KeyboardModifierKeys.Windows))
            keyboardModifierNames.Add(LocalizedResourceAccessor.GetString("KeyboardModifier.Windows"));

        return string.Join(" + ", keyboardModifierNames);
    }

    public static string FormatKeyboardShortcut(KeyboardShortcutSettings keyboardShortcutSettings)
    {
        if (!keyboardShortcutSettings.IsEnabled)
            return LocalizedResourceAccessor.GetString("Common.Disabled");

        if (!KeyboardShortcutHelper.IsKeyboardShortcutSpecified(keyboardShortcutSettings))
            return LocalizedResourceAccessor.GetString("KeyboardShortcut.KeyNotSpecified");

        var keyNames = new List<string>(5);
        if (keyboardShortcutSettings.RequiredKeyboardModifierKeys != KeyboardModifierKeys.None)
            keyNames.Add(FormatKeyboardModifierKeys(keyboardShortcutSettings.RequiredKeyboardModifierKeys));

        keyNames.Add(FormatKeyboardShortcutTrigger(keyboardShortcutSettings.TriggerType, keyboardShortcutSettings.Key));
        return string.Join(" + ", keyNames);
    }

    public static string FormatKeyboardShortcutTrigger(KeyboardShortcutTriggerType keyboardShortcutTriggerType, VirtualKey virtualKey) => keyboardShortcutTriggerType switch
    {
        KeyboardShortcutTriggerType.VirtualKey => FormatVirtualKey(virtualKey),
        KeyboardShortcutTriggerType.MouseWheelUp => LocalizedResourceAccessor.GetString("KeyboardShortcutTrigger.MouseWheelUp"),
        KeyboardShortcutTriggerType.MouseWheelDown => LocalizedResourceAccessor.GetString("KeyboardShortcutTrigger.MouseWheelDown"),
        KeyboardShortcutTriggerType.MouseLeftButton => LocalizedResourceAccessor.GetString("KeyboardShortcutTrigger.MouseLeftButton"),
        KeyboardShortcutTriggerType.MouseRightButton => LocalizedResourceAccessor.GetString("KeyboardShortcutTrigger.MouseRightButton"),
        _ => keyboardShortcutTriggerType.ToString()
    };

    public static string FormatMultiDisplayBehavior(MultiDisplayBehavior multiDisplayBehavior) => multiDisplayBehavior switch
    {
        MultiDisplayBehavior.DisableInMultiDisplayEnvironment => LocalizedResourceAccessor.GetString("MultiDisplayBehavior.DisableInMultiDisplayEnvironment"),
        MultiDisplayBehavior.UseOuterDisplayEdges => LocalizedResourceAccessor.GetString("MultiDisplayBehavior.UseOuterDisplayEdges"),
        _ => multiDisplayBehavior.ToString()
    };

    public static string FormatTriggerRectangle(TriggerRectangleSettings triggerRectangleSettings) => string.Format(
        CultureInfo.CurrentCulture,
        LocalizedResourceAccessor.GetString("TriggerRectangle.Format"),
        triggerRectangleSettings.Left,
        triggerRectangleSettings.Top,
        triggerRectangleSettings.Width,
        triggerRectangleSettings.Height);

    public static string FormatDesktopDisplayName(int desktopNumber) => LocalizedResourceAccessor.GetFormattedString("Desktop.DisplayNameFormat", desktopNumber);

    public static string FormatVirtualKey(VirtualKey virtualKey) => virtualKey switch
    {
        VirtualKey.None => LocalizedResourceAccessor.GetString("Common.None"),
        VirtualKey.Number0 => "0",
        VirtualKey.Number1 => "1",
        VirtualKey.Number2 => "2",
        VirtualKey.Number3 => "3",
        VirtualKey.Number4 => "4",
        VirtualKey.Number5 => "5",
        VirtualKey.Number6 => "6",
        VirtualKey.Number7 => "7",
        VirtualKey.Number8 => "8",
        VirtualKey.Number9 => "9",
        VirtualKey.Space => LocalizedResourceAccessor.GetString("VirtualKey.Space"),
        VirtualKey.Tab => LocalizedResourceAccessor.GetString("VirtualKey.Tab"),
        VirtualKey.Escape => LocalizedResourceAccessor.GetString("VirtualKey.Escape"),
        VirtualKey.Insert => LocalizedResourceAccessor.GetString("VirtualKey.Insert"),
        VirtualKey.Delete => LocalizedResourceAccessor.GetString("VirtualKey.Delete"),
        VirtualKey.Home => LocalizedResourceAccessor.GetString("VirtualKey.Home"),
        VirtualKey.End => LocalizedResourceAccessor.GetString("VirtualKey.End"),
        VirtualKey.Left => LocalizedResourceAccessor.GetString("VirtualKey.Left"),
        VirtualKey.Right => LocalizedResourceAccessor.GetString("VirtualKey.Right"),
        VirtualKey.Up => LocalizedResourceAccessor.GetString("VirtualKey.Up"),
        VirtualKey.Down => LocalizedResourceAccessor.GetString("VirtualKey.Down"),
        VirtualKey.PageUp => LocalizedResourceAccessor.GetString("VirtualKey.PageUp"),
        VirtualKey.PageDown => LocalizedResourceAccessor.GetString("VirtualKey.PageDown"),
        VirtualKey.CapitalLock => LocalizedResourceAccessor.GetString("VirtualKey.CapitalLock"),
        _ => virtualKey.ToString()
    };
}
