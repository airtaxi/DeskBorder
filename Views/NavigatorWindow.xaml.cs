using DeskBorder.Interop;
using DeskBorder.Services;
using DeskBorder.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using WinUIEx;

namespace DeskBorder.Views;

public sealed partial class NavigatorWindow : WindowEx
{
    private const int NavigatorWindowHeight = 640;
    private const int NavigatorWindowWidth = 460;
    private readonly ILocalizationService _localizationService;
    private readonly INavigatorService _navigatorService;
    public NavigatorViewModel ViewModel => _navigatorService.ViewModel;

    public NavigatorWindow(INavigatorService navigatorService, ILocalizationService localizationService)
    {
        _navigatorService = navigatorService;
        _localizationService = localizationService;
        InitializeOverlayWindow();
    }

    public void ShowOverlay()
    {
        if (!AppWindow.IsVisible)
            AppWindow.Show();

        Activate();
        BringToFront();
        _ = RootGrid.Focus(FocusState.Programmatic);
        _ = DesktopListView.Focus(FocusState.Programmatic);
        SelectCurrentDesktopItem();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs windowActivatedEventArgs)
    {
        if (windowActivatedEventArgs.WindowActivationState == WindowActivationState.Deactivated)
            _navigatorService.NotifyWindowDeactivated();
        else
            _navigatorService.NotifyWindowActivated();
    }

    private void OnClosed(object sender, WindowEventArgs windowEventArgs)
    {
        _localizationService.LanguageChanged -= OnLocalizationServiceLanguageChanged;
        Activated -= OnActivated;
        Closed -= OnClosed;
    }

    private void OnDesktopListViewItemClick(object sender, ItemClickEventArgs itemClickEventArgs)
    {
        if (itemClickEventArgs.ClickedItem is not NavigatorDesktopItemViewModel navigatorDesktopItemViewModel)
            return;

        _navigatorService.RequestDesktopSelection(navigatorDesktopItemViewModel.DesktopIdentifier);
    }

    private void OnEscapeKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs keyboardAcceleratorInvokedEventArgs)
    {
        keyboardAcceleratorInvokedEventArgs.Handled = true;
        _navigatorService.CloseFromKeyboard();
    }

    private void SelectCurrentDesktopItem()
    {
        var currentDesktopItem = _navigatorService.ViewModel.DesktopItems.FirstOrDefault(desktopItem => desktopItem.IsCurrentDesktop) ?? _navigatorService.ViewModel.DesktopItems.FirstOrDefault();
        if (currentDesktopItem is null)
            return;

        DesktopListView.ScrollIntoView(currentDesktopItem);
    }

    private void InitializeOverlayWindow()
    {
        InitializeComponent();
        _localizationService.LanguageChanged += OnLocalizationServiceLanguageChanged;
        Activated += OnActivated;
        Closed += OnClosed;
        _navigatorService.Initialize(this);
        RefreshLocalizedText();

        var windowHandle = this.GetWindowHandle();
        AppWindow.Resize(new SizeInt32(
            ScaleToWindowPixels(windowHandle, NavigatorWindowWidth),
            ScaleToWindowPixels(windowHandle, NavigatorWindowHeight)));
        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        AppWindow.SetIcon("Assets/Icon.ico");
    }

    private void OnLocalizationServiceLanguageChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (DispatcherQueue.TryEnqueue(RefreshLocalizedText))
            return;

        RefreshLocalizedText();
    }

    private void RefreshLocalizedText()
    {
        Title = _localizationService.GetString("Navigator.WindowTitle");
        NavigatorTitleTextBlock.Text = _localizationService.GetString("Navigator.Title");
        TriggerAreaTitleTextBlock.Text = _localizationService.GetString("Navigator.TriggerAreaTitle");
    }

    private static int ScaleToWindowPixels(nint windowHandle, int logicalPixels) => (int)Math.Round(logicalPixels * Win32.GetDpiForWindow(windowHandle) / 96d, MidpointRounding.AwayFromZero);
}
