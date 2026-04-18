namespace DeskBorder.Models;

public enum DesktopEdgeKind
{
    None,
    LeftOuterDisplayEdge,
    RightOuterDisplayEdge,
}

public enum DesktopEdgeAvailabilityStatus
{
    Enabled,
    DisabledByDeskBorderSetting,
    DisabledByCursorClipping,
    DisabledInMultiDisplayEnvironment,
    CursorOutsideDisplayEnvironment,
}

public readonly record struct ScreenPoint(int X, int Y);

public readonly record struct ScreenRectangle(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Left >= Right || Top >= Bottom;

    public bool Contains(ScreenPoint screenPoint) => Contains(screenPoint.X, screenPoint.Y);

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

public sealed record CursorClippingState
{
    public bool IsCursorClipped { get; init; }

    public ScreenRectangle ClippingRectangle { get; init; }

    public ScreenRectangle VirtualScreenBounds { get; init; }
}

public sealed record ModifierKeySnapshot
{
    public KeyboardModifierKeys PressedKeyboardModifierKeys { get; init; } = KeyboardModifierKeys.None;

    public bool IsShiftPressed { get; init; }

    public bool IsControlPressed { get; init; }

    public bool IsAlternatePressed { get; init; }

    public bool IsWindowsPressed { get; init; }
}

public sealed record DisplayMonitorInfo
{
    public nint MonitorHandle { get; init; }

    public string DeviceName { get; init; } = string.Empty;

    public ScreenRectangle MonitorBounds { get; init; }

    public ScreenRectangle WorkAreaBounds { get; init; }

    public bool IsPrimaryDisplay { get; init; }
}

public sealed record NavigatorTriggerState
{
    public bool IsEnabled { get; init; }

    public ScreenRectangle? TriggerRectangle { get; init; }

    public bool IsCursorInsideTriggerRectangle { get; init; }

    public bool HasCursorEnteredTriggerRectangle { get; init; }

    public bool HasCursorLeftTriggerRectangle { get; init; }
}

public sealed record DesktopEdgeMonitoringState
{
    public ScreenPoint CursorPosition { get; init; }

    public CursorClippingState CursorClippingState { get; init; } = new();

    public ModifierKeySnapshot ModifierKeySnapshot { get; init; } = new();

    public DisplayMonitorInfo[] DisplayMonitors { get; init; } = [];

    public DisplayMonitorInfo? CurrentDisplayMonitor { get; init; }

    public DesktopEdgeAvailabilityStatus DesktopEdgeAvailabilityStatus { get; init; } = DesktopEdgeAvailabilityStatus.Enabled;

    public DesktopEdgeKind ActiveDesktopEdge { get; init; } = DesktopEdgeKind.None;

    public bool HasCursorEnteredDesktopEdge { get; init; }

    public bool HasCursorLeftDesktopEdge { get; init; }

    public bool IsSwitchDesktopModifierSatisfied { get; init; }

    public bool IsCreateDesktopModifierSatisfied { get; init; }

    public NavigatorTriggerState NavigatorTriggerState { get; init; } = new();

    public bool IsMultiDisplayEnvironment => DisplayMonitors.Length > 1;

    public bool IsDesktopEdgeAvailable => DesktopEdgeAvailabilityStatus == DesktopEdgeAvailabilityStatus.Enabled;
}
