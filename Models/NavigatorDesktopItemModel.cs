using Microsoft.UI.Xaml.Controls;

namespace DeskBorder.Models;

public sealed record NavigatorDesktopItemModel
{
    public required string DesktopIdentifier { get; init; }

    public required string DisplayName { get; init; }

    public string Description { get; init; } = string.Empty;

    public Symbol IconSymbol { get; init; } = Symbol.AllApps;
}
