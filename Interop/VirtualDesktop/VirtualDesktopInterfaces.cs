using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DeskBorder.Interop.VirtualDesktop;

internal enum AdjacentVirtualDesktopDirection
{
    Left = 3,
    Right = 4,
}

internal enum VirtualDesktopApiVersion
{
    Windows10,
    Windows11,
    Windows11Version24H2OrGreater,
}

internal readonly record struct VirtualDesktopIdentifier(Guid Value)
{
    public override string ToString() => Value.ToString("D");

    public static implicit operator Guid(VirtualDesktopIdentifier virtualDesktopIdentifier) => virtualDesktopIdentifier.Value;
    public static implicit operator VirtualDesktopIdentifier(Guid value) => new(value);
}

internal readonly record struct VirtualDesktopHandle(VirtualDesktopApiVersion VirtualDesktopApiVersion, object NativeVirtualDesktop);

[GeneratedComInterface]
[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface INativeServiceProvider
{
    [PreserveSig]
    int QueryService(in Guid serviceIdentifier, in Guid interfaceIdentifier, out nint objectPointer);
}

[GeneratedComInterface]
[Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IObjectArray
{
    [PreserveSig]
    int GetCount(out int objectCount);

    [PreserveSig]
    int GetAt(int index, in Guid interfaceIdentifier, out nint objectPointer);
}

[GeneratedComInterface]
[Guid("372E1D3B-38D3-42E4-A15B-8AB2B178F513")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IApplicationView
{
}

[GeneratedComInterface]
[Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IApplicationViewCollection
{
    [PreserveSig]
    int GetViews(out IObjectArray applicationViews);

    [PreserveSig]
    int GetViewsByZOrder(out IObjectArray applicationViews);

    [PreserveSig]
    int GetViewsByAppUserModelId([MarshalAs(UnmanagedType.LPWStr)] string applicationUserModelIdentifier, out IObjectArray applicationViews);

    [PreserveSig]
    int GetViewForHwnd(nint windowHandle, out IApplicationView applicationView);
}

[GeneratedComInterface]
[Guid("FF72FFDD-BE7E-43FC-9C03-AD81681E88E4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IWindows10VirtualDesktop
{
    [PreserveSig]
    int IsViewVisible(IApplicationView applicationView, [MarshalAs(UnmanagedType.Bool)] out bool isVisible);

    [PreserveSig]
    int GetId(out Guid desktopIdentifier);
}

[GeneratedComInterface]
[Guid("3F07F4BE-B107-441A-AF0F-39D82529072C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IWindows11VirtualDesktop
{
    [PreserveSig]
    int IsViewVisible(IApplicationView applicationView, [MarshalAs(UnmanagedType.Bool)] out bool isVisible);

    [PreserveSig]
    int GetId(out Guid desktopIdentifier);
}

[GeneratedComInterface]
[Guid("F31574D6-B682-4CDC-BD56-1827860ABEC6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IWindows10VirtualDesktopManagerInternal
{
    [PreserveSig]
    int GetCount(out int desktopCount);

    [PreserveSig]
    int MoveViewToDesktop(IApplicationView applicationView, IWindows10VirtualDesktop virtualDesktop);

    [PreserveSig]
    int CanViewMoveDesktops(IApplicationView applicationView, [MarshalAs(UnmanagedType.Bool)] out bool canMoveView);

    [PreserveSig]
    int GetCurrentDesktop(out IWindows10VirtualDesktop virtualDesktop);

    [PreserveSig]
    int GetDesktops(out IObjectArray desktops);

    [PreserveSig]
    int GetAdjacentDesktop(IWindows10VirtualDesktop referenceDesktop, AdjacentVirtualDesktopDirection direction, out IWindows10VirtualDesktop virtualDesktop);

    [PreserveSig]
    int SwitchDesktop(IWindows10VirtualDesktop virtualDesktop);

    [PreserveSig]
    int CreateDesktop(out IWindows10VirtualDesktop virtualDesktop);

    [PreserveSig]
    int MoveDesktop(IWindows10VirtualDesktop virtualDesktop, int desktopIndex);

    [PreserveSig]
    int RemoveDesktop(IWindows10VirtualDesktop removeDesktop, IWindows10VirtualDesktop fallbackDesktop);

    [PreserveSig]
    int FindDesktop(in Guid desktopIdentifier, out IWindows10VirtualDesktop virtualDesktop);
}

[GeneratedComInterface]
[Guid("53F5CA0B-158F-4124-900C-057158060B27")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IWindows11VirtualDesktopManagerInternal
{
    [PreserveSig]
    int GetCount(out int desktopCount);

    [PreserveSig]
    int MoveViewToDesktop(IApplicationView applicationView, IWindows11VirtualDesktop virtualDesktop);

    [PreserveSig]
    int CanViewMoveDesktops(IApplicationView applicationView, [MarshalAs(UnmanagedType.Bool)] out bool canMoveView);

    [PreserveSig]
    int GetCurrentDesktop(out IWindows11VirtualDesktop virtualDesktop);

    [PreserveSig]
    int GetDesktops(out IObjectArray desktops);

    [PreserveSig]
    int GetAdjacentDesktop(IWindows11VirtualDesktop referenceDesktop, AdjacentVirtualDesktopDirection direction, out IWindows11VirtualDesktop virtualDesktop);

    [PreserveSig]
    int SwitchDesktop(IWindows11VirtualDesktop virtualDesktop);

    [PreserveSig]
    int CreateDesktop(out IWindows11VirtualDesktop virtualDesktop);

    [PreserveSig]
    int MoveDesktop(IWindows11VirtualDesktop virtualDesktop, int desktopIndex);

    [PreserveSig]
    int RemoveDesktop(IWindows11VirtualDesktop removeDesktop, IWindows11VirtualDesktop fallbackDesktop);

    [PreserveSig]
    int FindDesktop(in Guid desktopIdentifier, out IWindows11VirtualDesktop virtualDesktop);
}

[GeneratedComInterface]
[Guid("53F5CA0B-158F-4124-900C-057158060B27")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IWindows11Version24H2OrGreaterVirtualDesktopManagerInternal
{
    [PreserveSig]
    int GetCount(out int desktopCount);

    [PreserveSig]
    int MoveViewToDesktop(IApplicationView applicationView, IWindows11VirtualDesktop virtualDesktop);

    [PreserveSig]
    int CanViewMoveDesktops(IApplicationView applicationView, [MarshalAs(UnmanagedType.Bool)] out bool canMoveView);

    [PreserveSig]
    int GetCurrentDesktop(out IWindows11VirtualDesktop virtualDesktop);

    [PreserveSig]
    int GetDesktops(out IObjectArray desktops);

    [PreserveSig]
    int GetAdjacentDesktop(IWindows11VirtualDesktop referenceDesktop, AdjacentVirtualDesktopDirection direction, out IWindows11VirtualDesktop virtualDesktop);

    [PreserveSig]
    int SwitchDesktop(IWindows11VirtualDesktop virtualDesktop);

    [PreserveSig]
    int SwitchDesktopAndMoveForegroundView(IWindows11VirtualDesktop virtualDesktop);

    [PreserveSig]
    int CreateDesktop(out IWindows11VirtualDesktop virtualDesktop);

    [PreserveSig]
    int MoveDesktop(IWindows11VirtualDesktop virtualDesktop, int desktopIndex);

    [PreserveSig]
    int RemoveDesktop(IWindows11VirtualDesktop removeDesktop, IWindows11VirtualDesktop fallbackDesktop);

    [PreserveSig]
    int FindDesktop(in Guid desktopIdentifier, out IWindows11VirtualDesktop virtualDesktop);
}

[GeneratedComInterface]
[Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IVirtualDesktopManager
{
    [PreserveSig]
    int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool isWindowOnCurrentVirtualDesktop);

    [PreserveSig]
    int GetWindowDesktopId(nint topLevelWindow, out Guid desktopIdentifier);

    [PreserveSig]
    int MoveWindowToDesktop(nint topLevelWindow, in Guid desktopIdentifier);
}
