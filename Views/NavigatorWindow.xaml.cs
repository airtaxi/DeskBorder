using DeskBorder.Interop;
using DeskBorder.Models;
using DeskBorder.Services;
using DeskBorder.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinUIEx;

namespace DeskBorder.Views;

public sealed partial class NavigatorWindow : WindowEx
{
    private const int NavigatorWindowHeight = 170;
    private readonly ILocalizationService _localizationService;
    private readonly INavigatorService _navigatorService;
    private readonly IThemeService _themeService;
    public NavigatorViewModel ViewModel => _navigatorService.ViewModel;

    public NavigatorWindow(INavigatorService navigatorService, ILocalizationService localizationService, IThemeService themeService)
    {
        _navigatorService = navigatorService;
        _localizationService = localizationService;
        _themeService = themeService;
        InitializeOverlayWindow();
    }

    public void ShowOverlay(DisplayMonitorInfo targetDisplayMonitor)
    {
        UpdateWindowBounds(targetDisplayMonitor);
        if (!AppWindow.IsVisible)
            AppWindow.Show();

        BringToFront();
    }

    private void OnNavigatorWindowClosed(object sender, WindowEventArgs windowEventArgs)
    {
        _ = windowEventArgs;
        _localizationService.LanguageChanged -= OnLocalizationServiceLanguageChanged;
        Closed -= OnNavigatorWindowClosed;
    }

    private void InitializeOverlayWindow()
    {
        InitializeComponent();
        RegisterCurrentWindowContentWithThemeService();
        _localizationService.LanguageChanged += OnLocalizationServiceLanguageChanged;
        Closed += OnNavigatorWindowClosed;
        _navigatorService.Initialize(this);
        RefreshLocalizedText();
        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        AppWindow.SetIcon("Assets/Icon.ico");
    }

    private void RegisterCurrentWindowContentWithThemeService()
    {
        if (Content is not FrameworkElement rootFrameworkElement)
            throw new InvalidOperationException("Unable to resolve the navigator window root content for theme application.");

        _themeService.RegisterFrameworkElement(rootFrameworkElement);
    }

    private void OnLocalizationServiceLanguageChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (DispatcherQueue.TryEnqueue(RefreshLocalizedText))
            return;

        RefreshLocalizedText();
    }

    private void RefreshLocalizedText() => Title = _localizationService.GetString("Navigator.WindowTitle");

    private void UpdateWindowBounds(DisplayMonitorInfo targetDisplayMonitor)
    {
        var windowHandle = this.GetWindowHandle();
        var navigatorWindowHeight = ScaleToWindowPixels(windowHandle, NavigatorWindowHeight);
        AppWindow.MoveAndResize(new RectInt32(
            targetDisplayMonitor.WorkAreaBounds.Left,
            targetDisplayMonitor.WorkAreaBounds.Bottom - navigatorWindowHeight,
            targetDisplayMonitor.WorkAreaBounds.Width,
            navigatorWindowHeight));
    }

    private static int ScaleToWindowPixels(nint windowHandle, int logicalPixels) => (int)Math.Round(logicalPixels * Win32.GetDpiForWindow(windowHandle) / 96d, MidpointRounding.AwayFromZero);
}
