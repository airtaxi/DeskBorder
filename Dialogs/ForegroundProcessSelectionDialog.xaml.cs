using Microsoft.UI.Xaml.Controls;

namespace DeskBorder.Dialogs;

public sealed partial class ForegroundProcessSelectionDialog : ContentDialog
{
    public List<string> AvailableProcessNames { get; }

    public IReadOnlyList<string> SelectedProcessNames => [.. AvailableProcessNamesListView.SelectedItems.Cast<string>()];

    public ForegroundProcessSelectionDialog(IEnumerable<string> availableProcessNames)
    {
        AvailableProcessNames = [.. availableProcessNames];
        InitializeComponent();
        IsPrimaryButtonEnabled = false;
    }

    private void OnAvailableProcessNamesListViewSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        _ = sender;
        _ = selectionChangedEventArgs;
        IsPrimaryButtonEnabled = AvailableProcessNamesListView.SelectedItems.Count > 0;
    }
}
