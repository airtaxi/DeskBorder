namespace DeskBorder.Models;

public sealed class NavigatorDesktopSelectionRequestedEventArgs(string desktopIdentifier) : EventArgs
{
    public string DesktopIdentifier { get; } = desktopIdentifier;
}
