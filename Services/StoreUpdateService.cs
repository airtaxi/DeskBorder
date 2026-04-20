using DeskBorder.Helpers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Xml.Dom;
using Windows.Services.Store;
using Windows.System;
using Windows.UI.Notifications;

namespace DeskBorder.Services;

public sealed partial class StoreUpdateService(ISettingsService settingsService, IFileLogService fileLogService) : IStoreUpdateService, IDisposable
{
    private const string StorePackageFamilyName = "49536HowonLee.DeskBorder_q278kdbtfr3f2";
    public const string StoreProductId = "9P3PLVML3JQD";
    private static readonly TimeSpan s_checkInterval = TimeSpan.FromHours(8);
    private static readonly Uri s_storePackageFamilyNameProductPageUri = new($"ms-windows-store://pdp/?PFN={StorePackageFamilyName}");
    private static readonly Uri s_storeProductIdentifierProductPageUri = new($"ms-windows-store://pdp/?ProductId={StoreProductId}");

    private readonly IFileLogService _fileLogService = fileLogService;
    private readonly ISettingsService _settingsService = settingsService;
    private CancellationTokenSource? _updateCheckCancellationTokenSource;
    private bool _isInitialized;

    public void Initialize()
    {
        if (_isInitialized)
            return;

        _fileLogService.WriteInformation(nameof(StoreUpdateService), "Initializing store update service.");
        _settingsService.SettingsChanged += OnSettingsServiceSettingsChanged;
        SynchronizeMonitoringState();
        _isInitialized = true;
        _fileLogService.WriteInformation(nameof(StoreUpdateService), "Store update service initialized.");
    }

    public async Task<int> GetAvailableUpdateCountAsync()
    {
        var storeContext = StoreContext.GetDefault();
        var storePackageUpdates = await storeContext.GetAppAndOptionalStorePackageUpdatesAsync();
        return storePackageUpdates.Count;
    }

    public async Task<bool> OpenStoreProductPageAsync() => await Launcher.LaunchUriAsync(s_storePackageFamilyNameProductPageUri)
        || await Launcher.LaunchUriAsync(s_storeProductIdentifierProductPageUri);

    public void Dispose()
    {
        if (_isInitialized)
            _settingsService.SettingsChanged -= OnSettingsServiceSettingsChanged;

        StopMonitoring();
        _fileLogService.WriteInformation(nameof(StoreUpdateService), "Disposed store update service.");
    }

    private async Task CheckForUpdatesAndNotifyAsync()
    {
        var availableUpdateCount = await GetAvailableUpdateCountAsync();
        if (availableUpdateCount > 0)
        {
            _fileLogService.WriteInformation(nameof(StoreUpdateService), $"Detected {availableUpdateCount} available store update(s).");
            ShowSystemToast();
        }
    }

    private async Task RunPeriodicUpdateCheckLoopAsync(CancellationToken cancellationToken)
    {
        using var periodicTimer = new PeriodicTimer(s_checkInterval);
        try
        {
            while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
            {
                try { await CheckForUpdatesAndNotifyAsync(); }
                catch (COMException exception) { _fileLogService.WriteWarning(nameof(StoreUpdateService), "Store update check failed with a COM exception.", exception); }
                catch (InvalidOperationException exception) { _fileLogService.WriteWarning(nameof(StoreUpdateService), "Store update check failed with an invalid operation exception.", exception); }
                catch (UnauthorizedAccessException exception) { _fileLogService.WriteWarning(nameof(StoreUpdateService), "Store update check failed with an unauthorized access exception.", exception); }
            }
        }
        catch (OperationCanceledException)
        {
            _fileLogService.WriteInformation(nameof(StoreUpdateService), "Store update monitoring loop was canceled.");
        }
    }

    private static void ShowSystemToast()
    {
        var storeUri = $"ms-windows-store://pdp/?PFN={Package.Current.Id.FamilyName}";
        var toastXmlDocument = new XmlDocument();
        toastXmlDocument.LoadXml(
            $"""
            <toast activationType="protocol" launch="{storeUri}">
                <visual>
                    <binding template="ToastGeneric">
                        <text>{EscapeToastXml(LocalizedResourceAccessor.GetString("Toast.StoreUpdate.Title"))}</text>
                        <text>{EscapeToastXml(LocalizedResourceAccessor.GetString("Toast.StoreUpdate.Message"))}</text>
                    </binding>
                </visual>
                <actions>
                    <action content="{EscapeToastXml(LocalizedResourceAccessor.GetString("Toast.StoreUpdate.Action"))}" activationType="protocol" arguments="{storeUri}" />
                </actions>
            </toast>
            """);

        var toastNotification = new ToastNotification(toastXmlDocument);
        ToastNotificationManager.CreateToastNotifier().Show(toastNotification);
    }

    private static string EscapeToastXml(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private void StartMonitoring()
    {
        if (_updateCheckCancellationTokenSource is not null)
            return;

        _updateCheckCancellationTokenSource = new CancellationTokenSource();
        _ = RunPeriodicUpdateCheckLoopAsync(_updateCheckCancellationTokenSource.Token);
        _fileLogService.WriteInformation(nameof(StoreUpdateService), "Started periodic store update monitoring.");
    }

    private void StopMonitoring()
    {
        _updateCheckCancellationTokenSource?.Cancel();
        _updateCheckCancellationTokenSource?.Dispose();
        _updateCheckCancellationTokenSource = null;
        _fileLogService.WriteInformation(nameof(StoreUpdateService), "Stopped periodic store update monitoring.");
    }

    private void SynchronizeMonitoringState()
    {
        if (_settingsService.Settings.IsStoreUpdateCheckEnabled)
        {
            StartMonitoring();
            return;
        }

        StopMonitoring();
    }

    private void OnSettingsServiceSettingsChanged(object? sender, EventArgs eventArguments) => SynchronizeMonitoringState();
}
