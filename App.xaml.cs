using DeskBorder.DependencyInjection;
using DeskBorder.Models;
using DeskBorder.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.AppLifecycle;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Windows.Storage;

namespace DeskBorder;

public partial class App : Application
{
    private const string SettingsKey = "DeskBorderSettings";

    private static IServiceProvider? s_services;

    public static ResourceLoader ResourceLoader { get; set; } = null!;
    public static IServiceProvider Services => s_services ?? throw new InvalidOperationException("Service provider has not been initialized yet.");

    public App()
    {
        InitializeComponent();
        LocalizationService.ApplyLanguagePreferenceOverride(LoadInitialLanguagePreference());
        ResourceLoader = new ResourceLoader();
        s_services = ConfigureServices();

        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public static TService GetRequiredService<TService>() where TService : notnull => Services.GetRequiredService<TService>();

    public static void HandleRedirectedActivation(AppActivationArguments appActivationArguments)
    {
        if (Current is not App currentApplication)
            return;

        var manageWindowService = GetRequiredService<IManageWindowService>();
        if (manageWindowService.IsInitialized)
        {
            var manageWindow = GetRequiredService<Views.ManageWindow>();
            if (manageWindow.DispatcherQueue.TryEnqueue(() => currentApplication.OnRedirectedActivation(appActivationArguments)))
                return;
        }

        currentApplication.OnRedirectedActivation(appActivationArguments);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs launchActivatedEventArgs)
    {
        var appActivationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
        var shouldActivateManageWindow = appActivationArguments.Kind != ExtendedActivationKind.StartupTask;
        await GetRequiredService<IApplicationBootstrapService>().InitializeAsync(shouldActivateManageWindow);
    }

    private static IServiceProvider ConfigureServices()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDeskBorderServices();
        return serviceCollection.BuildServiceProvider(validateScopes: false);
    }

    private async void OnRedirectedActivation(AppActivationArguments appActivationArguments)
    {
        var shouldActivateManageWindow = appActivationArguments.Kind != ExtendedActivationKind.StartupTask;
        await GetRequiredService<IApplicationBootstrapService>().InitializeAsync(shouldActivateManageWindow);
    }

    private static AppLanguagePreference LoadInitialLanguagePreference()
    {
        if (ApplicationData.Current.LocalSettings.Values[SettingsKey] is not string serializedSettings)
            return AppLanguagePreference.System;

        try
        {
            var storedSettings = JsonSerializer.Deserialize(serializedSettings, DeskBorderSettingsSerializationContext.Default.DeskBorderSettings);
            return storedSettings?.AppLanguagePreference ?? AppLanguagePreference.System;
        }
        catch (JsonException)
        {
            return AppLanguagePreference.System;
        }
        catch (NotSupportedException)
        {
            return AppLanguagePreference.System;
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs unhandledExceptionEventArgs)
    {
        WriteException("Microsoft.UI.Xaml.Application.UnhandledException", unhandledExceptionEventArgs.Exception);
        unhandledExceptionEventArgs.Handled = true;
    }

    private static void OnCurrentDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs unhandledExceptionEventArgs)
    {
        if (unhandledExceptionEventArgs.ExceptionObject is Exception exception)
            WriteException("AppDomain.CurrentDomain.UnhandledException", exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs unobservedTaskExceptionEventArgs)
    {
        WriteException("TaskScheduler.UnobservedTaskException", unobservedTaskExceptionEventArgs.Exception);
        unobservedTaskExceptionEventArgs.SetObserved();
    }

    private static void WriteException(string source, Exception exception)
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append('[');
        stringBuilder.Append(source);
        stringBuilder.Append("] ");

        var currentException = exception;
        while (currentException is not null)
        {
            stringBuilder.Append(currentException.GetType().FullName);
            stringBuilder.Append(": ");
            stringBuilder.Append(currentException.Message);
            stringBuilder.AppendLine();
            stringBuilder.AppendLine(currentException.StackTrace);

            currentException = currentException.InnerException;
            if (currentException is not null)
                stringBuilder.AppendLine("--->");
        }

        Debug.WriteLine(stringBuilder.ToString());
    }
}
