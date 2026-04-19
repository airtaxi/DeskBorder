using DeskBorder.Interop;
using DeskBorder.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeskBorder.Helpers;

public static class MouseHelper
{
    private const int VirtualScreenLeftSystemMetricIndex = 76;
    private const int VirtualScreenTopSystemMetricIndex = 77;
    private const int VirtualScreenWidthSystemMetricIndex = 78;
    private const int VirtualScreenHeightSystemMetricIndex = 79;
    private const int LeftShiftVirtualKey = 0xA0;
    private const int RightShiftVirtualKey = 0xA1;
    private const int LeftControlVirtualKey = 0xA2;
    private const int RightControlVirtualKey = 0xA3;
    private const int LeftAlternateVirtualKey = 0xA4;
    private const int RightAlternateVirtualKey = 0xA5;
    private const int LeftWindowsVirtualKey = 0x5B;
    private const int RightWindowsVirtualKey = 0x5C;

    public static bool AreRequiredKeyboardModifierKeysPressed(KeyboardModifierKeys requiredKeyboardModifierKeys, KeyboardModifierKeys pressedKeyboardModifierKeys) => (pressedKeyboardModifierKeys & requiredKeyboardModifierKeys) == requiredKeyboardModifierKeys;

    public static ScreenPoint GetCurrentCursorPosition()
    {
        if (!Win32.GetCursorPos(out var currentCursorPosition))
            throw new InvalidOperationException("Unable to retrieve the current cursor position.");

        return new(currentCursorPosition.X, currentCursorPosition.Y);
    }

    public static void SetCursorPosition(ScreenPoint position)
    {
        if (!Win32.SetCursorPos(position.X, position.Y))
            throw new InvalidOperationException("Unable to set the cursor position.");
    }

    public static bool TrySetCursorPosition(ScreenPoint position) => Win32.SetCursorPos(position.X, position.Y);

    public static CursorClippingState GetCursorClippingState()
    {
        if (!Win32.GetClipCursor(out var clippingRectangle))
            throw new InvalidOperationException("Unable to retrieve the cursor clipping rectangle.");

        var actualClippingRectangle = CreateScreenRectangle(clippingRectangle);
        var virtualScreenBounds = GetVirtualScreenBounds();
        return new()
        {
            ClippingRectangle = actualClippingRectangle,
            VirtualScreenBounds = virtualScreenBounds,
            IsCursorClipped = actualClippingRectangle != virtualScreenBounds
        };
    }

    public static DisplayMonitorInfo[] GetDisplayMonitors()
    {
        var displayMonitorInfos = new List<DisplayMonitorInfo>();
        var monitorEnumerationContext = new MonitorEnumerationContext(displayMonitorInfos);
        var monitorEnumerationContextHandle = GCHandle.Alloc(monitorEnumerationContext);

        try
        {
            var didEnumerateDisplayMonitors = Win32.EnumDisplayMonitors(
                0,
                0,
                static (monitorHandle, deviceContextHandle, monitorRectanglePointer, applicationData) =>
                {
                    _ = deviceContextHandle;
                    _ = monitorRectanglePointer;
                    var monitorEnumerationContext = (MonitorEnumerationContext?)GCHandle.FromIntPtr(applicationData).Target;
                    if (monitorEnumerationContext is null)
                        return false;

                    try
                    {
                        var monitorInfo = Win32.MonitorInfoExtended.Create();
                        if (!Win32.GetMonitorInfo(monitorHandle, ref monitorInfo))
                            throw new InvalidOperationException("Unable to retrieve display monitor information.");

                        monitorEnumerationContext.DisplayMonitorInfos.Add(new()
                        {
                            MonitorHandle = monitorHandle,
                            DeviceName = monitorInfo.GetDeviceName().TrimEnd('\0'),
                            MonitorBounds = CreateScreenRectangle(monitorInfo.MonitorRectangle),
                            WorkAreaBounds = CreateScreenRectangle(monitorInfo.WorkAreaRectangle),
                            IsPrimaryDisplay = monitorInfo.IsPrimary
                        });
                        return true;
                    }
                    catch (Exception exception)
                    {
                        monitorEnumerationContext.Exception = exception;
                        return false;
                    }
                },
                GCHandle.ToIntPtr(monitorEnumerationContextHandle));

            if (!didEnumerateDisplayMonitors)
                throw monitorEnumerationContext.Exception ?? new InvalidOperationException("Unable to enumerate display monitors.");

            return [.. displayMonitorInfos
                .OrderBy(displayMonitorInfo => displayMonitorInfo.MonitorBounds.Left)
                .ThenBy(displayMonitorInfo => displayMonitorInfo.MonitorBounds.Top)
                .ThenBy(displayMonitorInfo => displayMonitorInfo.DeviceName, StringComparer.OrdinalIgnoreCase)];
        }
        finally { monitorEnumerationContextHandle.Free(); }
    }

    public static DisplayMonitorInfo GetDisplayMonitorFromWindow(nint windowHandle)
    {
        if (!Win32.GetWindowRect(windowHandle, out var nativeWindowRectangle))
            throw new InvalidOperationException("Unable to retrieve the window bounds.");

        var windowRectangle = CreateScreenRectangle(nativeWindowRectangle);
        var displayMonitors = GetDisplayMonitors();
        if (displayMonitors.Length == 0)
            throw new InvalidOperationException("No display monitor is available.");

        return displayMonitors
            .OrderByDescending(displayMonitorInfo => GetIntersectionArea(displayMonitorInfo.MonitorBounds, windowRectangle))
            .ThenByDescending(displayMonitorInfo => displayMonitorInfo.IsPrimaryDisplay)
            .First();
    }

    public static ModifierKeySnapshot GetModifierKeySnapshot()
    {
        var pressedKeyboardModifierKeys = GetPressedKeyboardModifierKeys();
        return new()
        {
            PressedKeyboardModifierKeys = pressedKeyboardModifierKeys,
            IsShiftPressed = pressedKeyboardModifierKeys.HasFlag(KeyboardModifierKeys.Shift),
            IsControlPressed = pressedKeyboardModifierKeys.HasFlag(KeyboardModifierKeys.Control),
            IsAlternatePressed = pressedKeyboardModifierKeys.HasFlag(KeyboardModifierKeys.Alternate),
            IsWindowsPressed = pressedKeyboardModifierKeys.HasFlag(KeyboardModifierKeys.Windows)
        };
    }

    public static string? TryGetForegroundProcessName()
    {
        var foregroundWindowHandle = Win32.GetForegroundWindow();
        if (foregroundWindowHandle == 0)
            return null;

        _ = Win32.GetWindowThreadProcessId(foregroundWindowHandle, out var processIdentifier);
        if (processIdentifier == 0)
            return null;

        try
        {
            using var foregroundProcess = Process.GetProcessById((int)processIdentifier);
            var processName = foregroundProcess.ProcessName.Trim();
            return string.IsNullOrWhiteSpace(processName) ? null : processName;
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (Win32Exception) { return null; }
    }

    public static ScreenRectangle GetVirtualScreenBounds()
    {
        var left = Win32.GetSystemMetrics(VirtualScreenLeftSystemMetricIndex);
        var top = Win32.GetSystemMetrics(VirtualScreenTopSystemMetricIndex);
        var width = Win32.GetSystemMetrics(VirtualScreenWidthSystemMetricIndex);
        var height = Win32.GetSystemMetrics(VirtualScreenHeightSystemMetricIndex);
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Unable to retrieve the virtual screen bounds.");

        return new(left, top, left + width, top + height);
    }

    private static ScreenRectangle CreateScreenRectangle(Win32.NativeRectangle nativeRectangle) => new(nativeRectangle.Left, nativeRectangle.Top, nativeRectangle.Right, nativeRectangle.Bottom);

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

    private static KeyboardModifierKeys GetPressedKeyboardModifierKeys()
    {
        var pressedKeyboardModifierKeys = KeyboardModifierKeys.None;
        if (IsVirtualKeyPressed(LeftShiftVirtualKey) || IsVirtualKeyPressed(RightShiftVirtualKey))
            pressedKeyboardModifierKeys |= KeyboardModifierKeys.Shift;

        if (IsVirtualKeyPressed(LeftControlVirtualKey) || IsVirtualKeyPressed(RightControlVirtualKey))
            pressedKeyboardModifierKeys |= KeyboardModifierKeys.Control;

        if (IsVirtualKeyPressed(LeftAlternateVirtualKey) || IsVirtualKeyPressed(RightAlternateVirtualKey))
            pressedKeyboardModifierKeys |= KeyboardModifierKeys.Alternate;

        if (IsVirtualKeyPressed(LeftWindowsVirtualKey) || IsVirtualKeyPressed(RightWindowsVirtualKey))
            pressedKeyboardModifierKeys |= KeyboardModifierKeys.Windows;

        return pressedKeyboardModifierKeys;
    }

    private static bool IsVirtualKeyPressed(int virtualKey) => (Win32.GetAsyncKeyState(virtualKey) & Win32.AsyncKeyDownMask) == Win32.AsyncKeyDownMask;

    private sealed class MonitorEnumerationContext(List<DisplayMonitorInfo> displayMonitorInfos)
    {
        public List<DisplayMonitorInfo> DisplayMonitorInfos { get; } = displayMonitorInfos;

        public Exception? Exception { get; set; }
    }
}
