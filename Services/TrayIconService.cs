using DeskBorder.Helpers;

namespace DeskBorder.Services;

public sealed class TrayIconService : ITrayIconService, IDisposable
{
    private readonly IDeskBorderRuntimeService _deskBorderRuntimeService;
    private readonly ISettingsService _settingsService;

    public event EventHandler? StateChanged;

    public bool IsLaunchOnStartupEnabled => _settingsService.Settings.IsLaunchOnStartupEnabled;

    public bool IsRuntimeEnabled => _deskBorderRuntimeService.IsRunning;

    public bool IsStoreUpdateCheckEnabled => _settingsService.Settings.IsStoreUpdateCheckEnabled;

    public string LaunchOnStartupToggleText => LocalizedResourceAccessor.GetString("Tray.LaunchOnStartup");

    public string RuntimeStatusText => _deskBorderRuntimeService.IsRunning
        ? LocalizedResourceAccessor.GetString("Tray.RuntimeStatus.Running")
        : LocalizedResourceAccessor.GetString("Tray.RuntimeStatus.Paused");

    public string RuntimeToggleText => LocalizedResourceAccessor.GetString("Tray.RuntimeToggle");

    public string StoreUpdateCheckToggleText => LocalizedResourceAccessor.GetString("Tray.StoreUpdateCheck");

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
