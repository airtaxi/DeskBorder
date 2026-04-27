using DeskBorder.Models;

namespace DeskBorder.Services;

public sealed class SettingsMigrationService(IFileLogService fileLogService) : ISettingsMigrationService
{
    private const int SchemaVersionFive = 5;
    private const int SchemaVersionSix = 6;
    private const double DefaultHorizontalDesktopEdgeIgnorePercentage = 20.0;

    private readonly IFileLogService _fileLogService = fileLogService;

    public int CurrentSchemaVersion => SchemaVersionSix;

    public DeskBorderSettings MigrateSettings(DeskBorderSettings settings)
    {
        var migratedSettings = settings;
        if (migratedSettings.SchemaVersion < SchemaVersionFive) migratedSettings = MigrateSettingsToSchemaVersionFive(migratedSettings);
        if (migratedSettings.SchemaVersion < SchemaVersionSix) migratedSettings = MigrateSettingsToSchemaVersionSix(migratedSettings);

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
}
