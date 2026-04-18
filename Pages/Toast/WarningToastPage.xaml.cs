using DeskBorder.Models;
using Microsoft.UI.Xaml;

namespace DeskBorder.Pages.Toast;

public sealed partial class WarningToastPage : ToastPageBase
{
    public WarningToastPage(WarningToastPresentationOptions warningToastPresentationOptions)
    {
        PresentationOptions = warningToastPresentationOptions;
        InitializeComponent();
    }

    public WarningToastPresentationOptions PresentationOptions { get; }

    private Visibility GetActionCardTitleVisibility(string actionCardTitle) => string.IsNullOrWhiteSpace(actionCardTitle) ? Visibility.Collapsed : Visibility.Visible;

    private void OnActionButtonClicked(object sender, RoutedEventArgs routedEventArgs) => RaiseActionInvoked();
}
