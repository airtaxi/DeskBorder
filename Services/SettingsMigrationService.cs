using DeskBorder.Models;

namespace DeskBorder.Services;

public sealed class SettingsMigrationService(IFileLogService fileLogService) : ISettingsMigrationService
{
    private const int SchemaVersionFour = 4;
    private const int SchemaVersionFive = 5;
    private const double DefaultHorizontalDesktopEdgeIgnorePercentage = 20.0;

    private static readonly IReadOnlyList<SettingsSchemaMigration> s_settingsSchemaMigrations =
    [
        new(SchemaVersionFour, SchemaVersionFive)
    ];

    private readonly IFileLogService _fileLogService = fileLogService;

    public int CurrentSchemaVersion => s_settingsSchemaMigrations[^1].TargetSchemaVersion;

    public DeskBorderSettings MigrateSettings(DeskBorderSettings settings)
    {
        var migratedSettings = settings;
        while (migratedSettings.SchemaVersion < CurrentSchemaVersion)
        {
            var settingsSchemaMigration = FindSettingsSchemaMigration(migratedSettings.SchemaVersion);
            if (settingsSchemaMigration is null) return MigrateUnknownLegacySettingsToCurrentSchemaVersion(migratedSettings);
            migratedSettings = MigrateSettingsToNextSchemaVersion(migratedSettings, settingsSchemaMigration);
        }

        return migratedSettings;
    }

    private static SettingsSchemaMigration? FindSettingsSchemaMigration(int sourceSchemaVersion)
    {
        foreach (var settingsSchemaMigration in s_settingsSchemaMigrations)
        {
            if (settingsSchemaMigration.SourceSchemaVersion == sourceSchemaVersion) return settingsSchemaMigration;
        }

        return null;
    }

    private DeskBorderSettings MigrateSettingsToNextSchemaVersion(DeskBorderSettings settings, SettingsSchemaMigration settingsSchemaMigration) => (settingsSchemaMigration.SourceSchemaVersion, settingsSchemaMigration.TargetSchemaVersion) switch
    {
        (SchemaVersionFour, SchemaVersionFive) => MigrateSettingsFromSchemaVersionFourToFive(settings),
        _ => MigrateUnknownLegacySettingsToCurrentSchemaVersion(settings)
    };

    private DeskBorderSettings MigrateSettingsFromSchemaVersionFourToFive(DeskBorderSettings settings)
    {
        _fileLogService.WriteInformation(nameof(SettingsMigrationService), "Migrating settings from schema version 4 to 5.");
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

    private DeskBorderSettings MigrateUnknownLegacySettingsToCurrentSchemaVersion(DeskBorderSettings settings)
    {
        _fileLogService.WriteWarning(nameof(SettingsMigrationService), $"Migrating settings from unknown legacy schema version {settings.SchemaVersion} to {CurrentSchemaVersion}.");
        return settings with { SchemaVersion = CurrentSchemaVersion };
    }

    private sealed record SettingsSchemaMigration(int SourceSchemaVersion, int TargetSchemaVersion);
}
