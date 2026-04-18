using DeskBorder.Services;
using Microsoft.UI.Xaml.Controls;

namespace DeskBorder.Dialogs;

public sealed partial class ForegroundProcessSelectionDialog : ContentDialog
{
    public List<string> AvailableProcessNames { get; }

    public IReadOnlyList<string> SelectedProcessNames => [.. AvailableProcessNamesListView.SelectedItems.Cast<string>()];

    public ForegroundProcessSelectionDialog(IEnumerable<string> availableProcessNames, IThemeService themeService)
    {
        AvailableProcessNames = [.. availableProcessNames];
        InitializeComponent();
        themeService.RegisterFrameworkElement(this);
        IsPrimaryButtonEnabled = false;
    }

    private void OnAvailableProcessNamesListViewSelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs) => IsPrimaryButtonEnabled = AvailableProcessNamesListView.SelectedItems.Count > 0;
}
