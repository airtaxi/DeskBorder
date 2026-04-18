namespace DeskBorder.Models;

public enum ToastPresentationResultKind
{
    TimedOut,
    ActionInvoked,
    Replaced,
    Dismissed,
}

public abstract record ToastPresentationOptions
{
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(3);
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
}

public sealed record WarningToastPresentationOptions : ToastPresentationOptions
{
    public string? Title { get; init; }

    public string? Message { get; init; }

    public string? ActionCardTitle { get; init; } = string.Empty;

    public string? ActionButtonText { get; init; }
}

public sealed record HotkeyToastPresentationOptions : ToastPresentationOptions
{
    public string? Title { get; init; }

    public string? Message { get; init; }
}

public sealed record ToastPresentationResult
{
    public ToastPresentationResultKind ResultKind { get; init; } = ToastPresentationResultKind.Dismissed;

    public bool WasActionInvoked => ResultKind == ToastPresentationResultKind.ActionInvoked;
}
