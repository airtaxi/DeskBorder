using DeskBorder.Models;
using System.IO;
using System.Text.Json;
using Windows.System;
using Windows.Storage;
using DeskBorder.Helpers;

namespace DeskBorder.Services;

public sealed class SettingsService(IStartupRegistrationService startupRegistrationService, ILocalizationService localizationService, IThemeService themeService, IFileLogService fileLogService) : ISettingsService
{
    private const string SettingsFileExtension = ".dbs";
    private const string SettingsKey = "DeskBorderSettings";
    private const int CurrentSchemaVersion = 1;
    private const double DefaultAutoDeleteWarningTimeoutSeconds = 3.0;
    private const double DefaultDesktopEdgeAdditionalTriggerDistancePercentage = 5.0;
    private const double MaximumAutoDeleteWarningTimeoutSeconds = 10.0;
    private const double MaximumDesktopEdgeAdditionalTriggerDistancePercentage = 50.0;
    private const double MaximumDesktopEdgeIgnorePercentage = 49.0;
    private const double MinimumAutoDeleteWarningTimeoutSeconds = 0.5;
    private const double MinimumDesktopEdgeAdditionalTriggerDistancePercentage = 0.1;

    private static readonly ApplicationDataContainer s_localSettings =
        ApplicationData.Current.LocalSettings;

    private readonly IFileLogService _fileLogService = fileLogService;
    private readonly IStartupRegistrationService _startupRegistrationService = startupRegistrationService;
    private readonly ILocalizationService _localizationService = localizationService;
    private readonly IThemeService _themeService = themeService;
    private DeskBorderSettings _settings = DeskBorderSettings.CreateDefault();
    private bool _isInitialized;

    public event EventHandler? SettingsChanged;

    public DeskBorderSettings Settings => CloneSettings(_settings);

    public async Task ExportAsync(string destinationFilePath)
    {
        if (!_isInitialized)
            await InitializeAsync();

        ValidateSettingsFilePath(destinationFilePath, nameof(destinationFilePath));
        await File.WriteAllTextAsync(destinationFilePath, JsonSerializer.Serialize(_settings, DeskBorderSettingsSerializationContext.Default.DeskBorderSettings));
        _fileLogService.WriteInformation(nameof(SettingsService), $"Exported settings to '{destinationFilePath}'.");
    }

    public async Task ImportAsync(string sourceFilePath)
    {
        if (!_isInitialized)
            await InitializeAsync();

        ValidateSettingsFilePath(sourceFilePath, nameof(sourceFilePath));
        var serializedSettings = await File.ReadAllTextAsync(sourceFilePath);
        await UpdateSettingsAsync(LoadDeserializedSettings(serializedSettings));
        _fileLogService.WriteInformation(nameof(SettingsService), $"Imported settings from '{sourceFilePath}'.");
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        _fileLogService.WriteInformation(nameof(SettingsService), "Initializing settings service.");
        await ReloadStoredSettingsAsync(shouldApplyDefaultLaunchOnStartupWhenMissing: true);
        _isInitialized = true;
        _fileLogService.WriteInformation(nameof(SettingsService), "Settings service initialized.");
    }

    public async Task ReloadAsync()
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
            return;
        }

        await ReloadStoredSettingsAsync(shouldApplyDefaultLaunchOnStartupWhenMissing: false);
        _fileLogService.WriteInformation(nameof(SettingsService), "Reloaded settings from local storage.");
    }

    public async Task<bool> RefreshLaunchOnStartupEnabledAsync()
    {
        await ReloadAsync();
        return _settings.IsLaunchOnStartupEnabled;
    }

    public async Task SetLaunchOnStartupEnabledAsync(bool isEnabled)
    {
        if (!_isInitialized)
            await InitializeAsync();

        await UpdateSettingsAsync(_settings with { IsLaunchOnStartupEnabled = isEnabled });
        _fileLogService.WriteInformation(nameof(SettingsService), $"Requested launch on startup state {isEnabled}.");
    }

    public async Task UpdateSettingsAsync(DeskBorderSettings settings)
    {
        if (!_isInitialized)
            await InitializeAsync();

        var normalizedSettings = NormalizeSettings(settings);
        if (normalizedSettings.IsLaunchOnStartupEnabled != _settings.IsLaunchOnStartupEnabled)
            await _startupRegistrationService.SetIsEnabledAsync(normalizedSettings.IsLaunchOnStartupEnabled);

        await ApplySettingsAsync(normalizedSettings);
        _fileLogService.WriteInformation(nameof(SettingsService), "Applied updated settings.");
    }

    private static DeskBorderSettings CloneSettings(DeskBorderSettings settings) => NormalizeSettings(settings);

    private static string[] NormalizeBlacklistedProcessNames(string[]? blacklistedProcessNames) => (blacklistedProcessNames ?? [])
        .Where(processName => !string.IsNullOrWhiteSpace(processName))
        .Select(processName => processName.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string[] NormalizeWhitelistedProcessNames(string[]? whitelistedProcessNames) => (whitelistedProcessNames ?? [])
        .Where(processName => !string.IsNullOrWhiteSpace(processName))
        .Select(processName => processName.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static TriggerRectangleSettings NormalizeTriggerRectangleSettings(TriggerRectangleSettings? triggerRectangleSettings)
    {
        var actualTriggerRectangleSettings = triggerRectangleSettings ?? new();
        var width = ClampNormalizedLength(actualTriggerRectangleSettings.Width);
        var height = ClampNormalizedLength(actualTriggerRectangleSettings.Height);
        var left = ClampNormalizedOffset(actualTriggerRectangleSettings.Left, width);
        var top = ClampNormalizedOffset(actualTriggerRectangleSettings.Top, height);
        return actualTriggerRectangleSettings with
        {
            Left = left,
            Top = top,
            Width = width,
            Height = height
        };
    }

    private static DeskBorderSettings NormalizeSettings(DeskBorderSettings settings)
    {
        var normalizedWhitelistedProcessNames = NormalizeWhitelistedProcessNames(settings.WhitelistedProcessNames);
        var normalizedSettings = new DeskBorderSettings
        {
            SchemaVersion = CurrentSchemaVersion,
            IsDeskBorderEnabled = settings.IsDeskBorderEnabled,
            MultiDisplayBehavior = settings.MultiDisplayBehavior,
            SwitchDesktopModifierSettings = NormalizeModifierGateSettings(settings.SwitchDesktopModifierSettings),
            CreateDesktopModifierSettings = NormalizeModifierGateSettings(settings.CreateDesktopModifierSettings, KeyboardModifierKeys.Shift),
            IsDesktopCreationEnabled = settings.IsDesktopCreationEnabled,
            IsAutoDeleteEnabled = settings.IsAutoDeleteEnabled,
            IsAutoDeleteWarningEnabled = settings.IsAutoDeleteWarningEnabled,
            IsAutoDeleteCompletionToastEnabled = settings.IsAutoDeleteCompletionToastEnabled,
            IsDesktopEdgeAdditionalTriggerDistanceEnabled = settings.IsDesktopEdgeAdditionalTriggerDistanceEnabled,
            DesktopEdgeAdditionalTriggerDistancePercentage = ClampDesktopEdgeAdditionalTriggerDistancePercentage(settings.DesktopEdgeAdditionalTriggerDistancePercentage),
            AutoDeleteWarningTimeoutSeconds = ClampAutoDeleteWarningTimeoutSeconds(settings.AutoDeleteWarningTimeoutSeconds),
            DesktopEdgeIgnoreZoneSettings = NormalizeDesktopEdgeIgnoreZoneSettings(settings.DesktopEdgeIgnoreZoneSettings),
            ApplicationHotkeySettings = NormalizeApplicationHotkeySettings(settings.ApplicationHotkeySettings),
            FocusedWindowMoveHotkeySettings = NormalizeFocusedWindowMoveHotkeySettings(settings.FocusedWindowMoveHotkeySettings),
            NavigatorSettings = NormalizeNavigatorSettings(settings.NavigatorSettings),
            BlacklistedProcessNames = NormalizeBlacklistedProcessNames(settings.BlacklistedProcessNames)
                .Except(normalizedWhitelistedProcessNames, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            WhitelistedProcessNames = normalizedWhitelistedProcessNames,
            IsLaunchOnStartupEnabled = settings.IsLaunchOnStartupEnabled,
            IsStoreUpdateCheckEnabled = settings.IsStoreUpdateCheckEnabled,
            IsWindowsOnlyModifierWarningSuppressed = settings.IsWindowsOnlyModifierWarningSuppressed,
            AppLanguagePreference = settings.AppLanguagePreference,
            ApplicationThemePreference = settings.ApplicationThemePreference
        };
        ValidateSettings(normalizedSettings);
        return normalizedSettings;
    }

    private static DesktopEdgeIgnoreZoneSettings NormalizeDesktopEdgeIgnoreZoneSettings(DesktopEdgeIgnoreZoneSettings? desktopEdgeIgnoreZoneSettings)
    {
        var actualDesktopEdgeIgnoreZoneSettings = desktopEdgeIgnoreZoneSettings ?? new();
        return actualDesktopEdgeIgnoreZoneSettings with
        {
            TopIgnorePercentage = ClampDesktopEdgeIgnorePercentage(actualDesktopEdgeIgnoreZoneSettings.TopIgnorePercentage),
            BottomIgnorePercentage = ClampDesktopEdgeIgnorePercentage(actualDesktopEdgeIgnoreZoneSettings.BottomIgnorePercentage)
        };
    }

    private static ApplicationHotkeySettings NormalizeApplicationHotkeySettings(ApplicationHotkeySettings? applicationHotkeySettings)
    {
        var actualApplicationHotkeySettings = applicationHotkeySettings ?? new();
        return actualApplicationHotkeySettings with
        {
            ToggleDeskBorderEnabledHotkey = NormalizeKeyboardShortcutSettings(actualApplicationHotkeySettings.ToggleDeskBorderEnabledHotkey)
        };
    }

    private static FocusedWindowMoveHotkeySettings NormalizeFocusedWindowMoveHotkeySettings(FocusedWindowMoveHotkeySettings? focusedWindowMoveHotkeySettings)
    {
        var actualFocusedWindowMoveHotkeySettings = focusedWindowMoveHotkeySettings ?? new();
        return actualFocusedWindowMoveHotkeySettings with
        {
            MoveToPreviousDesktopHotkey = NormalizeKeyboardShortcutSettings(actualFocusedWindowMoveHotkeySettings.MoveToPreviousDesktopHotkey),
            MoveToNextDesktopHotkey = NormalizeKeyboardShortcutSettings(actualFocusedWindowMoveHotkeySettings.MoveToNextDesktopHotkey)
        };
    }

    private static KeyboardShortcutSettings NormalizeKeyboardShortcutSettings(KeyboardShortcutSettings? keyboardShortcutSettings)
    {
        var actualKeyboardShortcutSettings = keyboardShortcutSettings ?? new();
        return actualKeyboardShortcutSettings with
        {
            RequiredKeyboardModifierKeys = actualKeyboardShortcutSettings.RequiredKeyboardModifierKeys
        };
    }

    private static ModifierGateSettings NormalizeModifierGateSettings(ModifierGateSettings? modifierGateSettings, KeyboardModifierKeys defaultKeyboardModifierKeys = KeyboardModifierKeys.None)
    {
        var actualModifierGateSettings = modifierGateSettings ?? new()
        {
            RequiredKeyboardModifierKeys = defaultKeyboardModifierKeys
        };
        return actualModifierGateSettings with
        {
            RequiredKeyboardModifierKeys = actualModifierGateSettings.RequiredKeyboardModifierKeys
        };
    }

    private static NavigatorSettings NormalizeNavigatorSettings(NavigatorSettings? navigatorSettings)
    {
        var actualNavigatorSettings = navigatorSettings ?? new();
        return actualNavigatorSettings with
        {
            ToggleHotkey = NormalizeKeyboardShortcutSettings(actualNavigatorSettings.ToggleHotkey),
            TriggerRectangle = NormalizeTriggerRectangleSettings(actualNavigatorSettings.TriggerRectangle)
        };
    }

    private static double ClampNormalizedLength(double value) => double.IsFinite(value) ? Math.Clamp(value, 0.01, 1.0) : 0.01;

    private static double ClampNormalizedOffset(double value, double length) => double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0 - length) : 0.0;

    private static double ClampAutoDeleteWarningTimeoutSeconds(double value) => double.IsFinite(value)
        ? Math.Clamp(value, MinimumAutoDeleteWarningTimeoutSeconds, MaximumAutoDeleteWarningTimeoutSeconds)
        : DefaultAutoDeleteWarningTimeoutSeconds;

    private static double ClampDesktopEdgeAdditionalTriggerDistancePercentage(double value) => double.IsFinite(value)
        ? Math.Clamp(value, MinimumDesktopEdgeAdditionalTriggerDistancePercentage, MaximumDesktopEdgeAdditionalTriggerDistancePercentage)
        : DefaultDesktopEdgeAdditionalTriggerDistancePercentage;

    private static double ClampDesktopEdgeIgnorePercentage(double value) => double.IsFinite(value) ? Math.Clamp(value, 0.0, MaximumDesktopEdgeIgnorePercentage) : 0.0;

    private static string GetKeyboardShortcutDisplayName(string keyboardShortcutDisplayNameResourceKey) => LocalizedResourceAccessor.GetString(keyboardShortcutDisplayNameResourceKey);

    private static void ValidateKeyboardShortcutSettings(KeyboardShortcutSettings keyboardShortcutSettings, string keyboardShortcutDisplayNameResourceKey)
    {
        if (!keyboardShortcutSettings.IsEnabled)
            return;

        if (keyboardShortcutSettings.Key == VirtualKey.None)
            throw new InvalidOperationException(LocalizedResourceAccessor.GetFormattedString(
                "Settings.Validation.HotkeyMissingKeyFormat",
                GetKeyboardShortcutDisplayName(keyboardShortcutDisplayNameResourceKey)));
    }

    private static void ValidateSettings(DeskBorderSettings settings)
    {
        ValidateKeyboardShortcutSettings(settings.ApplicationHotkeySettings.ToggleDeskBorderEnabledHotkey, "SettingsPage_ToggleDeskBorderHotkeyToggleSwitch.Header");
        ValidateKeyboardShortcutSettings(settings.FocusedWindowMoveHotkeySettings.MoveToPreviousDesktopHotkey, "SettingsPage_MovePreviousHotkeyToggleSwitch.Header");
        ValidateKeyboardShortcutSettings(settings.FocusedWindowMoveHotkeySettings.MoveToNextDesktopHotkey, "SettingsPage_MoveNextHotkeyToggleSwitch.Header");
        ValidateKeyboardShortcutSettings(settings.NavigatorSettings.ToggleHotkey, "SettingsPage_NavigatorToggleHotkeyToggleSwitch.Header");
        ValidateUniqueKeyboardShortcutSettings(
        [
            new("SettingsPage_ToggleDeskBorderHotkeyToggleSwitch.Header", settings.ApplicationHotkeySettings.ToggleDeskBorderEnabledHotkey),
            new("SettingsPage_MovePreviousHotkeyToggleSwitch.Header", settings.FocusedWindowMoveHotkeySettings.MoveToPreviousDesktopHotkey),
            new("SettingsPage_MoveNextHotkeyToggleSwitch.Header", settings.FocusedWindowMoveHotkeySettings.MoveToNextDesktopHotkey),
            new("SettingsPage_NavigatorToggleHotkeyToggleSwitch.Header", settings.NavigatorSettings.ToggleHotkey)
        ]);
    }

    private static void ValidateUniqueKeyboardShortcutSettings(
        IReadOnlyList<(string KeyboardShortcutDisplayNameResourceKey, KeyboardShortcutSettings KeyboardShortcutSettings)> keyboardShortcutEntries)
    {
        var registeredKeyboardShortcuts = new Dictionary<(KeyboardModifierKeys RequiredKeyboardModifierKeys, VirtualKey Key), string>();
        foreach (var keyboardShortcutEntry in keyboardShortcutEntries)
        {
            if (!keyboardShortcutEntry.KeyboardShortcutSettings.IsEnabled || keyboardShortcutEntry.KeyboardShortcutSettings.Key == VirtualKey.None)
                continue;

            var keyboardShortcutIdentity = (
                keyboardShortcutEntry.KeyboardShortcutSettings.RequiredKeyboardModifierKeys,
                keyboardShortcutEntry.KeyboardShortcutSettings.Key);
            if (registeredKeyboardShortcuts.TryGetValue(keyboardShortcutIdentity, out var existingKeyboardShortcutDisplayNameResourceKey))
                throw new InvalidOperationException(LocalizedResourceAccessor.GetFormattedString(
                    "Settings.Validation.HotkeyDuplicateFormat",
                    GetKeyboardShortcutDisplayName(keyboardShortcutEntry.KeyboardShortcutDisplayNameResourceKey),
                    GetKeyboardShortcutDisplayName(existingKeyboardShortcutDisplayNameResourceKey)));

            registeredKeyboardShortcuts.Add(keyboardShortcutIdentity, keyboardShortcutEntry.KeyboardShortcutDisplayNameResourceKey);
        }
    }

    private static void ValidateSettingsFilePath(string filePath, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath, parameterName);
        if (!string.Equals(Path.GetExtension(filePath), SettingsFileExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Settings files must use the .dbs extension.", parameterName);
    }

    private DeskBorderSettings LoadDeserializedSettings(string serializedSettings)
    {
        try
        {
            var deserializedSettings = JsonSerializer.Deserialize(serializedSettings, DeskBorderSettingsSerializationContext.Default.DeskBorderSettings);
            return deserializedSettings is null
                ? throw new InvalidOperationException("Stored settings payload was empty.")
                : NormalizeSettings(deserializedSettings);
        }
        catch (JsonException exception)
        {
            _fileLogService.WriteError(nameof(SettingsService), "Stored settings payload is invalid.", exception);
            throw new InvalidOperationException("Stored settings payload is invalid.", exception);
        }
        catch (NotSupportedException exception)
        {
            _fileLogService.WriteError(nameof(SettingsService), "Stored settings payload contains unsupported values.", exception);
            throw new InvalidOperationException("Stored settings payload contains unsupported values.", exception);
        }
    }

    private bool TryLoadStoredSettings(out DeskBorderSettings settings)
    {
        if (s_localSettings.Values[SettingsKey] is not string serializedSettings)
        {
            settings = DeskBorderSettings.CreateDefault();
            return false;
        }

        settings = LoadDeserializedSettings(serializedSettings);
        return true;
    }

    private async Task ApplySettingsAsync(DeskBorderSettings settings)
    {
        var normalizedSettings = NormalizeSettings(settings);
        var isLaunchOnStartupEnabled = await _startupRegistrationService.GetIsEnabledAsync();
        _settings = normalizedSettings with { IsLaunchOnStartupEnabled = isLaunchOnStartupEnabled };
        SaveSettings(_settings);
        _localizationService.ApplyLanguagePreference(_settings.AppLanguagePreference);
        _themeService.ApplyApplicationThemePreference(_settings.ApplicationThemePreference);
        _fileLogService.WriteInformation(nameof(SettingsService), $"Persisted settings. LaunchOnStartup={_settings.IsLaunchOnStartupEnabled}, StoreUpdateChecks={_settings.IsStoreUpdateCheckEnabled}, Language={_settings.AppLanguagePreference}, Theme={_settings.ApplicationThemePreference}.");
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReloadStoredSettingsAsync(bool shouldApplyDefaultLaunchOnStartupWhenMissing)
    {
        var hasStoredSettings = TryLoadStoredSettings(out var storedSettings);
        if (!hasStoredSettings && shouldApplyDefaultLaunchOnStartupWhenMissing && storedSettings.IsLaunchOnStartupEnabled)
            await _startupRegistrationService.SetIsEnabledAsync(true);

        await ApplySettingsAsync(storedSettings);
    }

    private static void SaveSettings(DeskBorderSettings settings) => s_localSettings.Values[SettingsKey] =
        JsonSerializer.Serialize(settings, DeskBorderSettingsSerializationContext.Default.DeskBorderSettings);
}
