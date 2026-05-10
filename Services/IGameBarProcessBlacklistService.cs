using DeskBorder.Models;

namespace DeskBorder.Services;

public interface IGameBarProcessBlacklistService
{
    bool TryAutoBlacklistForegroundProcess(DeskBorderSettings currentSettings, ForegroundProcessSnapshot foregroundProcessSnapshot);

    bool IsAutoBlacklisted(ForegroundProcessSnapshot foregroundProcessSnapshot);
}
