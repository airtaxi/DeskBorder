using DeskBorder.Models;
using DeskBorder.ViewModels;
using DeskBorder.Views;

namespace DeskBorder.Services;

public sealed class NavigatorService(
    IDesktopEdgeMonitorService desktopEdgeMonitorService,
    IVirtualDesktopService virtualDesktopService,
    IFileLogService fileLogService) : INavigatorService
{
    private readonly IDesktopEdgeMonitorService _desktopEdgeMonitorService = desktopEdgeMonitorService;
    private readonly IFileLogService _fileLogService = fileLogService;
    private NavigatorWindow? _navigatorWindow;
    private DisplayMonitorInfo? _targetDisplayMonitor;
    private readonly IVirtualDesktopService _virtualDesktopService = virtualDesktopService;

    public bool IsInitialized => _navigatorWindow is not null;

    public bool IsVisible => ViewModel.IsVisible;

    public NavigatorViewModel ViewModel { get; } = new();

    public void CloseFromKeyboard() => Hide();

    public void Hide()
    {
        ViewModel.IsVisible = false;
        if (_navigatorWindow?.AppWindow.IsVisible == true)
            _navigatorWindow.AppWindow.Hide();

        _fileLogService.WriteInformation(nameof(NavigatorService), "Hid the navigator overlay.");
    }

    public void Initialize(NavigatorWindow navigatorWindow)
    {
        if (_navigatorWindow is not null)
            return;

        _navigatorWindow = navigatorWindow;
        RefreshPreview();
        _fileLogService.WriteInformation(nameof(NavigatorService), "Initialized the navigator service.");
    }

    public void RefreshPreview()
    {
        var previewSnapshot = _virtualDesktopService.GetNavigatorPreviewSnapshot(ResolveTargetDisplayMonitor());
        _targetDisplayMonitor = previewSnapshot.TargetDisplayMonitor;
        ViewModel.ReplaceDesktopItems(
            previewSnapshot.DesktopItems,
            previewSnapshot.TargetDisplayMonitor.WorkAreaBounds.Width,
            previewSnapshot.TargetDisplayMonitor.WorkAreaBounds.Height);
        if (IsVisible)
            _navigatorWindow?.ShowOverlay(previewSnapshot.TargetDisplayMonitor);
    }

    public bool ShowFromTriggerArea()
    {
        if (!ViewModel.IsTriggerAreaEnabled)
            return false;

        ShowOverlay();
        _fileLogService.WriteInformation(nameof(NavigatorService), "Displayed the navigator overlay from the trigger area.");
        return true;
    }

    public void ToggleOverlay()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        ShowOverlay();
        _fileLogService.WriteInformation(nameof(NavigatorService), "Displayed the navigator overlay from the toggle action.");
    }

    public void UpdateTriggerAreaState(bool isEnabled, TriggerRectangleSettings triggerRectangleSettings)
    {
        _ = triggerRectangleSettings;
        ViewModel.IsTriggerAreaEnabled = isEnabled;
        _fileLogService.WriteInformation(nameof(NavigatorService), $"Updated trigger area enabled state to {isEnabled}.");
    }

    public bool UpdateTriggerAreaPointerState(bool isPointerInsideTriggerArea)
    {
        if (ViewModel.IsPointerInsideTriggerArea == isPointerInsideTriggerArea)
            return false;

        ViewModel.IsPointerInsideTriggerArea = isPointerInsideTriggerArea;
        if (isPointerInsideTriggerArea)
            return ShowFromTriggerArea();

        Hide();
        return true;
    }

    private DisplayMonitorInfo ResolveTargetDisplayMonitor()
    {
        var currentMonitoringState = _desktopEdgeMonitorService.CaptureCurrentState();
        return currentMonitoringState.CurrentDisplayMonitor
            ?? currentMonitoringState.DisplayMonitors.FirstOrDefault(displayMonitor => displayMonitor.IsPrimaryDisplay)
            ?? currentMonitoringState.DisplayMonitors.FirstOrDefault()
            ?? throw new InvalidOperationException("No display monitor is available for the navigator.");
    }

    private void ShowOverlay()
    {
        RefreshPreview();
        ViewModel.IsVisible = true;
        _navigatorWindow?.ShowOverlay(_targetDisplayMonitor ?? ResolveTargetDisplayMonitor());
    }
}
