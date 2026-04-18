using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DeskBorder.Interop.VirtualDesktop;

internal static partial class VirtualDesktopFoundation
{
    private const uint ClassContextInProcessServer = 0x00000001;
    private const uint ClassContextLocalServer = 0x00000004;
    private const uint ClassContextRemoteServer = 0x00000010;
    private const uint ClassContextAll = ClassContextInProcessServer | ClassContextLocalServer | ClassContextRemoteServer;
    private const int TypeElementNotFoundHresult = unchecked((int)0x8002802B);
    private const int AdjacentDesktopNotFoundHresult = unchecked((int)0x80028CA1);

    private static readonly Guid s_immersiveShellClassIdentifier = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid s_virtualDesktopManagerClassIdentifier = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");
    private static readonly Guid s_virtualDesktopManagerInternalServiceIdentifier = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
    private static readonly Guid s_applicationViewCollectionServiceIdentifier = new("1841C6D7-4F9D-42C0-AF41-8747538F10E5");

    public static VirtualDesktopShell Connect()
    {
        var nativeServiceProvider = CreateInstance<INativeServiceProvider>(s_immersiveShellClassIdentifier);
        return new VirtualDesktopShell(
            nativeServiceProvider,
            CreateInstance<IVirtualDesktopManager>(s_virtualDesktopManagerClassIdentifier),
            QueryService<IVirtualDesktopManagerInternal>(nativeServiceProvider, s_virtualDesktopManagerInternalServiceIdentifier),
            QueryService<IApplicationViewCollection>(nativeServiceProvider, s_applicationViewCollectionServiceIdentifier));
    }

    public static TInterface CreateInstance<TInterface>(Guid classIdentifier) where TInterface : class
    {
        var interfaceIdentifier = typeof(TInterface).GUID;
        ThrowIfFailed(CoCreateInstance(in classIdentifier, 0, ClassContextAll, in interfaceIdentifier, out var objectPointer));
        return ConvertToManaged<TInterface>(objectPointer);
    }

    public static TInterface QueryService<TInterface>(INativeServiceProvider nativeServiceProvider, Guid serviceIdentifier) where TInterface : class
    {
        var interfaceIdentifier = typeof(TInterface).GUID;
        ThrowIfFailed(nativeServiceProvider.QueryService(in serviceIdentifier, in interfaceIdentifier, out var objectPointer));
        return ConvertToManaged<TInterface>(objectPointer);
    }

    public static int GetDesktopCount(IVirtualDesktopManagerInternal virtualDesktopManagerInternal)
    {
        ThrowIfFailed(virtualDesktopManagerInternal.GetCount(out var desktopCount));
        return desktopCount;
    }

    public static IApplicationView GetApplicationView(IApplicationViewCollection applicationViewCollection, nint windowHandle)
    {
        ThrowIfFailed(applicationViewCollection.GetViewForHwnd(windowHandle, out var applicationView));
        return applicationView;
    }

    public static bool TryGetApplicationView(IApplicationViewCollection applicationViewCollection, nint windowHandle, out IApplicationView? applicationView)
    {
        var hresult = applicationViewCollection.GetViewForHwnd(windowHandle, out var nativeApplicationView);
        if (hresult == TypeElementNotFoundHresult)
        {
            applicationView = null;
            return false;
        }

        ThrowIfFailed(hresult);
        applicationView = nativeApplicationView;
        return true;
    }

    public static IReadOnlyList<IVirtualDesktop> GetDesktops(IVirtualDesktopManagerInternal virtualDesktopManagerInternal)
    {
        ThrowIfFailed(virtualDesktopManagerInternal.GetDesktops(out var objectArray));
        var desktopCount = GetObjectCount(objectArray);
        var virtualDesktops = new List<IVirtualDesktop>((int)desktopCount);

        for (var index = 0; index < desktopCount; index++)
            virtualDesktops.Add(GetObjectAt<IVirtualDesktop>(objectArray, index));

        return virtualDesktops;
    }

    public static IVirtualDesktop GetCurrentDesktop(IVirtualDesktopManagerInternal virtualDesktopManagerInternal)
    {
        ThrowIfFailed(virtualDesktopManagerInternal.GetCurrentDesktop(out var virtualDesktop));
        return virtualDesktop;
    }

    public static IVirtualDesktop GetAdjacentDesktop(IVirtualDesktopManagerInternal virtualDesktopManagerInternal, IVirtualDesktop referenceDesktop, AdjacentVirtualDesktopDirection direction)
    {
        ThrowIfFailed(virtualDesktopManagerInternal.GetAdjacentDesktop(referenceDesktop, direction, out var virtualDesktop));
        return virtualDesktop;
    }

    public static bool TryGetAdjacentDesktop(IVirtualDesktopManagerInternal virtualDesktopManagerInternal, IVirtualDesktop referenceDesktop, AdjacentVirtualDesktopDirection direction, out IVirtualDesktop? virtualDesktop)
    {
        var hresult = virtualDesktopManagerInternal.GetAdjacentDesktop(referenceDesktop, direction, out var nativeVirtualDesktop);
        if (hresult == AdjacentDesktopNotFoundHresult)
        {
            virtualDesktop = null;
            return false;
        }

        ThrowIfFailed(hresult);
        virtualDesktop = nativeVirtualDesktop;
        return true;
    }

    public static IVirtualDesktop CreateDesktop(IVirtualDesktopManagerInternal virtualDesktopManagerInternal)
    {
        ThrowIfFailed(virtualDesktopManagerInternal.CreateDesktop(out var virtualDesktop));
        return virtualDesktop;
    }

    public static IVirtualDesktop FindDesktop(IVirtualDesktopManagerInternal virtualDesktopManagerInternal, VirtualDesktopIdentifier desktopIdentifier)
    {
        ThrowIfFailed(virtualDesktopManagerInternal.FindDesktop(desktopIdentifier, out var virtualDesktop));
        return virtualDesktop;
    }

    public static bool TryFindDesktop(IVirtualDesktopManagerInternal virtualDesktopManagerInternal, VirtualDesktopIdentifier desktopIdentifier, out IVirtualDesktop? virtualDesktop)
    {
        var hresult = virtualDesktopManagerInternal.FindDesktop(desktopIdentifier, out var nativeVirtualDesktop);
        if (hresult == TypeElementNotFoundHresult)
        {
            virtualDesktop = null;
            return false;
        }

        ThrowIfFailed(hresult);
        virtualDesktop = nativeVirtualDesktop;
        return true;
    }

    public static void SwitchDesktop(IVirtualDesktopManagerInternal virtualDesktopManagerInternal, IVirtualDesktop virtualDesktop) => ThrowIfFailed(virtualDesktopManagerInternal.SwitchDesktop(virtualDesktop));

    public static void RemoveDesktop(IVirtualDesktopManagerInternal virtualDesktopManagerInternal, IVirtualDesktop removeDesktop, IVirtualDesktop fallbackDesktop) => ThrowIfFailed(virtualDesktopManagerInternal.RemoveDesktop(removeDesktop, fallbackDesktop));

    public static void MoveViewToDesktop(IVirtualDesktopManagerInternal virtualDesktopManagerInternal, IApplicationView applicationView, IVirtualDesktop virtualDesktop) => ThrowIfFailed(virtualDesktopManagerInternal.MoveViewToDesktop(applicationView, virtualDesktop));

    public static bool CanMoveViewBetweenDesktops(IVirtualDesktopManagerInternal virtualDesktopManagerInternal, IApplicationView applicationView)
    {
        ThrowIfFailed(virtualDesktopManagerInternal.CanViewMoveDesktops(applicationView, out var canMoveView));
        return canMoveView;
    }

    public static bool IsWindowOnCurrentDesktop(IVirtualDesktopManager virtualDesktopManager, nint windowHandle)
    {
        ThrowIfFailed(virtualDesktopManager.IsWindowOnCurrentVirtualDesktop(windowHandle, out var isWindowOnCurrentDesktop));
        return isWindowOnCurrentDesktop;
    }

    public static bool TryIsWindowOnCurrentDesktop(IVirtualDesktopManager virtualDesktopManager, nint windowHandle, out bool isWindowOnCurrentDesktop)
    {
        var hresult = virtualDesktopManager.IsWindowOnCurrentVirtualDesktop(windowHandle, out var nativeIsWindowOnCurrentDesktop);
        if (hresult == TypeElementNotFoundHresult)
        {
            isWindowOnCurrentDesktop = false;
            return false;
        }

        ThrowIfFailed(hresult);
        isWindowOnCurrentDesktop = nativeIsWindowOnCurrentDesktop;
        return true;
    }

    public static VirtualDesktopIdentifier GetWindowDesktopIdentifier(IVirtualDesktopManager virtualDesktopManager, nint windowHandle)
    {
        ThrowIfFailed(virtualDesktopManager.GetWindowDesktopId(windowHandle, out var desktopIdentifier));
        return desktopIdentifier;
    }

    public static bool TryGetWindowDesktopIdentifier(IVirtualDesktopManager virtualDesktopManager, nint windowHandle, out VirtualDesktopIdentifier desktopIdentifier)
    {
        var hresult = virtualDesktopManager.GetWindowDesktopId(windowHandle, out var nativeDesktopIdentifier);
        if (hresult == TypeElementNotFoundHresult)
        {
            desktopIdentifier = default;
            return false;
        }

        ThrowIfFailed(hresult);
        desktopIdentifier = nativeDesktopIdentifier;
        return true;
    }

    public static void MoveWindowToDesktop(IVirtualDesktopManager virtualDesktopManager, nint windowHandle, VirtualDesktopIdentifier desktopIdentifier) => ThrowIfFailed(virtualDesktopManager.MoveWindowToDesktop(windowHandle, desktopIdentifier));

    public static VirtualDesktopIdentifier GetDesktopIdentifier(IVirtualDesktop virtualDesktop)
    {
        ThrowIfFailed(virtualDesktop.GetId(out var desktopIdentifier));
        return desktopIdentifier;
    }

    public static bool IsViewVisibleOnDesktop(IVirtualDesktop virtualDesktop, IApplicationView applicationView)
    {
        ThrowIfFailed(virtualDesktop.IsViewVisible(applicationView, out var isVisible));
        return isVisible;
    }

    private static int GetObjectCount(IObjectArray objectArray)
    {
        ThrowIfFailed(objectArray.GetCount(out var objectCount));
        return objectCount;
    }

    private static TInterface GetObjectAt<TInterface>(IObjectArray objectArray, int index) where TInterface : class
    {
        var interfaceIdentifier = typeof(TInterface).GUID;
        ThrowIfFailed(objectArray.GetAt(index, in interfaceIdentifier, out var objectPointer));
        return ConvertToManaged<TInterface>(objectPointer);
    }

    private static unsafe TInterface ConvertToManaged<TInterface>(nint objectPointer) where TInterface : class
    {
        if (objectPointer == 0)
            throw new InvalidOperationException("The COM interface pointer is null.");

        return ComInterfaceMarshaller<TInterface>.ConvertToManaged((void*)objectPointer)
            ?? throw new InvalidOperationException("The COM interface pointer could not be converted to a managed interface.");
    }

    private static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0)
            throw new COMException($"The COM interop call failed with HRESULT 0x{hresult:X8}.", hresult);
    }

    [LibraryImport("ole32.dll", SetLastError = true)]
    private static partial int CoCreateInstance(in Guid classIdentifier, nint outerUnknownPointer, uint classContext, in Guid interfaceIdentifier, out nint objectPointer);
}
