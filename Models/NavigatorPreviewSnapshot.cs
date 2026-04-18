namespace DeskBorder.Models;

public sealed record NavigatorDesktopWindowItemModel
{
    public required ScreenRectangle PreviewBounds { get; init; }

    public nint WindowHandle { get; init; }

    public string? ExecutablePath { get; init; }
}

public sealed record NavigatorPreviewSnapshot
{
    public required DisplayMonitorInfo TargetDisplayMonitor { get; init; }

    public NavigatorDesktopItemModel[] DesktopItems { get; init; } = [];
}
