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
    private const int LeftMouseButtonVirtualKey = 0x01;
    private const int MiddleMouseButtonVirtualKey = 0x04;
    private const int RightMouseButtonVirtualKey = 0x02;

    public static bool AreRequiredMouseModifierButtonTriggersPressed(IReadOnlyList<InputTriggerType> requiredMouseModifierButtonTriggers, MouseButtonSnapshot mouseButtonSnapshot)
    {
        foreach (var requiredMouseModifierButtonTrigger in requiredMouseModifierButtonTriggers)
        {
            if (!IsMouseModifierButtonTrigger(requiredMouseModifierButtonTrigger)) return false;

            if (!IsMouseModifierButtonPressed(requiredMouseModifierButtonTrigger, mouseButtonSnapshot)) return false;
        }

        return true;
    }

    public static bool IsMouseModifierButtonTrigger(InputTriggerType inputTriggerType) => inputTriggerType is InputTriggerType.MouseLeftButton or InputTriggerType.MouseMiddleButton or InputTriggerType.MouseRightButton;

    public static ScreenPoint GetCurrentCursorPosition()
    {
        if (!TryGetCurrentCursorPosition(out var currentCursorPosition, out _)) throw new InvalidOperationException("Unable to retrieve the current cursor position.");

        return currentCursorPosition;
    }

    public static bool TryGetCurrentCursorPosition(out ScreenPoint currentCursorPosition, out int lastWindowsErrorCode)
    {
        if (Win32.GetCursorPos(out var nativeCursorPosition))
        {
            currentCursorPosition = new(nativeCursorPosition.X, nativeCursorPosition.Y);
            lastWindowsErrorCode = 0;
            return true;
        }

        currentCursorPosition = default;
        lastWindowsErrorCode = Marshal.GetLastWin32Error();
        return false;
    }

    public static void SetCursorPosition(ScreenPoint position)
    {
        if (TrySetCursorPosition(position, out var lastWindowsErrorCode)) return;

        var lastWindowsErrorMessage = new Win32Exception(lastWindowsErrorCode).Message;
        throw new InvalidOperationException($"Unable to set the cursor position. LastWindowsErrorCode={lastWindowsErrorCode} (0x{lastWindowsErrorCode:X8}, {lastWindowsErrorMessage}).");
    }

    public static bool TrySetCursorPosition(ScreenPoint position) => TrySetCursorPosition(position, out _);

    public static bool TrySetCursorPosition(ScreenPoint position, out int lastWindowsErrorCode)
    {
        if (Win32.SetCursorPos(position.X, position.Y))
        {
            lastWindowsErrorCode = 0;
            return true;
        }

        lastWindowsErrorCode = Marshal.GetLastWin32Error();
        return false;
    }

    public static CursorClippingState GetCursorClippingState()
    {
        if (!Win32.GetClipCursor(out var clippingRectangle)) throw new InvalidOperationException("Unable to retrieve the cursor clipping rectangle.");

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
                    if (monitorEnumerationContext is null) return false;

                    try
                    {
                        var monitorInfo = Win32.MonitorInfoExtended.Create();
                        if (!Win32.GetMonitorInfo(monitorHandle, ref monitorInfo)) throw new InvalidOperationException("Unable to retrieve display monitor information.");

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

            if (!didEnumerateDisplayMonitors) throw monitorEnumerationContext.Exception ?? new InvalidOperationException("Unable to enumerate display monitors.");

            return [.. displayMonitorInfos
                .OrderBy(displayMonitorInfo => displayMonitorInfo.MonitorBounds.Left)
                .ThenBy(displayMonitorInfo => displayMonitorInfo.MonitorBounds.Top)
                .ThenBy(displayMonitorInfo => displayMonitorInfo.DeviceName, StringComparer.OrdinalIgnoreCase)];
        }
        finally { monitorEnumerationContextHandle.Free(); }
    }

    public static DisplayMonitorInfo GetDisplayMonitorFromWindow(nint windowHandle)
    {
        if (!Win32.GetWindowRect(windowHandle, out var nativeWindowRectangle)) throw new InvalidOperationException("Unable to retrieve the window bounds.");

        var windowRectangle = CreateScreenRectangle(nativeWindowRectangle);
        var displayMonitors = GetDisplayMonitors();
        if (displayMonitors.Length == 0) throw new InvalidOperationException("No display monitor is available.");

        return displayMonitors
            .OrderByDescending(displayMonitorInfo => GetIntersectionArea(displayMonitorInfo.MonitorBounds, windowRectangle))
            .ThenByDescending(displayMonitorInfo => displayMonitorInfo.IsPrimaryDisplay)
            .First();
    }

    public static MouseButtonSnapshot GetMouseButtonSnapshot() => new()
    {
        IsLeftButtonPressed = IsVirtualKeyPressed(LeftMouseButtonVirtualKey),
        IsRightButtonPressed = IsVirtualKeyPressed(RightMouseButtonVirtualKey),
        IsMiddleButtonPressed = IsVirtualKeyPressed(MiddleMouseButtonVirtualKey)
    };

    public static ForegroundProcessSnapshot GetForegroundProcessSnapshot()
    {
        var foregroundWindowHandle = Win32.GetForegroundWindow();
        if (foregroundWindowHandle == 0) return new();

        _ = Win32.GetWindowThreadProcessId(foregroundWindowHandle, out var processIdentifier);
        if (processIdentifier == 0) return new();

        try
        {
            using var foregroundProcess = Process.GetProcessById((int)processIdentifier);
            var processName = foregroundProcess.ProcessName.Trim();
            string? executablePath;
            try { executablePath = foregroundProcess.MainModule?.FileName?.Trim(); }
            catch (InvalidOperationException) { executablePath = null; }
            catch (NotSupportedException) { executablePath = null; }
            catch (Win32Exception) { executablePath = null; }
            return new()
            {
                ProcessName = string.IsNullOrWhiteSpace(processName) ? null : processName,
                ExecutablePath = string.IsNullOrWhiteSpace(executablePath) ? null : executablePath
            };
        }
        catch (ArgumentException) { return new(); }
        catch (InvalidOperationException) { return new(); }
        catch (NotSupportedException) { return new(); }
        catch (Win32Exception) { return new(); }
    }

    public static void ConsumePressedMouseModifierButtonTriggers(IReadOnlyList<InputTriggerType> inputTriggerTypes)
    {
        var requestedInputTriggerTypes = inputTriggerTypes
            .Where(IsMouseModifierButtonTrigger)
            .Distinct()
            .ToArray();
        if (requestedInputTriggerTypes.Length == 0) return;

        var mouseButtonSnapshotBeforeConsume = GetMouseButtonSnapshot();
        var pressedMouseButtonStateSummaryBeforeConsume = CreatePressedMouseButtonStateSummary();
        var mouseInputs = new List<Win32.NativeInput>(requestedInputTriggerTypes.Length);
        foreach (var inputTriggerType in requestedInputTriggerTypes)
        {
            if (IsMouseModifierButtonPressed(inputTriggerType, mouseButtonSnapshotBeforeConsume))
            {
                mouseInputs.Add(CreateMouseInput(GetMouseButtonUpEventFlag(inputTriggerType)));
            }
        }

        if (mouseInputs.Count == 0) return;

        var nativeInputSize = Marshal.SizeOf<Win32.NativeInput>();
        var sentInputCount = Win32.SendInput((uint)mouseInputs.Count, [.. mouseInputs], nativeInputSize);
        if (sentInputCount != mouseInputs.Count) throw CreateMouseModifierButtonConsumeException(requestedInputTriggerTypes, mouseButtonSnapshotBeforeConsume, pressedMouseButtonStateSummaryBeforeConsume, mouseInputs, sentInputCount, nativeInputSize);
    }

    public static string? TryGetForegroundProcessName()
    {
        var foregroundProcessSnapshot = GetForegroundProcessSnapshot();
        return foregroundProcessSnapshot.ProcessName;
    }

    public static ScreenRectangle GetVirtualScreenBounds()
    {
        var left = Win32.GetSystemMetrics(VirtualScreenLeftSystemMetricIndex);
        var top = Win32.GetSystemMetrics(VirtualScreenTopSystemMetricIndex);
        var width = Win32.GetSystemMetrics(VirtualScreenWidthSystemMetricIndex);
        var height = Win32.GetSystemMetrics(VirtualScreenHeightSystemMetricIndex);
        if (width <= 0 || height <= 0) throw new InvalidOperationException("Unable to retrieve the virtual screen bounds.");

        return new(left, top, left + width, top + height);
    }

    private static ScreenRectangle CreateScreenRectangle(Win32.NativeRectangle nativeRectangle) => new(nativeRectangle.Left, nativeRectangle.Top, nativeRectangle.Right, nativeRectangle.Bottom);

    private static Win32.NativeInput CreateMouseInput(uint flags) => new()
    {
        Type = Win32.InputMouse,
        Data = new Win32.NativeInputUnion
        {
            MouseInput = new Win32.NativeMouseInput
            {
                Flags = flags
            }
        }
    };

    private static InvalidOperationException CreateMouseModifierButtonConsumeException(InputTriggerType[] requestedInputTriggerTypes, MouseButtonSnapshot mouseButtonSnapshotBeforeConsume, string pressedMouseButtonStateSummaryBeforeConsume, List<Win32.NativeInput> mouseInputs, uint sentInputCount, int nativeInputSize)
    {
        var lastWindowsErrorCode = Marshal.GetLastWin32Error();
        var lastWindowsErrorMessage = new Win32Exception(lastWindowsErrorCode).Message;
        var mouseButtonSnapshotAfterConsume = GetMouseButtonSnapshot();
        var pressedMouseButtonStateSummaryAfterConsume = CreatePressedMouseButtonStateSummary();
        var foregroundWindowSummary = CreateForegroundWindowSummary();
        var mouseInputSummaries = string.Join(", ", mouseInputs.Select(CreateMouseInputSummary));
        return new($"Unable to consume the pressed mouse modifier input. RequestedInputTriggers={string.Join("|", requestedInputTriggerTypes)}, MouseButtonSnapshotBeforeConsume={mouseButtonSnapshotBeforeConsume}, MouseButtonSnapshotAfterConsume={mouseButtonSnapshotAfterConsume}, PressedMouseButtonStatesBeforeConsume=[{pressedMouseButtonStateSummaryBeforeConsume}], PressedMouseButtonStatesAfterConsume=[{pressedMouseButtonStateSummaryAfterConsume}], RequestedInputCount={mouseInputs.Count}, SentInputCount={sentInputCount}, NativeInputSize={nativeInputSize}, LastWindowsErrorCode={lastWindowsErrorCode} (0x{lastWindowsErrorCode:X8}, {lastWindowsErrorMessage}), ForegroundWindow=[{foregroundWindowSummary}], MouseInputs=[{mouseInputSummaries}].");
    }

    private static string CreateMouseInputSummary(Win32.NativeInput mouseInput)
    {
        var mouseInputData = mouseInput.Data.MouseInput;
        return $"RelativeX={mouseInputData.RelativeX}, RelativeY={mouseInputData.RelativeY}, MouseData=0x{mouseInputData.MouseData:X8}, Flags=0x{mouseInputData.Flags:X4}, Time={mouseInputData.Time}, ExtraInfo={mouseInputData.ExtraInfo}";
    }

    private static string CreatePressedMouseButtonStateSummary() => string.Join(", ", [$"LeftButton={IsVirtualKeyPressed(LeftMouseButtonVirtualKey)}", $"MiddleButton={IsVirtualKeyPressed(MiddleMouseButtonVirtualKey)}", $"RightButton={IsVirtualKeyPressed(RightMouseButtonVirtualKey)}"]);

    private static string CreateForegroundWindowSummary()
    {
        var foregroundWindowHandle = Win32.GetForegroundWindow();
        if (foregroundWindowHandle == 0) return "WindowHandle=0";

        var foregroundThreadIdentifier = Win32.GetWindowThreadProcessId(foregroundWindowHandle, out var foregroundProcessIdentifier);
        var foregroundProcessSnapshot = GetForegroundProcessSnapshot();
        return $"WindowHandle={foregroundWindowHandle}, ThreadIdentifier={foregroundThreadIdentifier}, ProcessIdentifier={foregroundProcessIdentifier}, ProcessName={foregroundProcessSnapshot.ProcessName ?? "<null>"}, ExecutablePath={foregroundProcessSnapshot.ExecutablePath ?? "<null>"}";
    }

    private static int GetIntersectionArea(ScreenRectangle firstRectangle, ScreenRectangle secondRectangle)
    {
        var left = Math.Max(firstRectangle.Left, secondRectangle.Left);
        var top = Math.Max(firstRectangle.Top, secondRectangle.Top);
        var right = Math.Min(firstRectangle.Right, secondRectangle.Right);
        var bottom = Math.Min(firstRectangle.Bottom, secondRectangle.Bottom);
        return right <= left || bottom <= top ? 0 : (right - left) * (bottom - top);
    }

    private static uint GetMouseButtonUpEventFlag(InputTriggerType inputTriggerType) => inputTriggerType switch
    {
        InputTriggerType.MouseLeftButton => Win32.MouseEventLeftUpFlag,
        InputTriggerType.MouseMiddleButton => Win32.MouseEventMiddleUpFlag,
        InputTriggerType.MouseRightButton => Win32.MouseEventRightUpFlag,
        _ => throw new InvalidOperationException("The requested input trigger is not a mouse modifier button.")
    };

    private static bool IsMouseModifierButtonPressed(InputTriggerType inputTriggerType, MouseButtonSnapshot mouseButtonSnapshot) => inputTriggerType switch
    {
        InputTriggerType.MouseLeftButton => mouseButtonSnapshot.IsLeftButtonPressed,
        InputTriggerType.MouseMiddleButton => mouseButtonSnapshot.IsMiddleButtonPressed,
        InputTriggerType.MouseRightButton => mouseButtonSnapshot.IsRightButtonPressed,
        _ => false
    };

    private static bool IsVirtualKeyPressed(int virtualKey) => (Win32.GetAsyncKeyState(virtualKey) & Win32.AsyncKeyDownMask) == Win32.AsyncKeyDownMask;

    private sealed class MonitorEnumerationContext(List<DisplayMonitorInfo> displayMonitorInfos)
    {
        public List<DisplayMonitorInfo> DisplayMonitorInfos { get; } = displayMonitorInfos;

        public Exception? Exception { get; set; }
    }
}
