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
    private readonly DisplayMonitorInfo _targetDisplayMonitor;
    private readonly TaskCompletionSource<TriggerRectangleSettings?> _selectionTaskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Point _selectionStartPoint;
    private bool _isCompletingSelection;
    private bool _isPointerPressed;

    public NavigatorTriggerAreaSelectionWindow(ILocalizationService localizationService, DisplayMonitorInfo targetDisplayMonitor)
    {
        _localizationService = localizationService;
        _targetDisplayMonitor = targetDisplayMonitor;
        InitializeComponent();
        InitializeSelectionWindow();
    }

    public Task<TriggerRectangleSettings?> ShowSelectionAsync()
    {
        PositionToTargetDisplayMonitor();
        if (!AppWindow.IsVisible)
            AppWindow.Show();

        Activate();
        BringToFront();
        _ = RootGrid.Focus(FocusState.Programmatic);
        return _selectionTaskCompletionSource.Task;
    }

    private TriggerRectangleSettings CreateTriggerRectangleSettings(Rect selectionBounds)
    {
        var actualCanvasWidth = SelectionCanvas.ActualWidth;
        var actualCanvasHeight = SelectionCanvas.ActualHeight;
        if (actualCanvasWidth <= 0 || actualCanvasHeight <= 0)
            throw new InvalidOperationException("Unable to resolve the selection canvas bounds.");

        return new()
        {
            Left = Math.Clamp(selectionBounds.Left / actualCanvasWidth, 0.0, 1.0),
            Top = Math.Clamp(selectionBounds.Top / actualCanvasHeight, 0.0, 1.0),
            Width = Math.Clamp(selectionBounds.Width / actualCanvasWidth, 0.0, 1.0),
            Height = Math.Clamp(selectionBounds.Height / actualCanvasHeight, 0.0, 1.0)
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

    private Rect GetSelectionBounds(Point currentPoint)
    {
        var left = Math.Min(_selectionStartPoint.X, currentPoint.X);
        var top = Math.Min(_selectionStartPoint.Y, currentPoint.Y);
        var right = Math.Max(_selectionStartPoint.X, currentPoint.X);
        var bottom = Math.Max(_selectionStartPoint.Y, currentPoint.Y);
        return new(left, top, right - left, bottom - top);
    }

    private void InitializeSelectionWindow()
    {
        _localizationService.LanguageChanged += OnLocalizationServiceLanguageChanged;
        Activated += OnNavigatorTriggerAreaSelectionWindowActivated;
        Closed += OnNavigatorTriggerAreaSelectionWindowClosed;
        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        RefreshLocalizedText();
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

        UpdateSelectionVisual(pointerRoutedEventArgs.GetCurrentPoint(SelectionCanvas).Position);
    }

    private void OnRootGridPointerPressed(object sender, PointerRoutedEventArgs pointerRoutedEventArgs)
    {
        _ = sender;
        _selectionStartPoint = pointerRoutedEventArgs.GetCurrentPoint(SelectionCanvas).Position;
        _isPointerPressed = true;
        _ = RootGrid.CapturePointer(pointerRoutedEventArgs.Pointer);
        UpdateSelectionVisual(_selectionStartPoint);
    }

    private void OnRootGridPointerReleased(object sender, PointerRoutedEventArgs pointerRoutedEventArgs)
    {
        _ = sender;
        if (!_isPointerPressed)
            return;

        var selectionBounds = GetSelectionBounds(pointerRoutedEventArgs.GetCurrentPoint(SelectionCanvas).Position);
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

    private void RefreshLocalizedText()
    {
        Title = _localizationService.GetString("NavigatorTriggerAreaSelectionWindow.WindowTitle");
        SelectionTitleTextBlock.Text = _localizationService.GetString("NavigatorTriggerAreaSelectionWindow.Title");
        SelectionDescriptionTextBlock.Text = _localizationService.GetString("NavigatorTriggerAreaSelectionWindow.Description");
    }

    private void ResetPointerSelection()
    {
        _isPointerPressed = false;
        SelectionBorder.Visibility = Visibility.Collapsed;
        RootGrid.ReleasePointerCaptures();
    }

    private void UpdateSelectionVisual(Point currentPoint)
    {
        var selectionBounds = GetSelectionBounds(currentPoint);
        Canvas.SetLeft(SelectionBorder, selectionBounds.Left);
        Canvas.SetTop(SelectionBorder, selectionBounds.Top);
        SelectionBorder.Width = selectionBounds.Width;
        SelectionBorder.Height = selectionBounds.Height;
        SelectionBorder.Visibility = Visibility.Visible;
    }
}
