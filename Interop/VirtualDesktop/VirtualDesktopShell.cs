namespace DeskBorder.Interop.VirtualDesktop;

internal sealed class VirtualDesktopShell(
    VirtualDesktopApiVersion virtualDesktopApiVersion,
    INativeServiceProvider nativeServiceProvider,
    IVirtualDesktopManager virtualDesktopManager,
    IApplicationViewCollection applicationViewCollection,
    IWindows10VirtualDesktopManagerInternal? windows10VirtualDesktopManagerInternal = null,
    IWindows11VirtualDesktopManagerInternal? windows11VirtualDesktopManagerInternal = null,
    IWindows11Version24H2OrGreaterVirtualDesktopManagerInternal? windows11Version24H2OrGreaterVirtualDesktopManagerInternal = null)
{
    public VirtualDesktopApiVersion VirtualDesktopApiVersion { get; } = virtualDesktopApiVersion;
    public INativeServiceProvider NativeServiceProvider { get; } = nativeServiceProvider;
    public IVirtualDesktopManager VirtualDesktopManager { get; } = virtualDesktopManager;
    public IApplicationViewCollection ApplicationViewCollection { get; } = applicationViewCollection;
    public IWindows10VirtualDesktopManagerInternal? Windows10VirtualDesktopManagerInternal { get; } = windows10VirtualDesktopManagerInternal;
    public IWindows11VirtualDesktopManagerInternal? Windows11VirtualDesktopManagerInternal { get; } = windows11VirtualDesktopManagerInternal;
    public IWindows11Version24H2OrGreaterVirtualDesktopManagerInternal? Windows11Version24H2OrGreaterVirtualDesktopManagerInternal { get; } = windows11Version24H2OrGreaterVirtualDesktopManagerInternal;
}
