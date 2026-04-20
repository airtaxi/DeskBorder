using DeskBorder.Interop;
using System.Runtime.InteropServices;

namespace DeskBorder.Services;

public sealed class MouseMovementTrackingService(IFileLogService fileLogService) : IMouseMovementTrackingService
{
    private readonly IFileLogService _fileLogService = fileLogService;
    private int _pendingHorizontalMovement;
    private nint _registeredWindowHandle;

    public int ConsumePendingHorizontalMovement() => Interlocked.Exchange(ref _pendingHorizontalMovement, 0);

    public void ProcessRawInputMessage(nint rawInputHandle)
    {
        var rawInput = GetRawInput(rawInputHandle);
        if (rawInput is null
            || rawInput.Value.Header.Type != Win32.RawInputTypeMouse
            || (rawInput.Value.Mouse.Flags & Win32.RawMouseMoveAbsoluteFlag) != 0
            || rawInput.Value.Mouse.LastX == 0)
        {
            return;
        }

        _ = Interlocked.Add(ref _pendingHorizontalMovement, rawInput.Value.Mouse.LastX);
    }

    public void RegisterWindowHandle(nint windowHandle)
    {
        if (_registeredWindowHandle == windowHandle)
            return;

        Win32.NativeRawInputDevice[] rawInputDevices =
        [
            new()
            {
                UsagePage = Win32.GenericDesktopControlsUsagePage,
                Usage = Win32.GenericDesktopMouseUsage,
                Flags = Win32.RawInputDeviceInputSinkFlag,
                WindowHandle = windowHandle
            }
        ];
        if (!Win32.RegisterRawInputDevices(rawInputDevices, (uint)rawInputDevices.Length, (uint)Marshal.SizeOf<Win32.NativeRawInputDevice>()))
            throw new InvalidOperationException($"Failed to register raw mouse input. Win32Error={Marshal.GetLastWin32Error()}.");

        _registeredWindowHandle = windowHandle;
        _fileLogService.WriteInformation(nameof(MouseMovementTrackingService), $"Registered raw mouse input for window handle 0x{windowHandle:X}.");
    }

    private static Win32.NativeRawInput? GetRawInput(nint rawInputHandle)
    {
        var rawInputHeaderSize = (uint)Marshal.SizeOf<Win32.NativeRawInputHeader>();
        uint rawInputSize = 0;
        if (Win32.GetRawInputData(rawInputHandle, Win32.RawInputDataCommandInput, 0, ref rawInputSize, rawInputHeaderSize) == uint.MaxValue || rawInputSize == 0)
            return null;

        var rawInputBuffer = Marshal.AllocHGlobal((int)rawInputSize);
        try
        {
            if (Win32.GetRawInputData(rawInputHandle, Win32.RawInputDataCommandInput, rawInputBuffer, ref rawInputSize, rawInputHeaderSize) == uint.MaxValue)
                return null;

            return Marshal.PtrToStructure<Win32.NativeRawInput>(rawInputBuffer);
        }
        finally
        {
            Marshal.FreeHGlobal(rawInputBuffer);
        }
    }
}
