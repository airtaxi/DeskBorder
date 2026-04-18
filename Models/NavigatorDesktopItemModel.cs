namespace DeskBorder.Models;

public sealed record NavigatorDesktopItemModel
{
    public required string DesktopIdentifier { get; init; }

    public required string DisplayName { get; init; }

    public bool IsCurrentDesktop { get; init; }

    public NavigatorDesktopWindowItemModel[] WindowItems { get; init; } = [];
}
