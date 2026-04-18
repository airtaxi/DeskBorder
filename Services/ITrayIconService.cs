namespace DeskBorder.Services;

public interface ITrayIconService
{
    event EventHandler? StateChanged;

    bool IsLaunchOnStartupEnabled { get; }

    bool IsRuntimeEnabled { get; }

    bool IsStoreUpdateCheckEnabled { get; }

    string LaunchOnStartupActionText { get; }

    string RuntimeStatusText { get; }

    string RuntimeActionText { get; }

    string StoreUpdateCheckActionText { get; }

    void RefreshState();
}
