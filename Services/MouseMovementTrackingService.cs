using DeskBorder.Interop;
using System.Runtime.InteropServices;

namespace DeskBorder.Services;

public sealed class MouseMovementTrackingService(IFileLogService fileLogService) : IMouseMovementTrackingService
{
    private readonly IFileLogService _fileLogService = fileLogService;
    private int _pendingHorizontalMovement;
    private int _pendingVerticalMovement;
    private nint _registeredWindowHandle;
    private bool _isRawInputTrackingEnabled;

    public bool IsRawInputRegistered { get; private set; }

    public MouseMovementDelta ConsumePendingMouseMovementDelta() => new(Interlocked.Exchange(ref _pendingHorizontalMovement, 0), Interlocked.Exchange(ref _pendingVerticalMovement, 0));

    public void ProcessRawInputMessage(nint rawInputHandle)
    {
        if (!IsRawInputRegistered) return;

        var rawInput = GetRawInput(rawInputHandle);
        if (rawInput is null || rawInput.Value.Header.Type != Win32.RawInputTypeMouse || (rawInput.Value.Mouse.Flags & Win32.RawMouseMoveAbsoluteFlag) != 0 || (rawInput.Value.Mouse.LastX == 0 && rawInput.Value.Mouse.LastY == 0)) return;

        if (rawInput.Value.Mouse.LastX != 0) _ = Interlocked.Add(ref _pendingHorizontalMovement, rawInput.Value.Mouse.LastX);

        if (rawInput.Value.Mouse.LastY != 0) _ = Interlocked.Add(ref _pendingVerticalMovement, rawInput.Value.Mouse.LastY);
    }

    public void RegisterWindowHandle(nint windowHandle)
    {
        if (_registeredWindowHandle == windowHandle) return;

        if (IsRawInputRegistered) UnregisterRawInput();

        _registeredWindowHandle = windowHandle;
        _fileLogService.WriteInformation(nameof(MouseMovementTrackingService), $"Registered mouse movement tracking window handle 0x{windowHandle:X}.");
        if (_isRawInputTrackingEnabled) RegisterRawInput();
    }

    public void SetRawInputTrackingEnabled(bool isEnabled)
    {
        if (_isRawInputTrackingEnabled == isEnabled) return;

        _isRawInputTrackingEnabled = isEnabled;
        if (isEnabled) RegisterRawInput();
        else UnregisterRawInput();
    }

    private void RegisterRawInput()
    {
        if (IsRawInputRegistered) return;

        if (_registeredWindowHandle == 0)
        {
            _fileLogService.WriteWarning(nameof(MouseMovementTrackingService), "Skipped raw mouse input registration because no window handle is registered.");
            return;
        }

        Win32.NativeRawInputDevice[] rawInputDevices =
        [
            new()
            {
                UsagePage = Win32.GenericDesktopControlsUsagePage,
                Usage = Win32.GenericDesktopMouseUsage,
                Flags = Win32.RawInputDeviceInputSinkFlag,
                WindowHandle = _registeredWindowHandle
            }
        ];
        if (!Win32.RegisterRawInputDevices(rawInputDevices, (uint)rawInputDevices.Length, (uint)Marshal.SizeOf<Win32.NativeRawInputDevice>()))
        {
            _fileLogService.WriteWarning(nameof(MouseMovementTrackingService), $"Failed to register raw mouse input. Win32Error={Marshal.GetLastWin32Error()}.");
            return;
        }

        IsRawInputRegistered = true;
        _fileLogService.WriteInformation(nameof(MouseMovementTrackingService), $"Enabled raw mouse input for window handle 0x{_registeredWindowHandle:X}.");
    }

    private void UnregisterRawInput()
    {
        ResetPendingMouseMovementDelta();
        if (!IsRawInputRegistered) return;

        Win32.NativeRawInputDevice[] rawInputDevices =
        [
            new()
            {
                UsagePage = Win32.GenericDesktopControlsUsagePage,
                Usage = Win32.GenericDesktopMouseUsage,
                Flags = Win32.RawInputDeviceRemoveFlag,
                WindowHandle = 0
            }
        ];
        if (!Win32.RegisterRawInputDevices(rawInputDevices, (uint)rawInputDevices.Length, (uint)Marshal.SizeOf<Win32.NativeRawInputDevice>()))
        {
            _fileLogService.WriteWarning(nameof(MouseMovementTrackingService), $"Failed to unregister raw mouse input. Win32Error={Marshal.GetLastWin32Error()}.");
            return;
        }

        IsRawInputRegistered = false;
        _fileLogService.WriteInformation(nameof(MouseMovementTrackingService), "Disabled raw mouse input.");
    }

    private void ResetPendingMouseMovementDelta()
    {
        _ = Interlocked.Exchange(ref _pendingHorizontalMovement, 0);
        _ = Interlocked.Exchange(ref _pendingVerticalMovement, 0);
    }

    private static Win32.NativeRawInput? GetRawInput(nint rawInputHandle)
    {
        var rawInputHeaderSize = (uint)Marshal.SizeOf<Win32.NativeRawInputHeader>();
        uint rawInputSize = 0;
        if (Win32.GetRawInputData(rawInputHandle, Win32.RawInputDataCommandInput, 0, ref rawInputSize, rawInputHeaderSize) == uint.MaxValue || rawInputSize == 0) return null;

        var rawInputBuffer = Marshal.AllocHGlobal((int)rawInputSize);
        try
        {
            if (Win32.GetRawInputData(rawInputHandle, Win32.RawInputDataCommandInput, rawInputBuffer, ref rawInputSize, rawInputHeaderSize) == uint.MaxValue) return null;

            return Marshal.PtrToStructure<Win32.NativeRawInput>(rawInputBuffer);
        }
        finally { Marshal.FreeHGlobal(rawInputBuffer); }
    }
}
