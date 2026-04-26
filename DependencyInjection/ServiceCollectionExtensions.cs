using DeskBorder.Services;
using DeskBorder.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DeskBorder.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeskBorderServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IFileLogService, FileLogService>();
        serviceCollection.AddSingleton<ILocalizationService, LocalizationService>();
        serviceCollection.AddSingleton<IManageWindowService, ManageWindowService>();
        serviceCollection.AddSingleton<IManageNavigationService, ManageNavigationService>();
        serviceCollection.AddSingleton<IDeskBorderRuntimeService, DeskBorderRuntimeService>();
        serviceCollection.AddSingleton<IDesktopLifecycleService, DesktopLifecycleService>();
        serviceCollection.AddSingleton<IDesktopEdgeMonitorService, DesktopEdgeMonitorService>();
        serviceCollection.AddSingleton<IForegroundWindowFullscreenService, ForegroundWindowFullscreenService>();
        serviceCollection.AddSingleton<IMouseMovementTrackingService, MouseMovementTrackingService>();
        serviceCollection.AddSingleton<IHotkeyService, HotkeyService>();
        serviceCollection.AddSingleton<INavigatorService, NavigatorService>();
        serviceCollection.AddSingleton<IStartupRegistrationService, StartupRegistrationService>();
        serviceCollection.AddSingleton<ISettingsMigrationService, SettingsMigrationService>();
        serviceCollection.AddSingleton<ISettingsService, SettingsService>();
        serviceCollection.AddSingleton<IStoreUpdateService, StoreUpdateService>();
        serviceCollection.AddSingleton<IThemeService, ThemeService>();
        serviceCollection.AddSingleton<IToastService, ToastService>();
        serviceCollection.AddSingleton<ITrayIconService, TrayIconService>();
        serviceCollection.AddSingleton<IVirtualDesktopService, VirtualDesktopService>();
        serviceCollection.AddSingleton<IApplicationBootstrapService, ApplicationBootstrapService>();
        serviceCollection.AddSingleton(provider => new NavigatorWindow(provider.GetRequiredService<INavigatorService>(), provider.GetRequiredService<ILocalizationService>(), provider.GetRequiredService<IThemeService>()));
        serviceCollection.AddSingleton(provider => new ManageWindow(provider.GetRequiredService<IManageNavigationService>(), provider.GetRequiredService<IDeskBorderRuntimeService>(), provider.GetRequiredService<INavigatorService>(), provider.GetRequiredService<IMouseMovementTrackingService>(), provider.GetRequiredService<ITrayIconService>(), provider.GetRequiredService<ISettingsService>(), provider.GetRequiredService<ILocalizationService>(), provider.GetRequiredService<IThemeService>()));

        return serviceCollection;
    }
}
