using Windows.System;

namespace DeskBorder.Models;

public enum MultiDisplayBehavior
{
    DisableInMultiDisplayEnvironment,
    UseOuterDisplayEdges,
}

public enum AppLanguagePreference
{
    System,
    Korean,
    English,
    Japanese,
    ChineseSimplified,
    ChineseTraditional,
}

public enum ApplicationThemePreference
{
    System,
    Light,
    Dark,
}

[Flags]
public enum KeyboardModifierKeys
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alternate = 1 << 2,
    Windows = 1 << 3,
}

public enum KeyboardShortcutTriggerType
{
    VirtualKey,
    MouseWheelUp,
    MouseWheelDown,
    MouseLeftButton,
    MouseRightButton,
}

public enum DesktopSwitchMouseLocationOption
{
    OppositeSide,
    VirtualScreenCenter,
    PrimaryMonitorCenter,
    TargetMonitorCenter,
    InputMonitorCenter,
    DoNotMove,
}

public sealed record ModifierGateSettings
{
    public KeyboardModifierKeys RequiredKeyboardModifierKeys { get; init; } = KeyboardModifierKeys.None;
}

public sealed record KeyboardShortcutSettings
{
    public bool IsEnabled { get; init; }

    public KeyboardModifierKeys RequiredKeyboardModifierKeys { get; init; } = KeyboardModifierKeys.None;

    public KeyboardShortcutTriggerType TriggerType { get; init; } = KeyboardShortcutTriggerType.VirtualKey;

    public VirtualKey Key { get; init; } = VirtualKey.None;
}

public sealed record ApplicationHotkeySettings
{
    public KeyboardShortcutSettings ToggleDeskBorderEnabledHotkey { get; init; } = new()
    {
        RequiredKeyboardModifierKeys = KeyboardModifierKeys.Control | KeyboardModifierKeys.Shift | KeyboardModifierKeys.Alternate,
        Key = VirtualKey.D
    };
}

public sealed record DesktopSwitchHotkeySettings
{
    public KeyboardShortcutSettings SwitchToPreviousDesktopHotkey { get; init; } = new()
    {
        RequiredKeyboardModifierKeys = KeyboardModifierKeys.Control | KeyboardModifierKeys.Windows,
        TriggerType = KeyboardShortcutTriggerType.MouseWheelUp
    };

    public KeyboardShortcutSettings SwitchToNextDesktopHotkey { get; init; } = new()
    {
        RequiredKeyboardModifierKeys = KeyboardModifierKeys.Control | KeyboardModifierKeys.Windows,
        TriggerType = KeyboardShortcutTriggerType.MouseWheelDown
    };
}

public sealed record DesktopSwitchMouseLocationSettings
{
    public DesktopSwitchMouseLocationOption HotkeyTriggeredMouseLocationOption { get; init; } = DesktopSwitchMouseLocationOption.DoNotMove;

    public DesktopSwitchMouseLocationOption DesktopEdgeTriggeredMouseLocationOption { get; init; } = DesktopSwitchMouseLocationOption.OppositeSide;
}

public sealed record TriggerRectangleSettings
{
    public double Left { get; init; } = 0.99;

    public double Top { get; init; } = 0.99;

    public double Width { get; init; } = 0.01;

    public double Height { get; init; } = 0.01;
}

public sealed record DesktopEdgeIgnoreZoneSettings
{
    public double TopIgnorePercentage { get; init; } = 20.0;

    public double BottomIgnorePercentage { get; init; } = 20.0;
}

public sealed record FocusedWindowMoveHotkeySettings
{
    public KeyboardShortcutSettings MoveToPreviousDesktopHotkey { get; init; } = new()
    {
        IsEnabled = true,
        RequiredKeyboardModifierKeys = KeyboardModifierKeys.Control | KeyboardModifierKeys.Alternate | KeyboardModifierKeys.Windows,
        Key = VirtualKey.Left
    };

    public KeyboardShortcutSettings MoveToNextDesktopHotkey { get; init; } = new()
    {
        IsEnabled = true,
        RequiredKeyboardModifierKeys = KeyboardModifierKeys.Control | KeyboardModifierKeys.Alternate | KeyboardModifierKeys.Windows,
        Key = VirtualKey.Right
    };
}

public sealed record NavigatorSettings
{
    public KeyboardShortcutSettings ToggleHotkey { get; init; } = new()
    {
        RequiredKeyboardModifierKeys = KeyboardModifierKeys.Control | KeyboardModifierKeys.Shift | KeyboardModifierKeys.Windows,
        Key = VirtualKey.N
    };

    public bool IsTriggerAreaEnabled { get; init; }

    public TriggerRectangleSettings TriggerRectangle { get; init; } = new();
}

public sealed record DeskBorderSettings
{
    public int SchemaVersion { get; init; } = 4;

    public bool IsDeskBorderEnabled { get; init; } = true;

    public MultiDisplayBehavior MultiDisplayBehavior { get; init; } = MultiDisplayBehavior.DisableInMultiDisplayEnvironment;

    public ModifierGateSettings SwitchDesktopModifierSettings { get; init; } = new();

    public ModifierGateSettings CreateDesktopModifierSettings { get; init; } = new();

    public ModifierGateSettings SwitchDesktopWhileMouseButtonsArePressedModifierSettings { get; init; } = new();

    public bool IsDesktopCreationEnabled { get; init; } = true;

    public bool IsDesktopCreationSkippedWhenCurrentDesktopIsEmpty { get; init; }

    public bool IsDesktopCreationCompletionToastEnabled { get; init; }

    public bool IsDesktopSwitchingAndCreationDisabledWhenForegroundWindowIsFullscreen { get; init; }

    public bool IsWindowedFullscreenIncludedWhenDisablingDesktopSwitchingAndCreation { get; init; } = true;

    public bool IsAutoDeleteEnabled { get; init; } = true;

    public bool IsAutoDeleteWarningEnabled { get; init; }

    public bool IsAutoDeleteCompletionToastEnabled { get; init; }

    public bool IsDesktopEdgeAdditionalTriggerDistanceEnabled { get; init; } = true;

    public double DesktopEdgeAdditionalTriggerDistancePercentage { get; init; } = 3.0;

    public bool IsVerticalDesktopSwitchingEnabled { get; init; }

    public bool IsVerticalDesktopSwitchDirectionReversed { get; init; }

    public bool IsVerticalDesktopSwitchingOnlyInMultiDisplayEnvironment { get; init; }

    public double AutoDeleteWarningTimeoutSeconds { get; init; } = 3.0;

    public DesktopEdgeIgnoreZoneSettings DesktopEdgeIgnoreZoneSettings { get; init; } = new();

    public ApplicationHotkeySettings ApplicationHotkeySettings { get; init; } = new();

    public DesktopSwitchHotkeySettings DesktopSwitchHotkeySettings { get; init; } = new();

    public DesktopSwitchMouseLocationSettings DesktopSwitchMouseLocationSettings { get; init; } = new();

    public FocusedWindowMoveHotkeySettings FocusedWindowMoveHotkeySettings { get; init; } = new();

    public NavigatorSettings NavigatorSettings { get; init; } = new();

    public string[] BlacklistedProcessNames { get; init; } = [];

    public string[] WhitelistedProcessNames { get; init; } = [];

    public bool IsLaunchOnStartupEnabled { get; init; } = true;

    public bool IsAlwaysRunAsAdministratorEnabled { get; init; }

    public bool IsStoreUpdateCheckEnabled { get; init; } = true;

    public bool IsWindowsOnlyModifierWarningSuppressed { get; init; }

    public AppLanguagePreference AppLanguagePreference { get; init; } = AppLanguagePreference.System;

    public ApplicationThemePreference ApplicationThemePreference { get; init; } = ApplicationThemePreference.System;

    public static DeskBorderSettings CreateDefault() => new();
}
