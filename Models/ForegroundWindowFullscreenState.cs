namespace DeskBorder.Models;

public enum ForegroundWindowFullscreenKind
{
    None,
    Fullscreen,
    WindowedFullscreen,
}

public sealed record ForegroundWindowFullscreenState
{
    public nint WindowHandle { get; init; }

    public ForegroundWindowFullscreenKind FullscreenKind { get; init; } = ForegroundWindowFullscreenKind.None;

    public ScreenRectangle WindowBounds { get; init; }

    public ScreenRectangle DisplayMonitorBounds { get; init; }

    public bool IsFullscreen => FullscreenKind != ForegroundWindowFullscreenKind.None;
}
