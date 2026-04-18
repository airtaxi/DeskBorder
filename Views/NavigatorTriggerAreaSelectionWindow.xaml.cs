using DeskBorder.Interop;
using DeskBorder.Helpers;
using DeskBorder.Models;
using DeskBorder.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;
using WinUIEx;

namespace DeskBorder.Views;

public sealed partial class NavigatorTriggerAreaSelectionWindow : WindowEx
{
    private const double MinimumSelectionLength = 8.0;
    private readonly ILocalizationService _localizationService;
    private readonly IThemeService _themeService;
    private readonly DisplayMonitorInfo _targetDisplayMonitor;
    private readonly TaskCompletionSource<TriggerRectangleSettings?> _selectionTaskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ScreenPoint _selectionStartScreenPoint;
    private bool _isCompletingSelection;
    private bool _isPointerPressed;

    public NavigatorTriggerAreaSelectionWindow(ILocalizationService localizationService, IThemeService themeService, DisplayMonitorInfo targetDisplayMonitor)
    {
        _localizationService = localizationService;
        _themeService = themeService;
        _targetDisplayMonitor = targetDisplayMonitor;
        InitializeComponent();
        RegisterCurrentWindowContentWithThemeService();
        InitializeSelectionWindow();
    }

    public Task<TriggerRectangleSettings?> ShowSelectionAsync()
    {
        PrepareBorderlessOverlayPresentation();
        if (!AppWindow.IsVisible)
            AppWindow.Show();

        Activate();
        BringToFront();
        EnsureNonRoundedCorners();
        EnsureTopMostWindowState();
        _ = RootGrid.Focus(FocusState.Programmatic);
        return _selectionTaskCompletionSource.Task;
    }

    private TriggerRectangleSettings CreateTriggerRectangleSettings(ScreenRectangle selectionBounds)
    {
        var monitorBounds = _targetDisplayMonitor.MonitorBounds;
        if (monitorBounds.Width <= 0 || monitorBounds.Height <= 0)
            throw new InvalidOperationException("Unable to resolve the target display monitor bounds.");

        return new()
        {
            Left = Math.Clamp((double)(selectionBounds.Left - monitorBounds.Left) / monitorBounds.Width, 0.0, 1.0),
            Top = Math.Clamp((double)(selectionBounds.Top - monitorBounds.Top) / monitorBounds.Height, 0.0, 1.0),
            Width = Math.Clamp((double)selectionBounds.Width / monitorBounds.Width, 0.0, 1.0),
            Height = Math.Clamp((double)selectionBounds.Height / monitorBounds.Height, 0.0, 1.0)
        };
    }

    private void CompleteSelection(TriggerRectangleSettings? triggerRectangleSettings)
    {
        if (_isCompletingSelection)
            return;

        _isCompletingSelection = true;
        _selectionTaskCompletionSource.TrySetResult(triggerRectangleSettings);
        Close();
    }

    private ScreenRectangle GetSelectionBounds(ScreenPoint currentScreenPoint)
    {
        var left = Math.Min(_selectionStartScreenPoint.X, currentScreenPoint.X);
        var top = Math.Min(_selectionStartScreenPoint.Y, currentScreenPoint.Y);
        var right = Math.Max(_selectionStartScreenPoint.X, currentScreenPoint.X) + 1;
        var bottom = Math.Max(_selectionStartScreenPoint.Y, currentScreenPoint.Y) + 1;
        return new(left, top, right, bottom);
    }

    private void RegisterCurrentWindowContentWithThemeService()
    {
        if (Content is not FrameworkElement rootFrameworkElement)
            throw new InvalidOperationException("Unable to resolve the navigator trigger selection window root content for theme application.");

        _themeService.RegisterFrameworkElement(rootFrameworkElement);
    }

    private void InitializeSelectionWindow()
    {
        _localizationService.LanguageChanged += OnLocalizationServiceLanguageChanged;
        Activated += OnNavigatorTriggerAreaSelectionWindowActivated;
        Closed += OnNavigatorTriggerAreaSelectionWindowClosed;
        ConfigureBorderlessOverlayPresenter();
        RefreshLocalizedText();
    }

    private void ConfigureBorderlessOverlayPresenter()
    {
        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (AppWindow.Presenter is not OverlappedPresenter overlappedPresenter)
            throw new InvalidOperationException("Unable to configure the navigator trigger area selection window presenter.");

        overlappedPresenter.SetBorderAndTitleBar(false, false);
    }

    private void EnsureTopMostWindowState()
    {
        if (Win32.SetWindowPosition(
            this.GetWindowHandle(),
            Win32.TopMostWindowInsertAfterHandle,
            0,
            0,
            0,
            0,
            Win32.SetWindowPositionDoNotResizeFlag | Win32.SetWindowPositionDoNotMoveFlag | Win32.SetWindowPositionShowWindowFlag))
            return;

        throw new InvalidOperationException("Unable to place the navigator trigger area selection window above other windows.");
    }

    private void EnsureNonRoundedCorners()
    {
        var cornerPreference = Win32.DesktopWindowManagerWindowCornerPreferenceDoNotRound;
        if (Win32.DwmSetWindowInt32Attribute(
            this.GetWindowHandle(),
            Win32.DesktopWindowManagerWindowCornerPreferenceAttribute,
            cornerPreference,
            sizeof(int)) >= 0)
            return;

        throw new InvalidOperationException("Unable to disable rounded corners for the navigator trigger area selection window.");
    }

    private void OnEscapeKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs keyboardAcceleratorInvokedEventArgs)
    {
        _ = sender;
        keyboardAcceleratorInvokedEventArgs.Handled = true;
        CompleteSelection(null);
    }

    private void OnLocalizationServiceLanguageChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (DispatcherQueue.TryEnqueue(RefreshLocalizedText))
            return;

        RefreshLocalizedText();
    }

    private void OnNavigatorTriggerAreaSelectionWindowActivated(object sender, WindowActivatedEventArgs windowActivatedEventArgs)
    {
        _ = sender;
        if (windowActivatedEventArgs.WindowActivationState != WindowActivationState.Deactivated)
            return;

        CompleteSelection(null);
    }

    private void OnNavigatorTriggerAreaSelectionWindowClosed(object sender, WindowEventArgs windowEventArgs)
    {
        _ = sender;
        _ = windowEventArgs;
        _localizationService.LanguageChanged -= OnLocalizationServiceLanguageChanged;
        Activated -= OnNavigatorTriggerAreaSelectionWindowActivated;
        Closed -= OnNavigatorTriggerAreaSelectionWindowClosed;
        if (!_isCompletingSelection)
            _selectionTaskCompletionSource.TrySetResult(null);
    }

    private void OnRootGridPointerCanceled(object sender, PointerRoutedEventArgs pointerRoutedEventArgs)
    {
        _ = sender;
        _ = pointerRoutedEventArgs;
        ResetPointerSelection();
    }

    private void OnRootGridPointerMoved(object sender, PointerRoutedEventArgs pointerRoutedEventArgs)
    {
        _ = sender;
        if (!_isPointerPressed)
            return;

        UpdateSelectionVisual(GetClampedCurrentScreenPoint());
    }

    private void OnRootGridPointerPressed(object sender, PointerRoutedEventArgs pointerRoutedEventArgs)
    {
        _ = sender;
        _selectionStartScreenPoint = GetClampedCurrentScreenPoint();
        _isPointerPressed = true;
        _ = RootGrid.CapturePointer(pointerRoutedEventArgs.Pointer);
        UpdateSelectionVisual(_selectionStartScreenPoint);
    }

    private void OnRootGridPointerReleased(object sender, PointerRoutedEventArgs pointerRoutedEventArgs)
    {
        _ = sender;
        if (!_isPointerPressed)
            return;

        var selectionBounds = GetSelectionBounds(GetClampedCurrentScreenPoint());
        ResetPointerSelection();
        if (selectionBounds.Width < MinimumSelectionLength || selectionBounds.Height < MinimumSelectionLength)
            return;

        CompleteSelection(CreateTriggerRectangleSettings(selectionBounds));
    }

    private void PositionToTargetDisplayMonitor() => AppWindow.MoveAndResize(new RectInt32(
        _targetDisplayMonitor.MonitorBounds.Left,
        _targetDisplayMonitor.MonitorBounds.Top,
        _targetDisplayMonitor.MonitorBounds.Width,
        _targetDisplayMonitor.MonitorBounds.Height));

    private void PrepareBorderlessOverlayPresentation()
    {
        ConfigureBorderlessOverlayPresenter();
        PositionToTargetDisplayMonitor();
    }

    private void RefreshLocalizedText()
    {
        Title = _localizationService.GetString("NavigatorTriggerAreaSelectionWindow.WindowTitle");
        SelectionTitleTextBlock.Text = _localizationService.GetString("NavigatorTriggerAreaSelectionWindow.Title");
        SelectionDescriptionTextBlock.Text = _localizationService.GetString("NavigatorTriggerAreaSelectionWindow.Description");
    }

    private ScreenPoint GetClampedCurrentScreenPoint()
    {
        var currentScreenPoint = MouseHelper.GetCurrentCursorPosition();
        var monitorBounds = _targetDisplayMonitor.MonitorBounds;
        return new(
            Math.Clamp(currentScreenPoint.X, monitorBounds.Left, monitorBounds.Right - 1),
            Math.Clamp(currentScreenPoint.Y, monitorBounds.Top, monitorBounds.Bottom - 1));
    }

    private void ResetPointerSelection()
    {
        _isPointerPressed = false;
        SelectionBorder.Visibility = Visibility.Collapsed;
        RootGrid.ReleasePointerCaptures();
    }

    private void UpdateSelectionVisual(ScreenPoint currentScreenPoint)
    {
        var actualCanvasWidth = SelectionCanvas.ActualWidth;
        var actualCanvasHeight = SelectionCanvas.ActualHeight;
        if (actualCanvasWidth <= 0 || actualCanvasHeight <= 0)
            return;

        var monitorBounds = _targetDisplayMonitor.MonitorBounds;
        var selectionBounds = GetSelectionBounds(currentScreenPoint);
        var left = actualCanvasWidth * (selectionBounds.Left - monitorBounds.Left) / monitorBounds.Width;
        var top = actualCanvasHeight * (selectionBounds.Top - monitorBounds.Top) / monitorBounds.Height;
        var width = actualCanvasWidth * selectionBounds.Width / monitorBounds.Width;
        var height = actualCanvasHeight * selectionBounds.Height / monitorBounds.Height;
        Canvas.SetLeft(SelectionBorder, left);
        Canvas.SetTop(SelectionBorder, top);
        SelectionBorder.Width = width;
        SelectionBorder.Height = height;
        SelectionBorder.Visibility = Visibility.Visible;
    }
}
