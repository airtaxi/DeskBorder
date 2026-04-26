using DeskBorder.Models;

namespace DeskBorder.Services;

public sealed class SettingsMigrationService(IFileLogService fileLogService) : ISettingsMigrationService
{
    private const int SchemaVersionFive = 5;
    private const double DefaultHorizontalDesktopEdgeIgnorePercentage = 20.0;

    private readonly IFileLogService _fileLogService = fileLogService;

    public int CurrentSchemaVersion => SchemaVersionFive;

    public DeskBorderSettings MigrateSettings(DeskBorderSettings settings)
    {
        var migratedSettings = settings;
        if (migratedSettings.SchemaVersion < SchemaVersionFive) migratedSettings = MigrateSettingsToSchemaVersionFive(migratedSettings);

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
}
