using DeskBorder.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DeskBorder.Services;

public sealed class ApplicationBootstrapService(IServiceProvider serviceProvider, IHotkeyService hotkeyService, IManageWindowService manageWindowService, IDeskBorderRuntimeService deskBorderRuntimeService, ISettingsService settingsService) : IApplicationBootstrapService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IHotkeyService _hotkeyService = hotkeyService;
    private readonly IManageWindowService _manageWindowService = manageWindowService;
    private readonly IDeskBorderRuntimeService _deskBorderRuntimeService = deskBorderRuntimeService;
    private readonly ISettingsService _settingsService = settingsService;
    private bool _isInitialized;

    public async Task InitializeAsync(bool shouldActivateManageWindow)
    {
        if (!_isInitialized)
        {
            await _settingsService.InitializeAsync();
            _hotkeyService.HotkeyInvoked += OnHotkeyServiceHotkeyInvoked;
            if (!_hotkeyService.IsInitialized)
                _hotkeyService.Initialize();

            var manageWindow = _serviceProvider.GetRequiredService<ManageWindow>();
            _ = _serviceProvider.GetRequiredService<NavigatorWindow>();
            _manageWindowService.Initialize(manageWindow);
            await _deskBorderRuntimeService.StartAsync();
            _isInitialized = true;
        }

        if (shouldActivateManageWindow)
            _manageWindowService.Show();
    }

    private async void OnHotkeyServiceHotkeyInvoked(object? sender, HotkeyInvokedEventArgs hotkeyInvokedEventArgs)
    {
        if (hotkeyInvokedEventArgs.HotkeyActionType != HotkeyActionType.ToggleDeskBorderEnabled)
            return;

        await _deskBorderRuntimeService.SetRunningStateAsync(!_deskBorderRuntimeService.IsRunning);
    }
}
