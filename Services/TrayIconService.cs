using DeskBorder.Helpers;

namespace DeskBorder.Services;

public sealed class TrayIconService : ITrayIconService, IDisposable
{
    private readonly IDeskBorderRuntimeService _deskBorderRuntimeService;
    private readonly IFileLogService _fileLogService;
    private readonly ISettingsService _settingsService;

    public event EventHandler? StateChanged;

    public bool IsLaunchOnStartupEnabled => _settingsService.Settings.IsLaunchOnStartupEnabled;

    public bool IsRuntimeEnabled => _settingsService.Settings.IsDeskBorderEnabled;

    public bool IsStoreUpdateCheckEnabled => _settingsService.Settings.IsStoreUpdateCheckEnabled;

    public string LaunchOnStartupActionText => LocalizedResourceAccessor.GetString(IsLaunchOnStartupEnabled
        ? "Tray.LaunchOnStartupDisableAction"
        : "Tray.LaunchOnStartupEnableAction");

    public string RuntimeStatusText => _deskBorderRuntimeService.IsRunning && !_deskBorderRuntimeService.IsSuspended
        ? LocalizedResourceAccessor.GetString("Tray.RuntimeStatus.Running")
        : LocalizedResourceAccessor.GetString("Tray.RuntimeStatus.Paused");

    public string RuntimeActionText => LocalizedResourceAccessor.GetString(IsRuntimeEnabled
        ? "Tray.RuntimeDisableAction"
        : "Tray.RuntimeEnableAction");

    public string StoreUpdateCheckActionText => LocalizedResourceAccessor.GetString(IsStoreUpdateCheckEnabled
        ? "Tray.StoreUpdateCheckDisableAction"
        : "Tray.StoreUpdateCheckEnableAction");

    public TrayIconService(IDeskBorderRuntimeService deskBorderRuntimeService, ISettingsService settingsService, IFileLogService fileLogService)
    {
        _deskBorderRuntimeService = deskBorderRuntimeService;
        _settingsService = settingsService;
        _fileLogService = fileLogService;
        _deskBorderRuntimeService.StateChanged += OnDeskBorderRuntimeServiceStateChanged;
        _settingsService.SettingsChanged += OnSettingsServiceSettingsChanged;
        _fileLogService.WriteInformation(nameof(TrayIconService), "Initialized tray icon service.");
    }

    public void RefreshState()
    {
        _fileLogService.WriteInformation(nameof(TrayIconService), "Refreshing tray icon state.");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _deskBorderRuntimeService.StateChanged -= OnDeskBorderRuntimeServiceStateChanged;
        _settingsService.SettingsChanged -= OnSettingsServiceSettingsChanged;
        _fileLogService.WriteInformation(nameof(TrayIconService), "Disposed tray icon service.");
    }

    private void OnDeskBorderRuntimeServiceStateChanged(object? sender, EventArgs eventArguments)
    {
        _fileLogService.WriteInformation(nameof(TrayIconService), "Observed desk border runtime state change.");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsServiceSettingsChanged(object? sender, EventArgs eventArguments)
    {
        _fileLogService.WriteInformation(nameof(TrayIconService), "Observed settings change for tray icon state.");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
