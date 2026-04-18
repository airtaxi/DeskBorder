using Microsoft.UI.Xaml.Controls;

namespace DeskBorder.Pages.Toast;

public abstract class ToastPageBase : Page
{
    public event EventHandler? ActionInvoked;

    protected void RaiseActionInvoked() => ActionInvoked?.Invoke(this, EventArgs.Empty);
}
