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

#pragma warning disable CA1822 // Mark members as static => Used in XAML binding, cannot be static
    private Visibility GetActionCardTitleVisibility(string actionCardTitle) => string.IsNullOrWhiteSpace(actionCardTitle) ? Visibility.Collapsed : Visibility.Visible;
#pragma warning restore CA1822 // Mark members as static => Used in XAML binding, cannot be static

    private void OnActionButtonClicked(object sender, RoutedEventArgs routedEventArgs) => RaiseActionInvoked();
}
