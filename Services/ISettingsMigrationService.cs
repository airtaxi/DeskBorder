using DeskBorder.Models;

namespace DeskBorder.Services;

public interface ISettingsMigrationService
{
    int CurrentSchemaVersion { get; }

    DeskBorderSettings MigrateSettings(DeskBorderSettings settings);
}
