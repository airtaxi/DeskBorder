namespace DeskBorder.Services;

public readonly record struct SettingsImportResult
{
    public required bool WasAlwaysRunAsAdministratorSettingExcluded { get; init; }
}
