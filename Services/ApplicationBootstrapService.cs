using DeskBorder.Helpers;
using DeskBorder.Models;
using DeskBorder.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DeskBorder.Services;

public sealed class ApplicationBootstrapService(
    IServiceProvider serviceProvider,
    IHotkeyService hotkeyService,
    IManageWindowService manageWindowService,
    IDeskBorderRuntimeService deskBorderRuntimeService,
    IProcessDesktopPlacementService processDesktopPlacementService,
    IVirtualDesktopService virtualDesktopService,
    ILocalizationService localizationService,
    ISettingsService settingsService,
    IStoreUpdateService storeUpdateService,
    IThemeService themeService,
    IToastService toastService,
    IFileLogService fileLogService) : IApplicationBootstrapService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IHotkeyService _hotkeyService = hotkeyService;
    private readonly IManageWindowService _manageWindowService = manageWindowService;
    private readonly IDeskBorderRuntimeService _deskBorderRuntimeService = deskBorderRuntimeService;
    private readonly IFileLogService _fileLogService = fileLogService;
    private readonly ILocalizationService _localizationService = localizationService;
    private readonly IProcessDesktopPlacementService _processDesktopPlacementService = processDesktopPlacementService;
    private readonly ISettingsService _settingsService = settingsService;
    private readonly IStoreUpdateService _storeUpdateService = storeUpdateService;
    private readonly IThemeService _themeService = themeService;
    private readonly IToastService _toastService = toastService;
    private readonly IVirtualDesktopService _virtualDesktopService = virtualDesktopService;
    private bool _isInitialized;

    public async Task InitializeAsync(bool shouldActivateManageWindow)
    {
        if (!_isInitialized)
        {
            _fileLogService.WriteInformation(nameof(ApplicationBootstrapService), "Initializing application bootstrap service.");
            await _settingsService.InitializeAsync();
            _hotkeyService.HotkeyInvoked += OnHotkeyServiceHotkeyInvoked;
            _settingsService.SettingsChanged += OnSettingsServiceSettingsChanged;
            if (!_hotkeyService.IsInitialized) _hotkeyService.Initialize();

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

    private async void OnHotkeyServiceHotkeyInvoked(object? _, HotkeyInvokedEventArgs hotkeyInvokedEventArgs)
    {
        switch (hotkeyInvokedEventArgs.HotkeyActionType)
        {
            case HotkeyActionType.ToggleDeskBorderEnabled:
                await ToggleDeskBorderEnabledFromHotkeyAsync();
                return;

            case HotkeyActionType.ShowProcessDesktopPlacementQuickConfiguration:
                await ShowProcessDesktopPlacementQuickConfigurationAsync();
                return;

            default:
                return;
        }
    }

    private async Task ToggleDeskBorderEnabledFromHotkeyAsync()
    {
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

    private static ProcessDesktopPlacementRuleSettings CreateProcessDesktopPlacementRule(string processName, ProcessDesktopPlacementTargetSnapshot targetSnapshot) => new()
    {
        ProcessName = processName,
        TargetDesktopIdentifier = targetSnapshot.DesktopIdentifier,
        TargetDesktopNumber = targetSnapshot.DesktopNumber,
        TargetDesktopDisplayName = targetSnapshot.DisplayName,
        IsEnabled = true
    };

    private static ProcessDesktopPlacementRuleSettings CreateProcessDesktopPlacementRule(ProcessDesktopPlacementWindowSnapshot windowSnapshot, ProcessDesktopPlacementTargetSnapshot targetSnapshot) => CreateProcessDesktopPlacementRule(windowSnapshot.ProcessName, targetSnapshot);

    private static ProcessDesktopPlacementTemporaryRuleLifetime ConvertToTemporaryRuleLifetime(ProcessDesktopPlacementRuleLifetime lifetime) => lifetime switch
    {
        ProcessDesktopPlacementRuleLifetime.UntilProcessExit => ProcessDesktopPlacementTemporaryRuleLifetime.UntilProcessExit,
        ProcessDesktopPlacementRuleLifetime.Timed => ProcessDesktopPlacementTemporaryRuleLifetime.Timed,
        _ => throw new InvalidOperationException("The requested quick configuration lifetime is not temporary.")
    };

    private static ProcessDesktopPlacementRuleLifetime ConvertToPopupRuleLifetime(ProcessDesktopPlacementTemporaryRuleLifetime lifetime) => lifetime switch
    {
        ProcessDesktopPlacementTemporaryRuleLifetime.UntilProcessExit => ProcessDesktopPlacementRuleLifetime.UntilProcessExit,
        ProcessDesktopPlacementTemporaryRuleLifetime.Timed => ProcessDesktopPlacementRuleLifetime.Timed,
        _ => ProcessDesktopPlacementRuleLifetime.Permanent
    };

    private static TimeSpan? GetRemainingDuration(ProcessDesktopPlacementTemporaryRuleSnapshot temporaryRuleSnapshot)
    {
        if (!temporaryRuleSnapshot.ExpiresAt.HasValue) return null;

        var remainingDuration = temporaryRuleSnapshot.ExpiresAt.Value - DateTimeOffset.UtcNow;
        return remainingDuration <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : remainingDuration;
    }

    private async Task<ProcessDesktopPlacementPopupResult?> ShowProcessDesktopPlacementPopupWindowAsync(
        ProcessDesktopPlacementWindowSnapshot windowSnapshot,
        ProcessDesktopPlacementTargetSnapshot targetSnapshot,
        ProcessDesktopPlacementPopupInitialRule? initialRule)
        => await UiThreadHelper.ExecuteAsync<ProcessDesktopPlacementPopupResult?>(async () =>
        {
            var processDesktopPlacementPopupWindow = new ProcessDesktopPlacementPopupWindow([windowSnapshot.ProcessName], targetSnapshot, _localizationService, _themeService, initialRule);
            if (!await processDesktopPlacementPopupWindow.ShowModalAsync(windowSnapshot.WindowHandle)) return null;

            return new(
                processDesktopPlacementPopupWindow.Lifetime,
                processDesktopPlacementPopupWindow.Duration,
                processDesktopPlacementPopupWindow.TargetDesktopNumber);
        });

    private ExistingProcessDesktopPlacementRule? TryFindExistingProcessDesktopPlacementRule(string processName)
    {
        var temporaryRuleSnapshot = _processDesktopPlacementService.GetTemporaryRules()
            .FirstOrDefault(ruleSnapshot => string.Equals(ruleSnapshot.Rule.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
        if (temporaryRuleSnapshot is not null)
        {
            return new(
                temporaryRuleSnapshot.Rule,
                ConvertToPopupRuleLifetime(temporaryRuleSnapshot.Lifetime),
                GetRemainingDuration(temporaryRuleSnapshot));
        }

        var persistentRule = _settingsService.Settings.ProcessDesktopPlacementSettings.Rules
            .FirstOrDefault(rule => string.Equals(rule.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
        return persistentRule is null
            ? null
            : new(persistentRule, ProcessDesktopPlacementRuleLifetime.Permanent, null);
    }

    private async Task ShowProcessDesktopPlacementQuickConfigurationAsync()
    {
        var foregroundWindowSnapshot = _processDesktopPlacementService.GetForegroundWindowSnapshot();
        if (foregroundWindowSnapshot is null)
        {
            await ShowProcessDesktopPlacementQuickConfigurationUnavailableToastAsync();
            return;
        }

        var existingRule = TryFindExistingProcessDesktopPlacementRule(foregroundWindowSnapshot.ProcessName);
        var initialTargetSnapshot = existingRule is null
            ? _virtualDesktopService.GetCurrentProcessDesktopPlacementTarget()
            : _virtualDesktopService.GetProcessDesktopPlacementTarget(existingRule.Rule.TargetDesktopNumber);
        var initialRule = existingRule is null
            ? null
            : new ProcessDesktopPlacementPopupInitialRule(existingRule.Lifetime, existingRule.Duration);
        var popupResult = await ShowProcessDesktopPlacementPopupWindowAsync(foregroundWindowSnapshot, initialTargetSnapshot, initialRule);
        if (popupResult is null) return;

        var targetSnapshot = _virtualDesktopService.GetProcessDesktopPlacementTarget(popupResult.TargetDesktopNumber);
        var processDesktopPlacementRule = CreateProcessDesktopPlacementRule(foregroundWindowSnapshot, targetSnapshot);
        switch (popupResult.Lifetime)
        {
            case ProcessDesktopPlacementRuleLifetime.Permanent:
                _ = _processDesktopPlacementService.RemoveTemporaryRule(processDesktopPlacementRule.ProcessName);
                await UpsertPersistentProcessDesktopPlacementRuleAsync(processDesktopPlacementRule);
                break;
            default:
                await RemovePersistentProcessDesktopPlacementRuleAsync(processDesktopPlacementRule.ProcessName);
                _processDesktopPlacementService.AddTemporaryRule(
                    processDesktopPlacementRule,
                    ConvertToTemporaryRuleLifetime(popupResult.Lifetime),
                    popupResult.Duration);
                break;
        }

        await _processDesktopPlacementService.ApplyRuleToWindowAsync(foregroundWindowSnapshot.WindowHandle, processDesktopPlacementRule);
        await _toastService.ShowToastAsync(new HotkeyToastPresentationOptions
        {
            Title = LocalizedResourceAccessor.GetString("Toast.ProcessDesktopPlacementQuickConfiguration.SavedTitle"),
            Message = LocalizedResourceAccessor.GetFormattedString("Toast.ProcessDesktopPlacementQuickConfiguration.SavedMessageFormat", processDesktopPlacementRule.ProcessName, processDesktopPlacementRule.TargetDesktopDisplayName),
            Duration = TimeSpan.FromSeconds(2),
            WindowWidth = 420,
            WindowHeight = 110
        });
    }

    private async Task RemovePersistentProcessDesktopPlacementRuleAsync(string processName)
    {
        var currentSettings = _settingsService.Settings;
        var updatedRules = currentSettings.ProcessDesktopPlacementSettings.Rules
            .Where(rule => !string.Equals(rule.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (updatedRules.Length == currentSettings.ProcessDesktopPlacementSettings.Rules.Length) return;

        await _settingsService.UpdateSettingsAsync(currentSettings with
        {
            ProcessDesktopPlacementSettings = currentSettings.ProcessDesktopPlacementSettings with
            {
                Rules = updatedRules
            }
        });
    }

    private async Task ShowProcessDesktopPlacementQuickConfigurationUnavailableToastAsync()
    {
        await _toastService.ShowToastAsync(new HotkeyToastPresentationOptions
        {
            Title = LocalizedResourceAccessor.GetString("Toast.ProcessDesktopPlacementQuickConfiguration.UnavailableTitle"),
            Message = LocalizedResourceAccessor.GetString("Toast.ProcessDesktopPlacementQuickConfiguration.UnavailableMessage"),
            Duration = TimeSpan.FromSeconds(2),
            WindowWidth = 420,
            WindowHeight = 110
        });
    }

    private async Task UpsertPersistentProcessDesktopPlacementRuleAsync(ProcessDesktopPlacementRuleSettings processDesktopPlacementRule)
    {
        var currentSettings = _settingsService.Settings;
        var existingRules = currentSettings.ProcessDesktopPlacementSettings.Rules
            .Where(rule => !string.Equals(rule.ProcessName, processDesktopPlacementRule.ProcessName, StringComparison.OrdinalIgnoreCase));
        await _settingsService.UpdateSettingsAsync(currentSettings with
        {
            ProcessDesktopPlacementSettings = currentSettings.ProcessDesktopPlacementSettings with
            {
                Rules = [.. existingRules, processDesktopPlacementRule]
            }
        });
    }

    private void OnSettingsServiceSettingsChanged(object? _, EventArgs __) => _ = SynchronizeDeskBorderRuntimeStateAsync();

    private async Task SynchronizeDeskBorderRuntimeStateAsync()
    {
        var shouldEnableDeskBorder = _settingsService.Settings.IsDeskBorderEnabled;
        _fileLogService.WriteInformation(nameof(ApplicationBootstrapService), $"Synchronizing runtime state. DesiredEnabled={shouldEnableDeskBorder}.");
        await _deskBorderRuntimeService.SetRunningStateAsync(shouldEnableDeskBorder);
    }

    private sealed record ProcessDesktopPlacementPopupResult(ProcessDesktopPlacementRuleLifetime Lifetime, TimeSpan Duration, int TargetDesktopNumber);

    private sealed record ExistingProcessDesktopPlacementRule(ProcessDesktopPlacementRuleSettings Rule, ProcessDesktopPlacementRuleLifetime Lifetime, TimeSpan? Duration);
}
