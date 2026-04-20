using DeskBorder.Helpers;
using DeskBorder.Models;
using DeskBorder.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DeskBorder.Services;

public sealed class ApplicationBootstrapService(IServiceProvider serviceProvider, IHotkeyService hotkeyService, IManageWindowService manageWindowService, IDeskBorderRuntimeService deskBorderRuntimeService, ISettingsService settingsService, IStoreUpdateService storeUpdateService, IToastService toastService, IFileLogService fileLogService) : IApplicationBootstrapService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IHotkeyService _hotkeyService = hotkeyService;
    private readonly IManageWindowService _manageWindowService = manageWindowService;
    private readonly IDeskBorderRuntimeService _deskBorderRuntimeService = deskBorderRuntimeService;
    private readonly IFileLogService _fileLogService = fileLogService;
    private readonly ISettingsService _settingsService = settingsService;
    private readonly IStoreUpdateService _storeUpdateService = storeUpdateService;
    private readonly IToastService _toastService = toastService;
    private bool _isInitialized;

    public async Task InitializeAsync(bool shouldActivateManageWindow)
    {
        if (!_isInitialized)
        {
            _fileLogService.WriteInformation(nameof(ApplicationBootstrapService), "Initializing application bootstrap service.");
            await _settingsService.InitializeAsync();
            _hotkeyService.HotkeyInvoked += OnHotkeyServiceHotkeyInvoked;
            _settingsService.SettingsChanged += OnSettingsServiceSettingsChanged;
            if (!_hotkeyService.IsInitialized)
                _hotkeyService.Initialize();

            _storeUpdateService.Initialize();
            var manageWindow = _serviceProvider.GetRequiredService<ManageWindow>();
            _ = _serviceProvider.GetRequiredService<NavigatorWindow>();
            _manageWindowService.Initialize(manageWindow);
            await SynchronizeDeskBorderRuntimeStateAsync();
            _isInitialized = true;
            _fileLogService.WriteInformation(nameof(ApplicationBootstrapService), "Application bootstrap service initialized.");
        }

        if (shouldActivateManageWindow)
        {
            _manageWindowService.Show();
            _fileLogService.WriteInformation(nameof(ApplicationBootstrapService), "Manage window activation requested.");
        }
    }

    private async void OnHotkeyServiceHotkeyInvoked(object? sender, HotkeyInvokedEventArgs hotkeyInvokedEventArgs)
    {
        if (hotkeyInvokedEventArgs.HotkeyActionType != HotkeyActionType.ToggleDeskBorderEnabled)
            return;

        _fileLogService.WriteInformation(nameof(ApplicationBootstrapService), "Received DeskBorder toggle hotkey action.");
        var currentSettings = _settingsService.Settings;
        var updatedSettings = currentSettings with { IsDeskBorderEnabled = !currentSettings.IsDeskBorderEnabled };
        await _settingsService.UpdateSettingsAsync(updatedSettings);
        _fileLogService.WriteInformation(nameof(ApplicationBootstrapService), $"Updated DeskBorder enabled setting to {updatedSettings.IsDeskBorderEnabled} from hotkey.");

        await _toastService.ShowToastAsync(new HotkeyToastPresentationOptions
        {
            Title = LocalizedResourceAccessor.GetString("Toast.Hotkey.ToggleDeskBorder.Title"),
            Message = LocalizedResourceAccessor.GetString(updatedSettings.IsDeskBorderEnabled
                ? "Toast.Hotkey.ToggleDeskBorder.EnabledMessage"
                : "Toast.Hotkey.ToggleDeskBorder.DisabledMessage"),
            Duration = TimeSpan.FromSeconds(2),
            WindowWidth = 360,
            WindowHeight = 100
        });
    }

    private void OnSettingsServiceSettingsChanged(object? sender, EventArgs eventArguments) => _ = SynchronizeDeskBorderRuntimeStateAsync();

    private async Task SynchronizeDeskBorderRuntimeStateAsync()
    {
        var shouldEnableDeskBorder = _settingsService.Settings.IsDeskBorderEnabled;
        _fileLogService.WriteInformation(nameof(ApplicationBootstrapService), $"Synchronizing runtime state. DesiredEnabled={shouldEnableDeskBorder}.");
        await _deskBorderRuntimeService.SetRunningStateAsync(shouldEnableDeskBorder);
    }
}
