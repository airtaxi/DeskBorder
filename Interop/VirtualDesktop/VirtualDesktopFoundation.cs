using DeskBorder.Interop;
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
    private const int MinimumSupportedWindowsBuildNumber = 17763;
    private const int MinimumWindows11BuildNumber = 22000;
    private const int MinimumWindows11Version24H2BuildNumber = 26100;
    private const int TypeElementNotFoundHresult = unchecked((int)0x8002802B);
    private const int AdjacentDesktopNotFoundHresult = unchecked((int)0x80028CA1);

    private static readonly Guid s_immersiveShellClassIdentifier = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid s_virtualDesktopManagerClassIdentifier = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");
    private static readonly Guid s_virtualDesktopManagerInternalServiceIdentifier = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
    private static readonly Guid s_applicationViewCollectionServiceIdentifier = new("1841C6D7-4F9D-42C0-AF41-8747538F10E5");

    public static VirtualDesktopShell Connect()
    {
        var nativeServiceProvider = CreateInstance<INativeServiceProvider>(s_immersiveShellClassIdentifier);
        var virtualDesktopApiVersion = GetVirtualDesktopApiVersion();
        var virtualDesktopManager = CreateInstance<IVirtualDesktopManager>(s_virtualDesktopManagerClassIdentifier);
        var applicationViewCollection = QueryService<IApplicationViewCollection>(nativeServiceProvider, s_applicationViewCollectionServiceIdentifier);
        return virtualDesktopApiVersion switch
        {
            VirtualDesktopApiVersion.Windows10 => new(
                virtualDesktopApiVersion,
                nativeServiceProvider,
                virtualDesktopManager,
                applicationViewCollection,
                windows10VirtualDesktopManagerInternal: QueryService<IWindows10VirtualDesktopManagerInternal>(nativeServiceProvider, s_virtualDesktopManagerInternalServiceIdentifier)),
            VirtualDesktopApiVersion.Windows11 => new(
                virtualDesktopApiVersion,
                nativeServiceProvider,
                virtualDesktopManager,
                applicationViewCollection,
                windows11VirtualDesktopManagerInternal: QueryService<IWindows11VirtualDesktopManagerInternal>(nativeServiceProvider, s_virtualDesktopManagerInternalServiceIdentifier)),
            VirtualDesktopApiVersion.Windows11Version24H2OrGreater => new(
                virtualDesktopApiVersion,
                nativeServiceProvider,
                virtualDesktopManager,
                applicationViewCollection,
                windows11Version24H2OrGreaterVirtualDesktopManagerInternal: QueryService<IWindows11Version24H2OrGreaterVirtualDesktopManagerInternal>(nativeServiceProvider, s_virtualDesktopManagerInternalServiceIdentifier)),
            _ => throw new PlatformNotSupportedException("The current Windows version is not supported.")
        };
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

    public static int GetDesktopCount(VirtualDesktopShell virtualDesktopShell) => virtualDesktopShell.VirtualDesktopApiVersion switch
    {
        VirtualDesktopApiVersion.Windows10 => GetDesktopCount(GetWindows10VirtualDesktopManagerInternal(virtualDesktopShell)),
        VirtualDesktopApiVersion.Windows11 => GetDesktopCount(GetWindows11VirtualDesktopManagerInternal(virtualDesktopShell)),
        VirtualDesktopApiVersion.Windows11Version24H2OrGreater => GetDesktopCount(GetWindows11Version24H2OrGreaterVirtualDesktopManagerInternal(virtualDesktopShell)),
        _ => throw new PlatformNotSupportedException("The current Windows version is not supported.")
    };

    public static IApplicationView GetApplicationView(IApplicationViewCollection applicationViewCollection, nint windowHandle)
    {
        ThrowIfFailed(applicationViewCollection.GetViewForHwnd(windowHandle, out var applicationView));
        return applicationView;
    }

    public static bool TryGetApplicationView(IApplicationViewCollection applicationViewCollection, nint windowHandle, out IApplicationView? applicationView)
    {
        var applicationViewResultCode = applicationViewCollection.GetViewForHwnd(windowHandle, out var nativeApplicationView);
        if (applicationViewResultCode == TypeElementNotFoundHresult)
        {
            applicationView = null;
            return false;
        }

        ThrowIfFailed(applicationViewResultCode);
        applicationView = nativeApplicationView;
        return true;
    }

    public static IReadOnlyList<ApplicationViewSnapshot> GetApplicationViewSnapshots(IApplicationViewCollection applicationViewCollection)
    {
        ThrowIfFailed(applicationViewCollection.GetViewsByZOrder(out var applicationViews));
        var applicationViewCount = GetObjectCount(applicationViews);
        var applicationViewSnapshots = new List<ApplicationViewSnapshot>(applicationViewCount);
        var applicationViewInterfaceIdentifier = typeof(IApplicationView).GUID;
        for (var index = 0; index < applicationViewCount; index++)
        {
            var applicationViewPointer = GetObjectPointerAt(applicationViews, index, applicationViewInterfaceIdentifier);
            try
            {
                if (TryCreateApplicationViewSnapshot(applicationViewPointer, out var applicationViewSnapshot))
                    applicationViewSnapshots.Add(applicationViewSnapshot);
            }
            finally { _ = Marshal.Release(applicationViewPointer); }
        }

        return applicationViewSnapshots;
    }

    public static IReadOnlyList<VirtualDesktopHandle> GetDesktops(VirtualDesktopShell virtualDesktopShell) => virtualDesktopShell.VirtualDesktopApiVersion switch
    {
        VirtualDesktopApiVersion.Windows10 => GetWindows10VirtualDesktops(virtualDesktopShell),
        VirtualDesktopApiVersion.Windows11 => GetWindows11VirtualDesktops(virtualDesktopShell),
        VirtualDesktopApiVersion.Windows11Version24H2OrGreater => GetWindows11Version24H2OrGreaterVirtualDesktops(virtualDesktopShell),
        _ => throw new PlatformNotSupportedException("The current Windows version is not supported.")
    };

    public static VirtualDesktopHandle GetCurrentDesktop(VirtualDesktopShell virtualDesktopShell) => virtualDesktopShell.VirtualDesktopApiVersion switch
    {
        VirtualDesktopApiVersion.Windows10 => GetCurrentWindows10VirtualDesktop(virtualDesktopShell),
        VirtualDesktopApiVersion.Windows11 => GetCurrentWindows11VirtualDesktop(virtualDesktopShell),
        VirtualDesktopApiVersion.Windows11Version24H2OrGreater => GetCurrentWindows11Version24H2OrGreaterVirtualDesktop(virtualDesktopShell),
        _ => throw new PlatformNotSupportedException("The current Windows version is not supported.")
    };

    public static bool TryGetAdjacentDesktop(VirtualDesktopShell virtualDesktopShell, VirtualDesktopHandle referenceVirtualDesktop, AdjacentVirtualDesktopDirection direction, out VirtualDesktopHandle virtualDesktop)
    {
        switch (virtualDesktopShell.VirtualDesktopApiVersion)
        {
            case VirtualDesktopApiVersion.Windows10:
            {
                var adjacentDesktopResultCode = GetWindows10VirtualDesktopManagerInternal(virtualDesktopShell).GetAdjacentDesktop(GetWindows10VirtualDesktop(referenceVirtualDesktop), direction, out var nativeVirtualDesktop);
                if (adjacentDesktopResultCode == AdjacentDesktopNotFoundHresult)
                {
                    virtualDesktop = default;
                    return false;
                }

                ThrowIfFailed(adjacentDesktopResultCode);
                virtualDesktop = new(VirtualDesktopApiVersion.Windows10, nativeVirtualDesktop);
                return true;
            }

            case VirtualDesktopApiVersion.Windows11:
            {
                var adjacentDesktopResultCode = GetWindows11VirtualDesktopManagerInternal(virtualDesktopShell).GetAdjacentDesktop(GetWindows11VirtualDesktop(referenceVirtualDesktop), direction, out var nativeVirtualDesktop);
                if (adjacentDesktopResultCode == AdjacentDesktopNotFoundHresult)
                {
                    virtualDesktop = default;
                    return false;
                }

                ThrowIfFailed(adjacentDesktopResultCode);
                virtualDesktop = new(VirtualDesktopApiVersion.Windows11, nativeVirtualDesktop);
                return true;
            }

            case VirtualDesktopApiVersion.Windows11Version24H2OrGreater:
            {
                var adjacentDesktopResultCode = GetWindows11Version24H2OrGreaterVirtualDesktopManagerInternal(virtualDesktopShell).GetAdjacentDesktop(GetWindows11VirtualDesktop(referenceVirtualDesktop), direction, out var nativeVirtualDesktop);
                if (adjacentDesktopResultCode == AdjacentDesktopNotFoundHresult)
                {
                    virtualDesktop = default;
                    return false;
                }

                ThrowIfFailed(adjacentDesktopResultCode);
                virtualDesktop = new(VirtualDesktopApiVersion.Windows11Version24H2OrGreater, nativeVirtualDesktop);
                return true;
            }

            default:
                throw new PlatformNotSupportedException("The current Windows version is not supported.");
        }
    }

    public static VirtualDesktopHandle CreateDesktop(VirtualDesktopShell virtualDesktopShell) => virtualDesktopShell.VirtualDesktopApiVersion switch
    {
        VirtualDesktopApiVersion.Windows10 => CreateWindows10VirtualDesktop(virtualDesktopShell),
        VirtualDesktopApiVersion.Windows11 => CreateWindows11VirtualDesktop(virtualDesktopShell),
        VirtualDesktopApiVersion.Windows11Version24H2OrGreater => CreateWindows11Version24H2OrGreaterVirtualDesktop(virtualDesktopShell),
        _ => throw new PlatformNotSupportedException("The current Windows version is not supported.")
    };

    public static void MoveDesktop(VirtualDesktopShell virtualDesktopShell, VirtualDesktopHandle virtualDesktop, int desktopIndex)
    {
        switch (virtualDesktopShell.VirtualDesktopApiVersion)
        {
            case VirtualDesktopApiVersion.Windows10:
                ThrowIfFailed(GetWindows10VirtualDesktopManagerInternal(virtualDesktopShell).MoveDesktop(GetWindows10VirtualDesktop(virtualDesktop), desktopIndex));
                return;

            case VirtualDesktopApiVersion.Windows11:
                ThrowIfFailed(GetWindows11VirtualDesktopManagerInternal(virtualDesktopShell).MoveDesktop(GetWindows11VirtualDesktop(virtualDesktop), desktopIndex));
                return;

            case VirtualDesktopApiVersion.Windows11Version24H2OrGreater:
                ThrowIfFailed(GetWindows11Version24H2OrGreaterVirtualDesktopManagerInternal(virtualDesktopShell).MoveDesktop(GetWindows11VirtualDesktop(virtualDesktop), desktopIndex));
                return;

            default:
                throw new PlatformNotSupportedException("The current Windows version is not supported.");
        }
    }

    public static void SwapDesktops(VirtualDesktopShell virtualDesktopShell, VirtualDesktopHandle firstVirtualDesktop, int firstDesktopIndex, VirtualDesktopHandle secondVirtualDesktop, int secondDesktopIndex)
    {
        if (firstDesktopIndex == secondDesktopIndex)
            return;

        if (firstDesktopIndex > secondDesktopIndex)
        {
            (firstVirtualDesktop, secondVirtualDesktop) = (secondVirtualDesktop, firstVirtualDesktop);
            (firstDesktopIndex, secondDesktopIndex) = (secondDesktopIndex, firstDesktopIndex);
        }

        MoveDesktop(virtualDesktopShell, firstVirtualDesktop, secondDesktopIndex);
        MoveDesktop(virtualDesktopShell, secondVirtualDesktop, firstDesktopIndex);
    }

    public static bool TryFindDesktop(VirtualDesktopShell virtualDesktopShell, VirtualDesktopIdentifier desktopIdentifier, out VirtualDesktopHandle virtualDesktop)
    {
        switch (virtualDesktopShell.VirtualDesktopApiVersion)
        {
            case VirtualDesktopApiVersion.Windows10:
            {
                var findDesktopResultCode = GetWindows10VirtualDesktopManagerInternal(virtualDesktopShell).FindDesktop(desktopIdentifier, out var nativeVirtualDesktop);
                if (findDesktopResultCode == TypeElementNotFoundHresult)
                {
                    virtualDesktop = default;
                    return false;
                }

                ThrowIfFailed(findDesktopResultCode);
                virtualDesktop = new(VirtualDesktopApiVersion.Windows10, nativeVirtualDesktop);
                return true;
            }

            case VirtualDesktopApiVersion.Windows11:
            {
                var findDesktopResultCode = GetWindows11VirtualDesktopManagerInternal(virtualDesktopShell).FindDesktop(desktopIdentifier, out var nativeVirtualDesktop);
                if (findDesktopResultCode == TypeElementNotFoundHresult)
                {
                    virtualDesktop = default;
                    return false;
                }

                ThrowIfFailed(findDesktopResultCode);
                virtualDesktop = new(VirtualDesktopApiVersion.Windows11, nativeVirtualDesktop);
                return true;
            }

            case VirtualDesktopApiVersion.Windows11Version24H2OrGreater:
            {
                var findDesktopResultCode = GetWindows11Version24H2OrGreaterVirtualDesktopManagerInternal(virtualDesktopShell).FindDesktop(desktopIdentifier, out var nativeVirtualDesktop);
                if (findDesktopResultCode == TypeElementNotFoundHresult)
                {
                    virtualDesktop = default;
                    return false;
                }

                ThrowIfFailed(findDesktopResultCode);
                virtualDesktop = new(VirtualDesktopApiVersion.Windows11Version24H2OrGreater, nativeVirtualDesktop);
                return true;
            }

            default:
                throw new PlatformNotSupportedException("The current Windows version is not supported.");
        }
    }

    public static void SwitchDesktop(VirtualDesktopShell virtualDesktopShell, VirtualDesktopHandle virtualDesktop)
    {
        switch (virtualDesktopShell.VirtualDesktopApiVersion)
        {
            case VirtualDesktopApiVersion.Windows10:
                ThrowIfFailed(GetWindows10VirtualDesktopManagerInternal(virtualDesktopShell).SwitchDesktop(GetWindows10VirtualDesktop(virtualDesktop)));
                return;

            case VirtualDesktopApiVersion.Windows11:
                ThrowIfFailed(GetWindows11VirtualDesktopManagerInternal(virtualDesktopShell).SwitchDesktop(GetWindows11VirtualDesktop(virtualDesktop)));
                return;

            case VirtualDesktopApiVersion.Windows11Version24H2OrGreater:
                ThrowIfFailed(GetWindows11Version24H2OrGreaterVirtualDesktopManagerInternal(virtualDesktopShell).SwitchDesktop(GetWindows11VirtualDesktop(virtualDesktop)));
                return;

            default:
                throw new PlatformNotSupportedException("The current Windows version is not supported.");
        }
    }

    public static void RemoveDesktop(VirtualDesktopShell virtualDesktopShell, VirtualDesktopHandle removeVirtualDesktop, VirtualDesktopHandle fallbackVirtualDesktop)
    {
        switch (virtualDesktopShell.VirtualDesktopApiVersion)
        {
            case VirtualDesktopApiVersion.Windows10:
                ThrowIfFailed(GetWindows10VirtualDesktopManagerInternal(virtualDesktopShell).RemoveDesktop(GetWindows10VirtualDesktop(removeVirtualDesktop), GetWindows10VirtualDesktop(fallbackVirtualDesktop)));
                return;

            case VirtualDesktopApiVersion.Windows11:
                ThrowIfFailed(GetWindows11VirtualDesktopManagerInternal(virtualDesktopShell).RemoveDesktop(GetWindows11VirtualDesktop(removeVirtualDesktop), GetWindows11VirtualDesktop(fallbackVirtualDesktop)));
                return;

            case VirtualDesktopApiVersion.Windows11Version24H2OrGreater:
                ThrowIfFailed(GetWindows11Version24H2OrGreaterVirtualDesktopManagerInternal(virtualDesktopShell).RemoveDesktop(GetWindows11VirtualDesktop(removeVirtualDesktop), GetWindows11VirtualDesktop(fallbackVirtualDesktop)));
                return;

            default:
                throw new PlatformNotSupportedException("The current Windows version is not supported.");
        }
    }

    public static void MoveViewToDesktop(VirtualDesktopShell virtualDesktopShell, IApplicationView applicationView, VirtualDesktopHandle virtualDesktop)
    {
        switch (virtualDesktopShell.VirtualDesktopApiVersion)
        {
            case VirtualDesktopApiVersion.Windows10:
                ThrowIfFailed(GetWindows10VirtualDesktopManagerInternal(virtualDesktopShell).MoveViewToDesktop(applicationView, GetWindows10VirtualDesktop(virtualDesktop)));
                return;

            case VirtualDesktopApiVersion.Windows11:
                ThrowIfFailed(GetWindows11VirtualDesktopManagerInternal(virtualDesktopShell).MoveViewToDesktop(applicationView, GetWindows11VirtualDesktop(virtualDesktop)));
                return;

            case VirtualDesktopApiVersion.Windows11Version24H2OrGreater:
                ThrowIfFailed(GetWindows11Version24H2OrGreaterVirtualDesktopManagerInternal(virtualDesktopShell).MoveViewToDesktop(applicationView, GetWindows11VirtualDesktop(virtualDesktop)));
                return;

            default:
                throw new PlatformNotSupportedException("The current Windows version is not supported.");
        }
    }

    public static bool CanMoveViewBetweenDesktops(VirtualDesktopShell virtualDesktopShell, IApplicationView applicationView)
    {
        switch (virtualDesktopShell.VirtualDesktopApiVersion)
        {
            case VirtualDesktopApiVersion.Windows10:
                ThrowIfFailed(GetWindows10VirtualDesktopManagerInternal(virtualDesktopShell).CanViewMoveDesktops(applicationView, out var canMoveWindows10View));
                return canMoveWindows10View;

            case VirtualDesktopApiVersion.Windows11:
                ThrowIfFailed(GetWindows11VirtualDesktopManagerInternal(virtualDesktopShell).CanViewMoveDesktops(applicationView, out var canMoveWindows11View));
                return canMoveWindows11View;

            case VirtualDesktopApiVersion.Windows11Version24H2OrGreater:
                ThrowIfFailed(GetWindows11Version24H2OrGreaterVirtualDesktopManagerInternal(virtualDesktopShell).CanViewMoveDesktops(applicationView, out var canMoveWindows11Version24H2OrGreaterView));
                return canMoveWindows11Version24H2OrGreaterView;

            default:
                throw new PlatformNotSupportedException("The current Windows version is not supported.");
        }
    }

    public static bool IsWindowOnCurrentDesktop(IVirtualDesktopManager virtualDesktopManager, nint windowHandle)
    {
        ThrowIfFailed(virtualDesktopManager.IsWindowOnCurrentVirtualDesktop(windowHandle, out var isWindowOnCurrentDesktop));
        return isWindowOnCurrentDesktop;
    }

    public static bool TryIsWindowOnCurrentDesktop(IVirtualDesktopManager virtualDesktopManager, nint windowHandle, out bool isWindowOnCurrentDesktop)
    {
        var windowDesktopStateResultCode = virtualDesktopManager.IsWindowOnCurrentVirtualDesktop(windowHandle, out var nativeIsWindowOnCurrentDesktop);
        if (windowDesktopStateResultCode == TypeElementNotFoundHresult)
        {
            isWindowOnCurrentDesktop = false;
            return false;
        }

        ThrowIfFailed(windowDesktopStateResultCode);
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
        var windowDesktopIdentifierResultCode = virtualDesktopManager.GetWindowDesktopId(windowHandle, out var nativeDesktopIdentifier);
        if (windowDesktopIdentifierResultCode == TypeElementNotFoundHresult)
        {
            desktopIdentifier = default;
            return false;
        }

        ThrowIfFailed(windowDesktopIdentifierResultCode);
        desktopIdentifier = nativeDesktopIdentifier;
        return true;
    }

    public static void MoveWindowToDesktop(IVirtualDesktopManager virtualDesktopManager, nint windowHandle, VirtualDesktopIdentifier desktopIdentifier) => ThrowIfFailed(virtualDesktopManager.MoveWindowToDesktop(windowHandle, desktopIdentifier));

    public static VirtualDesktopIdentifier GetDesktopIdentifier(VirtualDesktopHandle virtualDesktop) => virtualDesktop.VirtualDesktopApiVersion switch
    {
        VirtualDesktopApiVersion.Windows10 => GetWindows10VirtualDesktopIdentifier(virtualDesktop),
        VirtualDesktopApiVersion.Windows11 => GetWindows11VirtualDesktopIdentifier(virtualDesktop),
        VirtualDesktopApiVersion.Windows11Version24H2OrGreater => GetWindows11VirtualDesktopIdentifier(virtualDesktop),
        _ => throw new PlatformNotSupportedException("The current Windows version is not supported.")
    };

    public static bool IsViewVisibleOnDesktop(VirtualDesktopHandle virtualDesktop, IApplicationView applicationView)
    {
        switch (virtualDesktop.VirtualDesktopApiVersion)
        {
            case VirtualDesktopApiVersion.Windows10:
                ThrowIfFailed(GetWindows10VirtualDesktop(virtualDesktop).IsViewVisible(applicationView, out var isWindows10Visible));
                return isWindows10Visible;

            case VirtualDesktopApiVersion.Windows11:
            case VirtualDesktopApiVersion.Windows11Version24H2OrGreater:
                ThrowIfFailed(GetWindows11VirtualDesktop(virtualDesktop).IsViewVisible(applicationView, out var isWindows11Visible));
                return isWindows11Visible;

            default:
                throw new PlatformNotSupportedException("The current Windows version is not supported.");
        }
    }

    private static int GetDesktopCount(IWindows10VirtualDesktopManagerInternal windows10VirtualDesktopManagerInternal)
    {
        ThrowIfFailed(windows10VirtualDesktopManagerInternal.GetCount(out var desktopCount));
        return desktopCount;
    }

    private static int GetDesktopCount(IWindows11VirtualDesktopManagerInternal windows11VirtualDesktopManagerInternal)
    {
        ThrowIfFailed(windows11VirtualDesktopManagerInternal.GetCount(out var desktopCount));
        return desktopCount;
    }

    private static int GetDesktopCount(IWindows11Version24H2OrGreaterVirtualDesktopManagerInternal windows11Version24H2OrGreaterVirtualDesktopManagerInternal)
    {
        ThrowIfFailed(windows11Version24H2OrGreaterVirtualDesktopManagerInternal.GetCount(out var desktopCount));
        return desktopCount;
    }

    private static VirtualDesktopHandle CreateWindows10VirtualDesktop(VirtualDesktopShell virtualDesktopShell)
    {
        ThrowIfFailed(GetWindows10VirtualDesktopManagerInternal(virtualDesktopShell).CreateDesktop(out var virtualDesktop));
        return new(VirtualDesktopApiVersion.Windows10, virtualDesktop);
    }

    private static VirtualDesktopHandle CreateWindows11VirtualDesktop(VirtualDesktopShell virtualDesktopShell)
    {
        ThrowIfFailed(GetWindows11VirtualDesktopManagerInternal(virtualDesktopShell).CreateDesktop(out var virtualDesktop));
        return new(VirtualDesktopApiVersion.Windows11, virtualDesktop);
    }

    private static VirtualDesktopHandle CreateWindows11Version24H2OrGreaterVirtualDesktop(VirtualDesktopShell virtualDesktopShell)
    {
        ThrowIfFailed(GetWindows11Version24H2OrGreaterVirtualDesktopManagerInternal(virtualDesktopShell).CreateDesktop(out var virtualDesktop));
        return new(VirtualDesktopApiVersion.Windows11Version24H2OrGreater, virtualDesktop);
    }

    private static VirtualDesktopHandle GetCurrentWindows10VirtualDesktop(VirtualDesktopShell virtualDesktopShell)
    {
        ThrowIfFailed(GetWindows10VirtualDesktopManagerInternal(virtualDesktopShell).GetCurrentDesktop(out var virtualDesktop));
        return new(VirtualDesktopApiVersion.Windows10, virtualDesktop);
    }

    private static VirtualDesktopHandle GetCurrentWindows11VirtualDesktop(VirtualDesktopShell virtualDesktopShell)
    {
        ThrowIfFailed(GetWindows11VirtualDesktopManagerInternal(virtualDesktopShell).GetCurrentDesktop(out var virtualDesktop));
        return new(VirtualDesktopApiVersion.Windows11, virtualDesktop);
    }

    private static VirtualDesktopHandle GetCurrentWindows11Version24H2OrGreaterVirtualDesktop(VirtualDesktopShell virtualDesktopShell)
    {
        ThrowIfFailed(GetWindows11Version24H2OrGreaterVirtualDesktopManagerInternal(virtualDesktopShell).GetCurrentDesktop(out var virtualDesktop));
        return new(VirtualDesktopApiVersion.Windows11Version24H2OrGreater, virtualDesktop);
    }

    private static VirtualDesktopApiVersion GetVirtualDesktopApiVersion()
    {
        var currentWindowsBuildNumber = Environment.OSVersion.Version.Build;
        if (currentWindowsBuildNumber >= MinimumWindows11Version24H2BuildNumber)
            return VirtualDesktopApiVersion.Windows11Version24H2OrGreater;

        if (currentWindowsBuildNumber >= MinimumWindows11BuildNumber)
            return VirtualDesktopApiVersion.Windows11;

        if (currentWindowsBuildNumber >= MinimumSupportedWindowsBuildNumber)
            return VirtualDesktopApiVersion.Windows10;

        throw new PlatformNotSupportedException($"Windows build {currentWindowsBuildNumber} is not supported.");
    }

    private static VirtualDesktopIdentifier GetWindows10VirtualDesktopIdentifier(VirtualDesktopHandle virtualDesktop)
    {
        ThrowIfFailed(GetWindows10VirtualDesktop(virtualDesktop).GetId(out var desktopIdentifier));
        return desktopIdentifier;
    }

    private static VirtualDesktopIdentifier GetWindows11VirtualDesktopIdentifier(VirtualDesktopHandle virtualDesktop)
    {
        ThrowIfFailed(GetWindows11VirtualDesktop(virtualDesktop).GetId(out var desktopIdentifier));
        return desktopIdentifier;
    }

    private static IReadOnlyList<VirtualDesktopHandle> GetWindows10VirtualDesktops(VirtualDesktopShell virtualDesktopShell)
    {
        ThrowIfFailed(GetWindows10VirtualDesktopManagerInternal(virtualDesktopShell).GetDesktops(out var objectArray));
        var desktopCount = GetObjectCount(objectArray);
        var virtualDesktops = new List<VirtualDesktopHandle>(desktopCount);
        for (var index = 0; index < desktopCount; index++)
            virtualDesktops.Add(new(VirtualDesktopApiVersion.Windows10, GetObjectAt<IWindows10VirtualDesktop>(objectArray, index)));

        return virtualDesktops;
    }

    private static IReadOnlyList<VirtualDesktopHandle> GetWindows11VirtualDesktops(VirtualDesktopShell virtualDesktopShell)
    {
        ThrowIfFailed(GetWindows11VirtualDesktopManagerInternal(virtualDesktopShell).GetDesktops(out var objectArray));
        var desktopCount = GetObjectCount(objectArray);
        var virtualDesktops = new List<VirtualDesktopHandle>(desktopCount);
        for (var index = 0; index < desktopCount; index++)
            virtualDesktops.Add(new(VirtualDesktopApiVersion.Windows11, GetObjectAt<IWindows11VirtualDesktop>(objectArray, index)));

        return virtualDesktops;
    }

    private static IReadOnlyList<VirtualDesktopHandle> GetWindows11Version24H2OrGreaterVirtualDesktops(VirtualDesktopShell virtualDesktopShell)
    {
        ThrowIfFailed(GetWindows11Version24H2OrGreaterVirtualDesktopManagerInternal(virtualDesktopShell).GetDesktops(out var objectArray));
        var desktopCount = GetObjectCount(objectArray);
        var virtualDesktops = new List<VirtualDesktopHandle>(desktopCount);
        for (var index = 0; index < desktopCount; index++)
            virtualDesktops.Add(new(VirtualDesktopApiVersion.Windows11Version24H2OrGreater, GetObjectAt<IWindows11VirtualDesktop>(objectArray, index)));

        return virtualDesktops;
    }

    private static IWindows10VirtualDesktop GetWindows10VirtualDesktop(VirtualDesktopHandle virtualDesktop) => virtualDesktop.VirtualDesktopApiVersion == VirtualDesktopApiVersion.Windows10
        ? (IWindows10VirtualDesktop)virtualDesktop.NativeVirtualDesktop
        : throw new InvalidOperationException("The virtual desktop handle is not a Windows 10 desktop.");

    private static IWindows11VirtualDesktop GetWindows11VirtualDesktop(VirtualDesktopHandle virtualDesktop) => virtualDesktop.VirtualDesktopApiVersion is VirtualDesktopApiVersion.Windows11 or VirtualDesktopApiVersion.Windows11Version24H2OrGreater
        ? (IWindows11VirtualDesktop)virtualDesktop.NativeVirtualDesktop
        : throw new InvalidOperationException("The virtual desktop handle is not a Windows 11 desktop.");

    private static IWindows10VirtualDesktopManagerInternal GetWindows10VirtualDesktopManagerInternal(VirtualDesktopShell virtualDesktopShell) => virtualDesktopShell.Windows10VirtualDesktopManagerInternal
        ?? throw new InvalidOperationException("The Windows 10 virtual desktop manager internal interface is unavailable.");

    private static IWindows11VirtualDesktopManagerInternal GetWindows11VirtualDesktopManagerInternal(VirtualDesktopShell virtualDesktopShell) => virtualDesktopShell.Windows11VirtualDesktopManagerInternal
        ?? throw new InvalidOperationException("The Windows 11 virtual desktop manager internal interface is unavailable.");

    private static IWindows11Version24H2OrGreaterVirtualDesktopManagerInternal GetWindows11Version24H2OrGreaterVirtualDesktopManagerInternal(VirtualDesktopShell virtualDesktopShell) => virtualDesktopShell.Windows11Version24H2OrGreaterVirtualDesktopManagerInternal
        ?? throw new InvalidOperationException("The Windows 11 24H2 virtual desktop manager internal interface is unavailable.");

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

    private static nint GetObjectPointerAt(IObjectArray objectArray, int index, Guid interfaceIdentifier)
    {
        ThrowIfFailed(objectArray.GetAt(index, in interfaceIdentifier, out var objectPointer));
        return objectPointer;
    }

    private static unsafe bool TryCreateApplicationViewSnapshot(nint applicationViewPointer, out ApplicationViewSnapshot applicationViewSnapshot)
    {
        if (!TryInvokeApplicationViewMethod(applicationViewPointer, 9, out nint thumbnailWindowHandle)
            || thumbnailWindowHandle == 0
            || !TryInvokeApplicationViewMethod(applicationViewPointer, 25, out Guid virtualDesktopIdentifier))
        {
            applicationViewSnapshot = default;
            return false;
        }

        var isVisible = !TryInvokeApplicationViewMethod(applicationViewPointer, 11, out int visibility) || visibility != 0;
        var showsInSwitchers = !TryInvokeApplicationViewMethod(applicationViewPointer, 27, out int showInSwitchers) || showInSwitchers != 0;
        var hasExtendedFrameBounds = TryInvokeApplicationViewMethod(applicationViewPointer, 16, out Win32.NativeRectangle extendedFrameBounds)
            && !extendedFrameBounds.IsEmpty;

        applicationViewSnapshot = new()
        {
            ThumbnailWindowHandle = thumbnailWindowHandle,
            VirtualDesktopIdentifier = virtualDesktopIdentifier,
            IsVisible = isVisible,
            ShowsInSwitchers = showsInSwitchers,
            HasExtendedFrameBounds = hasExtendedFrameBounds,
            ExtendedFrameBounds = extendedFrameBounds
        };
        return true;
    }

    private static unsafe bool TryInvokeApplicationViewMethod(nint applicationViewPointer, int methodIndex, out nint value)
    {
        var virtualFunctionTable = *(nint**)applicationViewPointer;
        var methodPointer = (delegate* unmanaged[Stdcall]<nint, out nint, int>)virtualFunctionTable[methodIndex];
        value = 0;
        return methodPointer(applicationViewPointer, out value) >= 0;
    }

    private static unsafe bool TryInvokeApplicationViewMethod(nint applicationViewPointer, int methodIndex, out int value)
    {
        var virtualFunctionTable = *(nint**)applicationViewPointer;
        var methodPointer = (delegate* unmanaged[Stdcall]<nint, out int, int>)virtualFunctionTable[methodIndex];
        value = 0;
        return methodPointer(applicationViewPointer, out value) >= 0;
    }

    private static unsafe bool TryInvokeApplicationViewMethod(nint applicationViewPointer, int methodIndex, out Guid value)
    {
        var virtualFunctionTable = *(nint**)applicationViewPointer;
        var methodPointer = (delegate* unmanaged[Stdcall]<nint, out Guid, int>)virtualFunctionTable[methodIndex];
        value = default;
        return methodPointer(applicationViewPointer, out value) >= 0;
    }

    private static unsafe bool TryInvokeApplicationViewMethod(nint applicationViewPointer, int methodIndex, out Win32.NativeRectangle value)
    {
        var virtualFunctionTable = *(nint**)applicationViewPointer;
        var methodPointer = (delegate* unmanaged[Stdcall]<nint, out Win32.NativeRectangle, int>)virtualFunctionTable[methodIndex];
        value = default;
        return methodPointer(applicationViewPointer, out value) >= 0;
    }

    private static unsafe TInterface ConvertToManaged<TInterface>(nint objectPointer) where TInterface : class
    {
        if (objectPointer == 0)
            throw new InvalidOperationException("The COM interface pointer is null.");

        return ComInterfaceMarshaller<TInterface>.ConvertToManaged((void*)objectPointer)
            ?? throw new InvalidOperationException("The COM interface pointer could not be converted to a managed interface.");
    }

    private static void ThrowIfFailed(int resultCode)
    {
        if (resultCode < 0)
            throw new COMException($"The COM interop call failed with HRESULT 0x{resultCode:X8}.", resultCode);
    }

    [LibraryImport("ole32.dll", SetLastError = true)]
    private static partial int CoCreateInstance(in Guid classIdentifier, nint outerUnknownPointer, uint classContext, in Guid interfaceIdentifier, out nint objectPointer);
}

internal readonly record struct ApplicationViewSnapshot
{
    public required nint ThumbnailWindowHandle { get; init; }

    public required VirtualDesktopIdentifier VirtualDesktopIdentifier { get; init; }

    public required bool IsVisible { get; init; }

    public required bool ShowsInSwitchers { get; init; }

    public required bool HasExtendedFrameBounds { get; init; }

    public Win32.NativeRectangle ExtendedFrameBounds { get; init; }
}
