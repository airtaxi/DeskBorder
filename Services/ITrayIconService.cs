namespace DeskBorder.Services;

public interface ITrayIconService
{
    event EventHandler? StateChanged;

    bool IsLaunchOnStartupEnabled { get; }

    bool IsRuntimeEnabled { get; }

    bool IsStoreUpdateCheckEnabled { get; }

    string LaunchOnStartupToggleText { get; }

    string RuntimeStatusText { get; }

    string RuntimeToggleText { get; }

    string StoreUpdateCheckToggleText { get; }

    void RefreshState();
}
