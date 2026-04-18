using DeskBorder.Helpers;

namespace DeskBorder.Services;

public sealed class TrayIconService : ITrayIconService, IDisposable
{
    private readonly IDeskBorderRuntimeService _deskBorderRuntimeService;
    private readonly ISettingsService _settingsService;

    public event EventHandler? StateChanged;

    public bool IsLaunchOnStartupEnabled => _settingsService.Settings.IsLaunchOnStartupEnabled;

    public bool IsRuntimeEnabled => _settingsService.Settings.IsDeskBorderEnabled;

    public bool IsStoreUpdateCheckEnabled => _settingsService.Settings.IsStoreUpdateCheckEnabled;

    public string LaunchOnStartupActionText => LocalizedResourceAccessor.GetString(IsLaunchOnStartupEnabled
        ? "Tray.LaunchOnStartup.DisableAction"
        : "Tray.LaunchOnStartup.EnableAction");

    public string RuntimeStatusText => _deskBorderRuntimeService.IsRunning && !_deskBorderRuntimeService.IsSuspended
        ? LocalizedResourceAccessor.GetString("Tray.RuntimeStatus.Running")
        : LocalizedResourceAccessor.GetString("Tray.RuntimeStatus.Paused");

    public string RuntimeActionText => LocalizedResourceAccessor.GetString(IsRuntimeEnabled
        ? "Tray.Runtime.DisableAction"
        : "Tray.Runtime.EnableAction");

    public string StoreUpdateCheckActionText => LocalizedResourceAccessor.GetString(IsStoreUpdateCheckEnabled
        ? "Tray.StoreUpdateCheck.DisableAction"
        : "Tray.StoreUpdateCheck.EnableAction");

    public TrayIconService(IDeskBorderRuntimeService deskBorderRuntimeService, ISettingsService settingsService)
    {
        _deskBorderRuntimeService = deskBorderRuntimeService;
        _settingsService = settingsService;
        _deskBorderRuntimeService.StateChanged += OnDeskBorderRuntimeServiceStateChanged;
        _settingsService.SettingsChanged += OnSettingsServiceSettingsChanged;
    }

    public void RefreshState() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _deskBorderRuntimeService.StateChanged -= OnDeskBorderRuntimeServiceStateChanged;
        _settingsService.SettingsChanged -= OnSettingsServiceSettingsChanged;
    }

    private void OnDeskBorderRuntimeServiceStateChanged(object? sender, EventArgs eventArguments) => StateChanged?.Invoke(this, EventArgs.Empty);

    private void OnSettingsServiceSettingsChanged(object? sender, EventArgs eventArguments) => StateChanged?.Invoke(this, EventArgs.Empty);
}
