using DeskBorder.Helpers;
using DeskBorder.Interop;
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

public enum ProcessDesktopPlacementRuleLifetime
{
    Permanent,
    UntilProcessExit,
    Timed,
}

public sealed record ProcessDesktopPlacementPopupInitialRule(ProcessDesktopPlacementRuleLifetime Lifetime, TimeSpan? Duration = null);

public sealed partial class ProcessDesktopPlacementPopupWindow : WindowEx
{
    private const int FallbackPopupWindowHeight = 430;
    private const int PopupWindowHeightPadding = 10;
    private const int PopupWindowWidth = 460;

    private readonly ILocalizationService _localizationService;
    private readonly IThemeService _themeService;
    private readonly TaskCompletionSource<bool> _completionTaskSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isCompleting;
    private bool _isResizingToContent;
    private bool _shouldRestoreOwnerWindowEnabled;
    private int _lastResizedPopupWindowHeight;
    private nint _ownerWindowHandle;

    public ProcessDesktopPlacementPopupWindow(
        IReadOnlyList<string> processNames,
        ProcessDesktopPlacementTargetSnapshot targetSnapshot,
        ILocalizationService localizationService,
        IThemeService themeService,
        ProcessDesktopPlacementPopupInitialRule? initialRule = null)
    {
        _localizationService = localizationService;
        _themeService = themeService;
        InitializeComponent();
        RegisterCurrentWindowContentWithThemeService();
        ConfigurePopupPresenter();
        ApplyLocalizedText(initialRule is not null);
        ProcessNameTextBlock.Text = localizationService.GetFormattedString("ProcessDesktopPlacementPopup.ProcessNameFormat", FormatProcessNames(processNames));
        TargetDesktopNumberBox.Value = Math.Max(1, targetSnapshot.DesktopNumber);
        ApplyInitialRule(initialRule);
        UpdateTargetDesktopText();
    }

    public TimeSpan Duration => TimeSpan.FromMinutes(Math.Clamp(double.IsFinite(TimedDurationMinutesNumberBox.Value) ? TimedDurationMinutesNumberBox.Value : 30, 1, 1440));

    public ProcessDesktopPlacementRuleLifetime Lifetime => LifetimeComboBox.SelectedItem is ComboBoxItem { Tag: string tag }
        && Enum.TryParse<ProcessDesktopPlacementRuleLifetime>(tag, out var lifetime)
            ? lifetime
            : ProcessDesktopPlacementRuleLifetime.Permanent;

    public int TargetDesktopNumber => Math.Clamp(double.IsFinite(TargetDesktopNumberBox.Value) ? (int)Math.Round(TargetDesktopNumberBox.Value, MidpointRounding.AwayFromZero) : 1, 1, 99);

    public Task<bool> ShowModalAsync(nint ownerWindowHandle)
    {
        _ownerWindowHandle = ownerWindowHandle;
        PrepareModalPopupWindow();
        AppWindow.Show();
        ResizePopupWindowToContent(RootGrid, true);
        Activate();
        BringToFront();
        _ = RootGrid.Focus(FocusState.Programmatic);
        return _completionTaskSource.Task;
    }

    private void ApplyInitialRule(ProcessDesktopPlacementPopupInitialRule? initialRule)
    {
        if (initialRule is null) return;

        SelectLifetime(initialRule.Lifetime);
        if (initialRule.Duration.HasValue) TimedDurationMinutesNumberBox.Value = Math.Clamp(Math.Ceiling(initialRule.Duration.Value.TotalMinutes), 1, 1440);
        TimedDurationMinutesNumberBox.Visibility = initialRule.Lifetime == ProcessDesktopPlacementRuleLifetime.Timed
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyLocalizedText(bool isEditingExistingRule)
    {
        var titleResourceName = isEditingExistingRule
            ? "ProcessDesktopPlacementPopup.EditTitle"
            : "ProcessDesktopPlacementPopupWindow.Title";
        var descriptionResourceName = isEditingExistingRule
            ? "ProcessDesktopPlacementPopup.EditDescription"
            : "ProcessDesktopPlacementPopupWindow_DescriptionTextBlock.Text";
        var title = _localizationService.GetString(titleResourceName);
        Title = title;
        TitleTextBlock.Text = title;
        DescriptionTextBlock.Text = _localizationService.GetString(descriptionResourceName);
    }

    private void Complete(bool isAccepted)
    {
        if (_isCompleting) return;

        _isCompleting = true;
        RestoreOwnerWindowEnabledState();
        _completionTaskSource.TrySetResult(isAccepted);
        Close();
    }

    private void ConfigurePopupPresenter()
    {
        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (AppWindow.Presenter is not OverlappedPresenter overlappedPresenter) throw new InvalidOperationException("Unable to configure the process desktop placement popup presenter.");

        overlappedPresenter.SetBorderAndTitleBar(true, false);
        overlappedPresenter.IsMaximizable = false;
        overlappedPresenter.IsMinimizable = false;
        overlappedPresenter.IsResizable = false;
    }

    private ScreenRectangle GetOwnerWindowBounds()
    {
        if (_ownerWindowHandle != 0 && Win32.GetWindowRect(_ownerWindowHandle, out var ownerWindowRectangle)) return new(ownerWindowRectangle.Left, ownerWindowRectangle.Top, ownerWindowRectangle.Right, ownerWindowRectangle.Bottom);

        var displayMonitors = MouseHelper.GetDisplayMonitors();
        var currentCursorPosition = MouseHelper.GetCurrentCursorPosition();
        var targetDisplayMonitor = displayMonitors
            .FirstOrDefault(displayMonitorInfo => displayMonitorInfo.WorkAreaBounds.Contains(currentCursorPosition))
            ?? displayMonitors.FirstOrDefault(displayMonitorInfo => displayMonitorInfo.IsPrimaryDisplay)
            ?? displayMonitors.FirstOrDefault()
            ?? throw new InvalidOperationException("No display monitor is available for the process desktop placement popup window.");
        return targetDisplayMonitor.WorkAreaBounds;
    }

    private DisplayMonitorInfo GetTargetDisplayMonitor(ScreenRectangle ownerWindowBounds)
    {
        var displayMonitors = MouseHelper.GetDisplayMonitors();
        if (displayMonitors.Length == 0) throw new InvalidOperationException("No display monitor is available for the process desktop placement popup window.");

        return displayMonitors
            .OrderByDescending(displayMonitorInfo => GetIntersectionArea(displayMonitorInfo.WorkAreaBounds, ownerWindowBounds))
            .ThenByDescending(displayMonitorInfo => displayMonitorInfo.IsPrimaryDisplay)
            .First();
    }

    private void PositionPopupWindow(int popupWindowWidth, int popupWindowHeight)
    {
        var ownerWindowBounds = GetOwnerWindowBounds();
        var targetWorkAreaBounds = GetTargetDisplayMonitor(ownerWindowBounds).WorkAreaBounds;
        var targetLeft = ownerWindowBounds.Left + (ownerWindowBounds.Width - popupWindowWidth) / 2;
        var targetTop = ownerWindowBounds.Top + (ownerWindowBounds.Height - popupWindowHeight) / 2;
        var left = Math.Clamp(targetLeft, targetWorkAreaBounds.Left, Math.Max(targetWorkAreaBounds.Left, targetWorkAreaBounds.Right - popupWindowWidth));
        var top = Math.Clamp(targetTop, targetWorkAreaBounds.Top, Math.Max(targetWorkAreaBounds.Top, targetWorkAreaBounds.Bottom - popupWindowHeight));
        AppWindow.MoveAndResize(new RectInt32(left, top, popupWindowWidth, popupWindowHeight));
    }

    private void PrepareModalPopupWindow()
    {
        ConfigurePopupPresenter();
        PositionPopupWindow(
            ScaleToWindowPixels(this.GetWindowHandle(), PopupWindowWidth),
            ScaleToWindowPixels(this.GetWindowHandle(), FallbackPopupWindowHeight));
        SetOwnerWindow();
        DisableOwnerWindow();
    }

    private void RegisterCurrentWindowContentWithThemeService()
    {
        if (Content is not FrameworkElement rootFrameworkElement) throw new InvalidOperationException("Unable to resolve the process desktop placement popup window root content for theme application.");

        _themeService.RegisterFrameworkElement(rootFrameworkElement);
    }

    private void RestoreOwnerWindowEnabledState()
    {
        if (!_shouldRestoreOwnerWindowEnabled || _ownerWindowHandle == 0 || !Win32.IsWindow(_ownerWindowHandle)) return;

        _ = Win32.EnableWindow(_ownerWindowHandle, true);
        _shouldRestoreOwnerWindowEnabled = false;
    }

    private void SetOwnerWindow()
    {
        if (_ownerWindowHandle == 0 || !Win32.IsWindow(_ownerWindowHandle)) return;

        _ = Win32.SetWindowLongPointer(this.GetWindowHandle(), Win32.WindowLongPointerOwnerIndex, _ownerWindowHandle);
    }

    private void DisableOwnerWindow()
    {
        if (_ownerWindowHandle == 0 || !Win32.IsWindow(_ownerWindowHandle) || !Win32.IsWindowEnabled(_ownerWindowHandle)) return;

        _ = Win32.EnableWindow(_ownerWindowHandle, false);
        _shouldRestoreOwnerWindowEnabled = true;
    }

    private void ResizePopupWindowToContent(FrameworkElement rootElement, bool shouldUpdateMeasure)
    {
        if (_isResizingToContent || rootElement.XamlRoot is null) return;

        _isResizingToContent = true;
        try
        {
            if (shouldUpdateMeasure) rootElement.Measure(new Size(PopupWindowWidth, double.PositiveInfinity));

            var popupWindowLogicalHeight = (int)Math.Ceiling(rootElement.DesiredSize.Height + PopupWindowHeightPadding);
            if (popupWindowLogicalHeight <= 0) return;

            var windowHandle = this.GetWindowHandle();
            var popupWindowHeight = ScaleToWindowPixels(windowHandle, popupWindowLogicalHeight);
            if (popupWindowHeight <= 0 || popupWindowHeight == _lastResizedPopupWindowHeight) return;

            _lastResizedPopupWindowHeight = popupWindowHeight;
            PositionPopupWindow(ScaleToWindowPixels(windowHandle, PopupWindowWidth), popupWindowHeight);
        }
        finally { _isResizingToContent = false; }
    }

    private void OnCancelButtonClicked(object sender, RoutedEventArgs args) => Complete(false);

    private void OnCancelKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Complete(false);
    }

    private void OnLifetimeComboBoxSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (TimedDurationMinutesNumberBox is null) return;

        TimedDurationMinutesNumberBox.Visibility = Lifetime == ProcessDesktopPlacementRuleLifetime.Timed
            ? Visibility.Visible
            : Visibility.Collapsed;
        ResizePopupWindowToContent(RootGrid, true);
    }

    private void OnProcessDesktopPlacementPopupWindowClosed(object sender, WindowEventArgs args)
    {
        RootGrid.LayoutUpdated -= OnRootGridLayoutUpdated;
        RestoreOwnerWindowEnabledState();
        if (!_isCompleting) _completionTaskSource.TrySetResult(false);
    }

    private void OnRootGridLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement rootElement) return;

        ResizePopupWindowToContent(rootElement, true);
        rootElement.LayoutUpdated -= OnRootGridLayoutUpdated;
        rootElement.LayoutUpdated += OnRootGridLayoutUpdated;
    }

    private void OnRootGridLayoutUpdated(object? _, object __) => ResizePopupWindowToContent(RootGrid, false);

    private void OnSaveButtonClicked(object sender, RoutedEventArgs args) => Complete(true);

    private void OnSaveKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Complete(true);
    }

    private void OnTargetDesktopNumberBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (TargetDesktopTextBlock is null || TargetDesktopNumberBox is null) return;

        UpdateTargetDesktopText();
        ResizePopupWindowToContent(RootGrid, true);
    }

    private void UpdateTargetDesktopText()
    {
        TargetDesktopTextBlock.Text = _localizationService.GetFormattedString(
            "ProcessDesktopPlacementPopup.TargetDesktopFormat",
            SettingsDisplayFormatter.FormatDesktopDisplayName(TargetDesktopNumber));
    }

    private void SelectLifetime(ProcessDesktopPlacementRuleLifetime lifetime)
    {
        foreach (var lifetimeComboBoxItem in LifetimeComboBox.Items.OfType<ComboBoxItem>())
        {
            if (lifetimeComboBoxItem.Tag is not string tag || !Enum.TryParse<ProcessDesktopPlacementRuleLifetime>(tag, out var itemLifetime) || itemLifetime != lifetime) continue;

            LifetimeComboBox.SelectedItem = lifetimeComboBoxItem;
            return;
        }
    }

    private static string FormatProcessNames(IReadOnlyList<string> processNames) => string.Join(", ", processNames.Select(processName => processName.Trim()).Where(processName => !string.IsNullOrWhiteSpace(processName)));

    private static int GetIntersectionArea(ScreenRectangle firstRectangle, ScreenRectangle secondRectangle)
    {
        var left = Math.Max(firstRectangle.Left, secondRectangle.Left);
        var top = Math.Max(firstRectangle.Top, secondRectangle.Top);
        var right = Math.Min(firstRectangle.Right, secondRectangle.Right);
        var bottom = Math.Min(firstRectangle.Bottom, secondRectangle.Bottom);
        return right <= left || bottom <= top
            ? 0
            : (right - left) * (bottom - top);
    }

    private static int ScaleToWindowPixels(nint windowHandle, int logicalPixels) => (int)Math.Round(logicalPixels * Win32.GetDpiForWindow(windowHandle) / 96d, MidpointRounding.AwayFromZero);
}
