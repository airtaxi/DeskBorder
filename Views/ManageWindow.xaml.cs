using DeskBorder.Navigation;
using DeskBorder.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Runtime.InteropServices;
using WinUIEx;
using TitleBar = Microsoft.UI.Xaml.Controls.TitleBar;

namespace DeskBorder.Views;

public sealed partial class ManageWindow : WindowEx
{
    private const uint WindowCloseMessage = 0x0010;
    private const uint WindowQueryEndSessionMessage = 0x0011;
    private const uint WindowEndSessionMessage = 0x0016;

    private readonly IDeskBorderRuntimeService _deskBorderRuntimeService;
    private readonly IManageNavigationService _manageNavigationService;
    private readonly INavigatorService _navigatorService;
    private readonly ILocalizationService _localizationService;
    private readonly ISettingsService _settingsService;
    private readonly ITrayIconService _trayIconService;
    private readonly WindowSubclassProcedure _windowSubclassProcedure;
    private bool _forceClose;
    private bool _systemShutdown;

    private delegate nint WindowSubclassProcedure(nint windowHandle, uint message, nint wParam, nint lParam, nuint subclassIdentifier, nuint referenceData);

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint windowHandle, WindowSubclassProcedure procedure, nuint subclassIdentifier, nuint referenceData);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint windowHandle, uint message, nint wParam, nint lParam);

    public ManageWindow(
        IManageNavigationService manageNavigationService,
        IDeskBorderRuntimeService deskBorderRuntimeService,
        INavigatorService navigatorService,
        ITrayIconService trayIconService,
        ISettingsService settingsService,
        ILocalizationService localizationService)
    {
        _manageNavigationService = manageNavigationService;
        _deskBorderRuntimeService = deskBorderRuntimeService;
        _navigatorService = navigatorService;
        _trayIconService = trayIconService;
        _settingsService = settingsService;
        _localizationService = localizationService;

        InitializeComponent();

        AppWindow.SetIcon("Assets/Icon.ico");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(ApplicationTitleBar);

        _windowSubclassProcedure = OnWindowSubclassProcedure;
        _ = SetWindowSubclass(this.GetWindowHandle(), _windowSubclassProcedure, 1, 0);

        _manageNavigationService.RegisterFrame(AppFrame);
        AppFrame.Navigated += OnAppFrameNavigated;
        _localizationService.LanguageChanged += OnLocalizationServiceLanguageChanged;
        _trayIconService.StateChanged += OnTrayIconServiceStateChanged;
        Closed += OnManageWindowClosed;

        _manageNavigationService.NavigateTo(ManageNavigationTarget.Dashboard, clearBackStack: true);
        RefreshLocalizedText();
        UpdateNavigationChrome();
        RefreshTrayMenu();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private static bool TryParseManageNavigationTarget(object? selectedItemTag, out ManageNavigationTarget manageNavigationTarget)
    {
        manageNavigationTarget = default;
        return selectedItemTag is string selectedItemTagText && Enum.TryParse(selectedItemTagText, out manageNavigationTarget);
    }

    private void RefreshTrayMenu()
    {
        RuntimeStatusMenuFlyoutItem.Text = _trayIconService.RuntimeStatusText;
        LaunchOnStartupToggleMenuFlyoutItem.IsChecked = _trayIconService.IsLaunchOnStartupEnabled;
        LaunchOnStartupToggleMenuFlyoutItem.Text = _trayIconService.LaunchOnStartupToggleText;
        StoreUpdateCheckToggleMenuFlyoutItem.IsChecked = _trayIconService.IsStoreUpdateCheckEnabled;
        StoreUpdateCheckToggleMenuFlyoutItem.Text = _trayIconService.StoreUpdateCheckToggleText;
        RuntimeToggleMenuFlyoutItem.IsChecked = _trayIconService.IsRuntimeEnabled;
        RuntimeToggleMenuFlyoutItem.Text = _trayIconService.RuntimeToggleText;
    }

    private void OnApplicationTitleBarBackRequested(TitleBar sender, object eventArguments)
    {
        _manageNavigationService.GoBack();
        UpdateNavigationChrome();
    }

    private async void OnLaunchOnStartupToggleMenuFlyoutItemClicked(object sender, RoutedEventArgs routedEventArgs) => await _settingsService.SetLaunchOnStartupEnabledAsync(LaunchOnStartupToggleMenuFlyoutItem.IsChecked);

    private void OnExitApplicationMenuFlyoutItemClicked(object sender, RoutedEventArgs routedEventArgs) => Environment.Exit(0);

    private void OnLocalizationServiceLanguageChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (DispatcherQueue.TryEnqueue(HandleLocalizationChanged))
            return;

        HandleLocalizationChanged();
    }

    private void OnManageNavigationViewBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs navigationViewBackRequestedEventArgs)
    {
        _manageNavigationService.GoBack();
        UpdateNavigationChrome();
    }

    private void OnManageNavigationViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs navigationViewSelectionChangedEventArgs)
    {
        if (!TryParseManageNavigationTarget(navigationViewSelectionChangedEventArgs.SelectedItemContainer?.Tag, out var manageNavigationTarget))
            return;

        _manageNavigationService.NavigateTo(manageNavigationTarget);
        UpdateNavigationChrome();
    }

    private void OnAppFrameNavigated(object sender, NavigationEventArgs navigationEventArgs) => UpdateNavigationChrome();

    private void OnManageWindowClosed(object sender, WindowEventArgs windowEventArguments)
    {
        _localizationService.LanguageChanged -= OnLocalizationServiceLanguageChanged;
        _trayIconService.StateChanged -= OnTrayIconServiceStateChanged;
        AppFrame.Navigated -= OnAppFrameNavigated;
        Closed -= OnManageWindowClosed;
    }

    private void OnOpenManageWindowMenuFlyoutItemClicked(object sender, RoutedEventArgs routedEventArgs) => App.GetRequiredService<IManageWindowService>().Show();

    private async void OnRuntimeToggleMenuFlyoutItemClicked(object sender, RoutedEventArgs routedEventArgs) => await _deskBorderRuntimeService.SetRunningStateAsync(RuntimeToggleMenuFlyoutItem.IsChecked);

    private void OnShowNavigatorMenuFlyoutItemClicked(object sender, RoutedEventArgs routedEventArgs) => _navigatorService.ToggleOverlay();

    private async void OnStoreUpdateCheckToggleMenuFlyoutItemClicked(object sender, RoutedEventArgs routedEventArgs)
    {
        await _settingsService.UpdateSettingsAsync(_settingsService.Settings with
        {
            IsStoreUpdateCheckEnabled = StoreUpdateCheckToggleMenuFlyoutItem.IsChecked
        });
    }

    private void OnTrayIconServiceStateChanged(object? sender, EventArgs eventArguments)
    {
        if (DispatcherQueue.TryEnqueue(RefreshTrayMenu))
            return;

        RefreshTrayMenu();
    }

    private nint OnWindowSubclassProcedure(nint windowHandle, uint message, nint wParam, nint lParam, nuint subclassIdentifier, nuint referenceData)
    {
        switch (message)
        {
            case WindowQueryEndSessionMessage:
                _systemShutdown = true;
                return 1;

            case WindowEndSessionMessage:
                if (wParam != 0)
                    Environment.Exit(0);

                return 0;

            case WindowCloseMessage:
                if (_forceClose || _systemShutdown)
                    break;

                AppWindow.Hide();
                return 0;
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private void HandleLocalizationChanged()
    {
        RefreshLocalizedText();
        _manageNavigationService.ReloadCurrentPage();
        UpdateNavigationChrome();
    }

    private void RefreshLocalizedText()
    {
        var localizedWindowTitle = _localizationService.GetString("ManageWindow.WindowTitle");
        Title = localizedWindowTitle;
        ApplicationTitleBar.Title = localizedWindowTitle;
        OpenManageWindowMenuFlyoutItem.Text = _localizationService.GetString("ManageWindow.OpenManageWindow");
        ShowNavigatorMenuFlyoutItem.Text = _localizationService.GetString("ManageWindow.ShowNavigator");
        ExitApplicationMenuFlyoutItem.Text = _localizationService.GetString("ManageWindow.ExitApplication");
        RefreshTrayMenu();
    }

    private void UpdateNavigationChrome() => ApplicationTitleBar.IsBackButtonVisible = _manageNavigationService.CanGoBack;
}
