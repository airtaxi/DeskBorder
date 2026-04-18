using DeskBorder.Interop;
using DeskBorder.Helpers;
using DeskBorder.Models;
using DeskBorder.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinUIEx;

namespace DeskBorder.Views;

public sealed partial class ToastWindow : WindowEx
{
    private const int ToastMargin = 32;
    private const int ToastHeight = 160;
    private const int ToastWidth = 420;

    public ToastWindow()
    {
        InitializeComponent();
        Title = LocalizedResourceAccessor.GetString("Toast.WindowTitle");
        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
    }

    public event EventHandler? ActionButtonClicked;

    public ToastViewModel ViewModel { get; } = new();

    public void HideToast()
    {
        if (AppWindow.IsVisible)
            AppWindow.Hide();
    }

    public void SetToastContent(ToastPresentationOptions toastPresentationOptions)
    {
        ViewModel.Title = toastPresentationOptions.Title;
        ViewModel.Message = toastPresentationOptions.Message;
        ViewModel.ActionButtonText = toastPresentationOptions.ActionButtonText;
        UpdateWindowBounds();
    }

    public void ShowToast()
    {
        UpdateWindowBounds();
        if (!AppWindow.IsVisible)
            AppWindow.Show();

        Activate();
        BringToFront();
    }

    private void OnActionButtonClicked(object sender, RoutedEventArgs routedEventArgs) => ActionButtonClicked?.Invoke(this, EventArgs.Empty);

    private void UpdateWindowBounds()
    {
        var windowHandle = this.GetWindowHandle();
        var toastMargin = ScaleToWindowPixels(windowHandle, ToastMargin);
        var toastHeight = ScaleToWindowPixels(windowHandle, ToastHeight);
        var toastWidth = ScaleToWindowPixels(windowHandle, ToastWidth);
        var displayMonitors = MouseHelper.GetDisplayMonitors();
        var targetDisplayMonitor = displayMonitors.FirstOrDefault(displayMonitor => displayMonitor.IsPrimaryDisplay) ?? displayMonitors.FirstOrDefault() ?? throw new InvalidOperationException("No display monitor is available for the toast window.");
        var workAreaBounds = targetDisplayMonitor.WorkAreaBounds;
        AppWindow.MoveAndResize(new RectInt32(workAreaBounds.Right - toastWidth - toastMargin, workAreaBounds.Bottom - toastHeight - toastMargin, toastWidth, toastHeight));
    }

    private static int ScaleToWindowPixels(nint windowHandle, int logicalPixels) => (int)Math.Round(logicalPixels * Win32.GetDpiForWindow(windowHandle) / 96d, MidpointRounding.AwayFromZero);
}
