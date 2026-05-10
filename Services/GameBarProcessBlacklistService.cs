using DeskBorder.Models;
using Microsoft.Win32;
using System.Collections.Concurrent;

namespace DeskBorder.Services;

public sealed class GameBarProcessBlacklistService(ISettingsService settingsService, IFileLogService fileLogService) : IGameBarProcessBlacklistService
{
    private const string GameBarRegistryPath = @"System\GameConfigStore\Children";
    private static readonly ConcurrentDictionary<string, bool> s_gameBarRecognizedGameCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> s_autoBlacklistedGameBarExecutablePaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> s_persistingGameBarExecutablePaths = new(StringComparer.OrdinalIgnoreCase);

    private readonly IFileLogService _fileLogService = fileLogService;
    private readonly ISettingsService _settingsService = settingsService;

    public bool TryAutoBlacklistForegroundProcess(DeskBorderSettings currentSettings, ForegroundProcessSnapshot foregroundProcessSnapshot)
    {
        if (!TryGetForegroundProcessIdentity(foregroundProcessSnapshot, out var processName, out var executablePath)) return false;
        if (IsProcessNameListed(currentSettings.WhitelistedProcessNames, processName) || IsProcessNameListed(currentSettings.BlacklistedProcessNames, processName)) return false;
        if (!IsGameBarRecognizedGame(executablePath)) return false;

        if (s_autoBlacklistedGameBarExecutablePaths.TryAdd(executablePath, processName)) LogRuntimeAutoBlacklist(processName, executablePath);

        QueueAutoBlacklistPersistence(processName, executablePath);
        return true;
    }

    public bool IsAutoBlacklisted(ForegroundProcessSnapshot foregroundProcessSnapshot)
    {
        var executablePath = foregroundProcessSnapshot.ExecutablePath;
        return !string.IsNullOrWhiteSpace(executablePath)
            && s_autoBlacklistedGameBarExecutablePaths.ContainsKey(executablePath);
    }

    private static bool IsGameBarRecognizedGame(string targetExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(targetExecutablePath)) return false;

        return s_gameBarRecognizedGameCache.GetOrAdd(targetExecutablePath, static executablePath =>
        {
            using var gameConfigStoreKey = Registry.CurrentUser.OpenSubKey(GameBarRegistryPath);
            if (gameConfigStoreKey is null) return false;

            foreach (var subKeyName in gameConfigStoreKey.GetSubKeyNames())
            {
                using var childKey = gameConfigStoreKey.OpenSubKey(subKeyName);
                if (childKey is null) continue;

                var matchedExecutablePath = childKey.GetValue("MatchedExeFullPath") as string;
                if (!string.Equals(matchedExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)) continue;

                var flagsValue = childKey.GetValue("Flags");
                if (flagsValue is int flags) return flags > 0;
            }

            return false;
        });
    }

    private void QueueAutoBlacklistPersistence(string processName, string executablePath)
    {
        if (!s_persistingGameBarExecutablePaths.TryAdd(executablePath, 0)) return;

        _ = PersistAutoBlacklistedProcessAsync(processName, executablePath);
    }

    private async Task PersistAutoBlacklistedProcessAsync(string processName, string executablePath)
    {
        try
        {
            var currentSettings = _settingsService.Settings;
            if (IsProcessNameListed(currentSettings.WhitelistedProcessNames, processName) || IsProcessNameListed(currentSettings.BlacklistedProcessNames, processName)) return;

            await _settingsService.UpdateSettingsAsync(currentSettings with
            {
                BlacklistedProcessNames = [.. currentSettings.BlacklistedProcessNames, processName]
            });
            LogPersistedAutoBlacklist(processName, executablePath);
        }
        catch (ArgumentException exception) { LogInvalidProcessNamePersistenceFailure(exception); }
        catch (InvalidOperationException exception) { LogRejectedSettingsPersistenceFailure(exception); }
        finally { _ = s_persistingGameBarExecutablePaths.TryRemove(executablePath, out _); }
    }

    private static bool IsProcessNameListed(IReadOnlyList<string> processNames, string processName) => processNames.Contains(processName, StringComparer.OrdinalIgnoreCase);

    private static bool TryGetForegroundProcessIdentity(ForegroundProcessSnapshot foregroundProcessSnapshot, out string processName, out string executablePath)
    {
        processName = foregroundProcessSnapshot.ProcessName ?? string.Empty;
        executablePath = foregroundProcessSnapshot.ExecutablePath ?? string.Empty;
        return !string.IsNullOrWhiteSpace(processName) && !string.IsNullOrWhiteSpace(executablePath);
    }

    private void LogInvalidProcessNamePersistenceFailure(Exception exception) => _fileLogService.WriteWarning(nameof(GameBarProcessBlacklistService), "Failed to persist Game Bar recognized foreground process to blacklist because the process name was invalid.", exception);

    private void LogPersistedAutoBlacklist(string processName, string executablePath) => _fileLogService.WriteInformation(
        nameof(GameBarProcessBlacklistService),
        $"Persisted Game Bar recognized foreground process to blacklist. ProcessName={processName}, ExecutablePath={executablePath}.");

    private void LogRejectedSettingsPersistenceFailure(Exception exception) => _fileLogService.WriteWarning(nameof(GameBarProcessBlacklistService), "Failed to persist Game Bar recognized foreground process to blacklist because settings update was rejected.", exception);

    private void LogRuntimeAutoBlacklist(string processName, string executablePath) => _fileLogService.WriteInformation(
        nameof(GameBarProcessBlacklistService),
        $"Auto-registered Game Bar recognized foreground process to the runtime blacklist. ProcessName={processName}, ExecutablePath={executablePath}.");
}
