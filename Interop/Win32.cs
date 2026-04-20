using System.Runtime.InteropServices;

namespace DeskBorder.Interop;

internal static partial class Win32
{
    public const short AsyncKeyDownMask = unchecked((short)0x8000);
    public const int InputKeyboard = 1;
    public const int ShowWindowRestore = 9;
    public const int LowLevelKeyboardHookId = 13;
    public const ushort VirtualKeyF24 = 0x87;
    public const uint KeyboardEventExtendedKeyFlag = 0x0001;
    public const uint MonitorInfoPrimary = 0x00000001;
    public const uint KeyboardEventKeyUpFlag = 0x0002;
    public const uint LowLevelKeyboardHookInjectedFlag = 0x00000010;
    public const uint PeekMessageNoRemove = 0x0000;
    public const nuint KeyUpWindowMessage = 0x0101;
    public const nuint SystemKeyUpWindowMessage = 0x0105;
    public const uint WindowApplicationMessage = 0x8000;
    public const uint WindowInputMessage = 0x00FF;
    public const uint WindowHotkeyMessage = 0x0312;
    public const uint WindowGetIconMessage = 0x007F;
    public const uint RawInputDataCommandInput = 0x10000003;
    public const uint RawInputDeviceInputSinkFlag = 0x00000100;
    public const uint RawInputTypeMouse = 0;
    public const ushort GenericDesktopControlsUsagePage = 0x01;
    public const ushort GenericDesktopMouseUsage = 0x02;
    public const ushort RawMouseMoveAbsoluteFlag = 0x0001;
    public const uint DesktopWindowManagerCloakedAttribute = 14;
    public const uint DesktopWindowManagerExtendedFrameBoundsAttribute = 9;
    public const uint DesktopWindowManagerWindowCornerPreferenceAttribute = 33;
    public const uint GetAncestorRootOwnerFlag = 3;
    public const uint HotkeyModifierAlternate = 0x0001;
    public const uint HotkeyModifierControl = 0x0002;
    public const uint HotkeyModifierShift = 0x0004;
    public const uint HotkeyModifierWindows = 0x0008;
    public const uint HotkeyModifierNoRepeat = 0x4000;
    public const int ExtendedWindowStyleIndex = -20;
    public const int WindowIconSmall = 0;
    public const int WindowIconBig = 1;
    public const int WindowIconSmallSecondary = 2;
    public const int ClassLongPointerIcon = -14;
    public const int ClassLongPointerSmallIcon = -34;
    public const uint SetWindowPositionDoNotResizeFlag = 0x0001;
    public const uint SetWindowPositionDoNotMoveFlag = 0x0002;
    public const uint SetWindowPositionShowWindowFlag = 0x0040;
    public const int DesktopWindowManagerWindowCornerPreferenceDoNotRound = 1;
    public const nint ExtendedWindowStyleApplicationWindow = 0x00040000;
    public const nint ExtendedWindowStyleNoActivate = 0x08000000;
    public const nint ExtendedWindowStyleToolWindow = 0x00000080;
    public static readonly nint TopMostWindowInsertAfterHandle = -1;

    public delegate bool MonitorEnumerationProcedure(nint monitorHandle, nint deviceContextHandle, nint monitorRectanglePointer, nint applicationData);
    public delegate bool WindowEnumerationProcedure(nint windowHandle, nint applicationData);
    public delegate nint LowLevelKeyboardHookProcedure(int code, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClipCursor(out NativeRectangle clippingRectangle);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint windowHandle, out NativeRectangle windowRectangle);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint SetWindowsHookEx(int hookIdentifier, LowLevelKeyboardHookProcedure hookProcedure, nint moduleHandle, uint threadIdentifier);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hookHandle, int code, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hookHandle);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint windowHandle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BringWindowToTop(nint windowHandle);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPosition(nint windowHandle, nint insertAfterWindowHandle, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint windowHandle);

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint windowHandle, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint GetLastActivePopup(nint windowHandle);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static partial nint GetWindowLongPointer(nint windowHandle, int index);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint windowHandle, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetClassLongPtrW", SetLastError = true)]
    public static partial nint GetClassLongPointer(nint windowHandle, int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindowAsync(nint windowHandle, int showWindowCommand);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint attachFromThreadIdentifier, uint attachToThreadIdentifier, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("user32.dll")]
    public static partial nint GetShellWindow();

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int systemMetricIndex);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint windowHandle);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint CopyIcon(nint iconHandle);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint iconHandle);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint windowHandle, int identifier, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterRawInputDevices([In] NativeRawInputDevice[] rawInputDevices, uint deviceCount, uint rawInputDeviceSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetRawInputData(nint rawInputHandle, uint command, nint dataPointer, ref uint dataSize, uint headerSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint windowHandle, int identifier);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    public static partial int GetMessage(out NativeMessage nativeMessage, nint windowHandle, uint minimumFilter, uint maximumFilter);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PeekMessage(out NativeMessage nativeMessage, nint windowHandle, uint minimumFilter, uint maximumFilter, uint removeMessage);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessage(uint threadIdentifier, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayMonitors(nint deviceContextHandle, nint clippingRectanglePointer, MonitorEnumerationProcedure enumerationProcedure, nint applicationData);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(WindowEnumerationProcedure enumerationProcedure, nint applicationData);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint windowHandle);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processIdentifier);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(nint monitorHandle, ref MonitorInfoExtended monitorInfo);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    public static partial int DwmGetWindowRectangleAttribute(nint windowHandle, uint attribute, out NativeRectangle attributeValue, uint attributeSize);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    public static partial int DwmGetWindowInt32Attribute(nint windowHandle, uint attribute, out int attributeValue, uint attributeSize);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    public static partial int DwmSetWindowInt32Attribute(nint windowHandle, uint attribute, in int attributeValue, uint attributeSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeLowLevelKeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public readonly bool IsEmpty => Left >= Right || Top >= Bottom;
        public readonly bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeMessage
    {
        public nint WindowHandle;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeInput
    {
        public int Type;
        public NativeInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct NativeInputUnion
    {
        [FieldOffset(0)]
        public NativeKeyboardInput KeyboardInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint WindowHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRawInputHeader
    {
        public uint Type;
        public uint Size;
        public nint DeviceHandle;
        public nuint WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRawMouse
    {
        public ushort Flags;
        public uint Buttons;
        public uint RawButtons;
        public int LastX;
        public int LastY;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct NativeRawInputData
    {
        [FieldOffset(0)]
        public NativeRawMouse Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRawInput
    {
        public NativeRawInputHeader Header;
        public NativeRawInputData Data;

        public readonly NativeRawMouse Mouse => Data.Mouse;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public unsafe struct MonitorInfoExtended
    {
        private const int DeviceNameCharacterCapacity = 32;

        public uint Size;
        public NativeRectangle MonitorRectangle;
        public NativeRectangle WorkAreaRectangle;
        public uint Flags;

        public fixed char DeviceName[DeviceNameCharacterCapacity];

        public readonly bool IsPrimary => (Flags & MonitorInfoPrimary) != 0;
        public readonly string GetDeviceName()
        {
            fixed (char* deviceNamePointer = DeviceName)
                return new string(deviceNamePointer);
        }

        public static MonitorInfoExtended Create() => new() { Size = (uint)sizeof(MonitorInfoExtended) };
    }
}
