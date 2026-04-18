using Windows.System;

namespace DeskBorder.Models;

public enum MultiDisplayBehavior
{
    DisableInMultiDisplayEnvironment,
    UseOuterDisplayEdges,
}

public enum EmptyDesktopDetectionMode
{
    IgnoreNotificationAreaApplications,
    CountAllTopLevelWindows,
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

[Flags]
public enum KeyboardModifierKeys
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alternate = 1 << 2,
    Windows = 1 << 3,
}

public sealed record ModifierGateSettings
{
    public KeyboardModifierKeys RequiredKeyboardModifierKeys { get; init; } = KeyboardModifierKeys.None;
}

public sealed record KeyboardShortcutSettings
{
    public bool IsEnabled { get; init; }

    public KeyboardModifierKeys RequiredKeyboardModifierKeys { get; init; } = KeyboardModifierKeys.None;

    public VirtualKey Key { get; init; } = VirtualKey.None;
}

public sealed record ApplicationHotkeySettings
{
    public KeyboardShortcutSettings ToggleDeskBorderEnabledHotkey { get; init; } = new();
}

public sealed record TriggerRectangleSettings
{
    public double Left { get; init; } = 0.45;

    public double Top { get; init; }

    public double Width { get; init; } = 0.10;

    public double Height { get; init; } = 0.02;
}

public sealed record DesktopEdgeIgnoreZoneSettings
{
    public double TopIgnorePercentage { get; init; }

    public double BottomIgnorePercentage { get; init; }
}

public sealed record FocusedWindowMoveHotkeySettings
{
    public KeyboardShortcutSettings MoveToPreviousDesktopHotkey { get; init; } = new();

    public KeyboardShortcutSettings MoveToNextDesktopHotkey { get; init; } = new();
}

public sealed record NavigatorSettings
{
    public KeyboardShortcutSettings ToggleHotkey { get; init; } = new();

    public bool IsTriggerAreaEnabled { get; init; }

    public TriggerRectangleSettings TriggerRectangle { get; init; } = new();
}

public sealed record DeskBorderSettings
{
    public int SchemaVersion { get; init; } = 1;

    public bool IsDeskBorderEnabled { get; init; } = true;

    public MultiDisplayBehavior MultiDisplayBehavior { get; init; } = MultiDisplayBehavior.DisableInMultiDisplayEnvironment;

    public ModifierGateSettings SwitchDesktopModifierSettings { get; init; } = new();

    public ModifierGateSettings CreateDesktopModifierSettings { get; init; } = new()
    {
        RequiredKeyboardModifierKeys = KeyboardModifierKeys.Shift
    };

    public bool IsDesktopCreationEnabled { get; init; } = true;

    public bool IsAutoDeleteEnabled { get; init; }

    public bool IsAutoDeleteWarningEnabled { get; init; } = true;

    public EmptyDesktopDetectionMode EmptyDesktopDetectionMode { get; init; } = EmptyDesktopDetectionMode.IgnoreNotificationAreaApplications;

    public DesktopEdgeIgnoreZoneSettings DesktopEdgeIgnoreZoneSettings { get; init; } = new();

    public ApplicationHotkeySettings ApplicationHotkeySettings { get; init; } = new();

    public FocusedWindowMoveHotkeySettings FocusedWindowMoveHotkeySettings { get; init; } = new();

    public NavigatorSettings NavigatorSettings { get; init; } = new();

    public string[] BlacklistedProcessNames { get; init; } = [];

    public bool IsLaunchOnStartupEnabled { get; init; }

    public bool IsStoreUpdateCheckEnabled { get; init; } = true;

    public AppLanguagePreference AppLanguagePreference { get; init; } = AppLanguagePreference.System;

    public static DeskBorderSettings CreateDefault() => new();
}
