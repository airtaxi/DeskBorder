using DeskBorder.Helpers;
using DeskBorder.Interop;
using DeskBorder.Models;
using System.Runtime.InteropServices;

namespace DeskBorder.Services;

public sealed class ForegroundWindowFullscreenService : IForegroundWindowFullscreenService
{
    private const int BoundsToleranceInPixels = 2;

    public ForegroundWindowFullscreenState GetForegroundWindowFullscreenState()
    {
        try { return GetForegroundWindowFullscreenState(MouseHelper.GetDisplayMonitors()); }
        catch (InvalidOperationException) { return new(); }
    }

    public ForegroundWindowFullscreenState GetForegroundWindowFullscreenState(DisplayMonitorInfo[] displayMonitors)
    {
        try
        {
            var foregroundWindowHandle = Win32.GetForegroundWindow();
            if (foregroundWindowHandle == 0 || foregroundWindowHandle == Win32.GetShellWindow() || Win32.IsIconic(foregroundWindowHandle) || !Win32.IsWindowVisible(foregroundWindowHandle)) return new();

            if (!TryGetWindowBounds(foregroundWindowHandle, out var windowBounds) || windowBounds.IsEmpty) return new();

            var displayMonitor = FindDisplayMonitorForWindowBounds(displayMonitors, windowBounds);
            if (displayMonitor is null || !DoesWindowCoverDisplayMonitor(windowBounds, displayMonitor.MonitorBounds)) return new();

            return new()
            {
                WindowHandle = foregroundWindowHandle,
                FullscreenKind = IsCursorClippedToDisplayMonitor(displayMonitor.MonitorBounds) ? ForegroundWindowFullscreenKind.Fullscreen : ForegroundWindowFullscreenKind.WindowedFullscreen,
                WindowBounds = windowBounds,
                DisplayMonitorBounds = displayMonitor.MonitorBounds
            };
        }
        catch (ExternalException) { return new(); }
        catch (InvalidOperationException) { return new(); }
    }

    public bool ShouldDisableDesktopSwitchingAndCreation(ForegroundWindowFullscreenState foregroundWindowFullscreenState, DeskBorderSettings settings)
    {
        if (!settings.IsDesktopSwitchingAndCreationDisabledWhenForegroundWindowIsFullscreen) return false;

        return foregroundWindowFullscreenState.FullscreenKind switch
        {
            ForegroundWindowFullscreenKind.Fullscreen => true,
            ForegroundWindowFullscreenKind.WindowedFullscreen => settings.IsWindowedFullscreenIncludedWhenDisablingDesktopSwitchingAndCreation,
            _ => false
        };
    }

    private static bool DoesWindowCoverDisplayMonitor(ScreenRectangle windowBounds, ScreenRectangle displayMonitorBounds) =>
        Math.Abs(windowBounds.Left - displayMonitorBounds.Left) <= BoundsToleranceInPixels
        && Math.Abs(windowBounds.Top - displayMonitorBounds.Top) <= BoundsToleranceInPixels
        && Math.Abs(windowBounds.Right - displayMonitorBounds.Right) <= BoundsToleranceInPixels
        && Math.Abs(windowBounds.Bottom - displayMonitorBounds.Bottom) <= BoundsToleranceInPixels;

    private static DisplayMonitorInfo? FindDisplayMonitorForWindowBounds(DisplayMonitorInfo[] displayMonitors, ScreenRectangle windowBounds)
    {
        if (displayMonitors.Length == 0) return null;

        return displayMonitors
            .OrderByDescending(displayMonitor => GetIntersectionArea(displayMonitor.MonitorBounds, windowBounds))
            .ThenByDescending(displayMonitor => displayMonitor.IsPrimaryDisplay)
            .FirstOrDefault();
    }

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

    private static bool TryGetWindowBounds(nint windowHandle, out ScreenRectangle windowBounds)
    {
        if (Win32.DwmGetWindowRectangleAttribute(
            windowHandle,
            Win32.DesktopWindowManagerExtendedFrameBoundsAttribute,
            out var nativeWindowRectangle,
            (uint)Marshal.SizeOf<Win32.NativeRectangle>()) >= 0
            && !nativeWindowRectangle.IsEmpty)
        {
            windowBounds = CreateScreenRectangle(nativeWindowRectangle);
            return true;
        }

        if (Win32.GetWindowRect(windowHandle, out nativeWindowRectangle) && !nativeWindowRectangle.IsEmpty)
        {
            windowBounds = CreateScreenRectangle(nativeWindowRectangle);
            return true;
        }

        windowBounds = new();
        return false;
    }

    private static ScreenRectangle CreateScreenRectangle(Win32.NativeRectangle nativeRectangle) => new(nativeRectangle.Left, nativeRectangle.Top, nativeRectangle.Right, nativeRectangle.Bottom);

    private static bool IsCursorClippedToDisplayMonitor(ScreenRectangle displayMonitorBounds)
    {
        try
        {
            var cursorClippingState = MouseHelper.GetCursorClippingState();
            return cursorClippingState.IsCursorClipped
                && displayMonitorBounds.Contains(cursorClippingState.ClippingRectangle.Left, cursorClippingState.ClippingRectangle.Top)
                && displayMonitorBounds.Contains(cursorClippingState.ClippingRectangle.Right - 1, cursorClippingState.ClippingRectangle.Bottom - 1);
        }
        catch (InvalidOperationException) { return false; }
    }
}
