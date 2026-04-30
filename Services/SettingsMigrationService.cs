using DeskBorder.Models;

namespace DeskBorder.Services;

public sealed class SettingsMigrationService(IFileLogService fileLogService) : ISettingsMigrationService
{
    private const int SchemaVersionFive = 5;
    private const int SchemaVersionSix = 6;
    private const int SchemaVersionSeven = 7;
    private const double DefaultHorizontalDesktopEdgeIgnorePercentage = 20.0;

    private readonly IFileLogService _fileLogService = fileLogService;

    public int CurrentSchemaVersion => SchemaVersionSeven;

    public DeskBorderSettings MigrateSettings(DeskBorderSettings settings)
    {
        var migratedSettings = settings;
        if (migratedSettings.SchemaVersion < SchemaVersionFive) migratedSettings = MigrateSettingsToSchemaVersionFive(migratedSettings);
        if (migratedSettings.SchemaVersion < SchemaVersionSix) migratedSettings = MigrateSettingsToSchemaVersionSix(migratedSettings);
        if (migratedSettings.SchemaVersion < SchemaVersionSeven) migratedSettings = MigrateSettingsToSchemaVersionSeven(migratedSettings);

        return migratedSettings;
    }

    private DeskBorderSettings MigrateSettingsToSchemaVersionFive(DeskBorderSettings settings)
    {
        _fileLogService.WriteInformation(nameof(SettingsMigrationService), $"Migrating settings from schema version {settings.SchemaVersion} to {SchemaVersionFive}.");
        var desktopEdgeIgnoreZoneSettings = settings.DesktopEdgeIgnoreZoneSettings ?? new();
        return settings with
        {
            SchemaVersion = SchemaVersionFive,
            DesktopEdgeIgnoreZoneSettings = desktopEdgeIgnoreZoneSettings with
            {
                LeftIgnorePercentage = DefaultHorizontalDesktopEdgeIgnorePercentage,
                RightIgnorePercentage = DefaultHorizontalDesktopEdgeIgnorePercentage
            }
        };
    }

    private DeskBorderSettings MigrateSettingsToSchemaVersionSix(DeskBorderSettings settings)
    {
        _fileLogService.WriteInformation(nameof(SettingsMigrationService), $"Migrating settings from schema version {settings.SchemaVersion} to {SchemaVersionSix}.");
        return settings with
        {
            SchemaVersion = SchemaVersionSix,
            IsKeyboardModifierConsumptionAfterDesktopActionEnabled = true
        };
    }

    private DeskBorderSettings MigrateSettingsToSchemaVersionSeven(DeskBorderSettings settings)
    {
        _fileLogService.WriteInformation(nameof(SettingsMigrationService), $"Migrating settings from schema version {settings.SchemaVersion} to {SchemaVersionSeven}.");
        return settings with
        {
            SchemaVersion = SchemaVersionSeven,
            SwitchDesktopModifierSettings = MigrateModifierGateSettings(settings.SwitchDesktopModifierSettings),
            CreateDesktopModifierSettings = MigrateModifierGateSettings(settings.CreateDesktopModifierSettings),
            SwitchDesktopWhileMouseButtonsArePressedModifierSettings = MigrateModifierGateSettings(settings.SwitchDesktopWhileMouseButtonsArePressedModifierSettings),
            IsMouseModifierButtonConsumptionAfterDesktopActionEnabled = true
        };
    }

    private static ModifierGateSettings MigrateModifierGateSettings(ModifierGateSettings? modifierGateSettings)
    {
        var actualModifierGateSettings = modifierGateSettings ?? new();
        return actualModifierGateSettings with
        {
            RequiredMouseModifierButtonTriggers = []
        };
    }
}
