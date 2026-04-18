namespace DeskBorder.Models;

public enum ToastPresentationResultKind
{
    TimedOut,
    ActionInvoked,
    Replaced,
    Dismissed,
}

public sealed record ToastPresentationOptions
{
    public required string Title { get; init; }

    public required string Message { get; init; }

    public string ActionCardTitle { get; init; } = string.Empty;

    public required string ActionButtonText { get; init; }

    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(3);
}

public sealed record ToastPresentationResult
{
    public ToastPresentationResultKind ResultKind { get; init; } = ToastPresentationResultKind.Dismissed;

    public bool WasActionInvoked => ResultKind == ToastPresentationResultKind.ActionInvoked;
}
