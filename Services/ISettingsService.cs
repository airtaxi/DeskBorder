using DeskBorder.Models;

namespace DeskBorder.Services;

public interface ISettingsService
{
    event EventHandler? SettingsChanged;

    DeskBorderSettings Settings { get; }

    Task ExportAsync(string destinationFilePath);

    Task ImportAsync(string sourceFilePath);

    Task InitializeAsync();

    Task ReloadAsync();

    Task<bool> RefreshLaunchOnStartupEnabledAsync();

    Task ResetAsync();

    Task SetLaunchOnStartupEnabledAsync(bool isEnabled);

    Task UpdateSettingsAsync(DeskBorderSettings settings);
}
