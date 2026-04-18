using DeskBorder.Interop;
using DeskBorder.Helpers;
using DeskBorder.Pages.Toast;
using DeskBorder.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinUIEx;

namespace DeskBorder.Views;

public sealed partial class ToastWindow : WindowEx
{
    private const int ToastMargin = 32;
    private ToastPageBase? _toastPage;
    public int ToastWidth { get; set; } = 420;
    public int ToastHeight { get; set; } = 160;

    public ToastWindow(IThemeService themeService)
    {
        InitializeComponent();
        RegisterCurrentWindowContentWithThemeService(themeService);
        Title = LocalizedResourceAccessor.GetString("Toast.WindowTitle");
        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
    }

    public void HideToast()
    {
        if (AppWindow.IsVisible)
            AppWindow.Hide();
    }

    public void ClearToastPage()
    {
        _toastPage = null;
        ToastContentPresenter.Content = null;
    }

    public void SetToastPage(ToastPageBase toastPage, int width, int height)
    {
        _toastPage = toastPage;
        ToastContentPresenter.Content = toastPage;
        ToastWidth = width;
        ToastHeight = height;
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

    private void RegisterCurrentWindowContentWithThemeService(IThemeService themeService)
    {
        if (Content is not FrameworkElement rootFrameworkElement)
            throw new InvalidOperationException("Unable to resolve the toast window root content for theme application.");

        themeService.RegisterFrameworkElement(rootFrameworkElement);
    }

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
