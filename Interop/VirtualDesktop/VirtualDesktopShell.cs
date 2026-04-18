namespace DeskBorder.Interop.VirtualDesktop;

internal sealed class VirtualDesktopShell(
    INativeServiceProvider nativeServiceProvider,
    IVirtualDesktopManager virtualDesktopManager,
    IVirtualDesktopManagerInternal virtualDesktopManagerInternal,
    IApplicationViewCollection applicationViewCollection)
{
    public INativeServiceProvider NativeServiceProvider { get; } = nativeServiceProvider;
    public IVirtualDesktopManager VirtualDesktopManager { get; } = virtualDesktopManager;
    public IVirtualDesktopManagerInternal VirtualDesktopManagerInternal { get; } = virtualDesktopManagerInternal;
    public IApplicationViewCollection ApplicationViewCollection { get; } = applicationViewCollection;
}
