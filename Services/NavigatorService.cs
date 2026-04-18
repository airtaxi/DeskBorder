using DeskBorder.Models;
using DeskBorder.ViewModels;
using DeskBorder.Views;

namespace DeskBorder.Services;

public sealed class NavigatorService(
    IDesktopEdgeMonitorService desktopEdgeMonitorService,
    IVirtualDesktopService virtualDesktopService) : INavigatorService
{
    private readonly IDesktopEdgeMonitorService _desktopEdgeMonitorService = desktopEdgeMonitorService;
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
    }

    public void Initialize(NavigatorWindow navigatorWindow)
    {
        if (_navigatorWindow is not null)
            return;

        _navigatorWindow = navigatorWindow;
        RefreshPreview();
    }

    public void RefreshPreview()
    {
        var previewSnapshot = _virtualDesktopService.GetNavigatorPreviewSnapshot(ResolveTargetDisplayMonitor());
        _targetDisplayMonitor = previewSnapshot.TargetDisplayMonitor;
        ViewModel.ReplaceDesktopItems(
            previewSnapshot.DesktopItems,
            previewSnapshot.TargetDisplayMonitor.MonitorBounds.Width,
            previewSnapshot.TargetDisplayMonitor.MonitorBounds.Height);
    }

    public bool ShowFromTriggerArea()
    {
        if (!ViewModel.IsTriggerAreaEnabled)
            return false;

        ShowOverlay();
        return true;
    }

    public void ToggleOverlay()
    {
        if (IsVisible)
            Hide();
        else
            ShowOverlay();
    }

    public void UpdateTriggerAreaState(bool isEnabled, TriggerRectangleSettings triggerRectangleSettings)
    {
        _ = triggerRectangleSettings;
        ViewModel.IsTriggerAreaEnabled = isEnabled;
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
