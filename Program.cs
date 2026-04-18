using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Threading;

namespace DeskBorder;

public static class Program
{
    private const string SingleInstanceKey = "DeskBorder_SingleInstance";

    [STAThread]
    public static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (TryRedirectToExistingInstance())
            return;

        Application.Start(applicationInitializationCallbackParameters =>
        {
            _ = applicationInitializationCallbackParameters;
            var dispatcherQueueSynchronizationContext = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(dispatcherQueueSynchronizationContext);
            _ = new App();
        });
    }

    private static bool TryRedirectToExistingInstance()
    {
        var mainInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);
        if (mainInstance.IsCurrent)
        {
            mainInstance.Activated += OnMainInstanceActivated;
            return false;
        }

        var appActivationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
        mainInstance.RedirectActivationToAsync(appActivationArguments).AsTask().GetAwaiter().GetResult();
        return true;
    }

    private static void OnMainInstanceActivated(object? sender, AppActivationArguments appActivationArguments) => App.HandleRedirectedActivation(appActivationArguments);
}
