namespace DeskBorder.Services;

public interface IHotkeyService : IDisposable
{
    event EventHandler<HotkeyInvokedEventArgs>? HotkeyInvoked;
    event EventHandler? RegistrationStateChanged;

    bool IsInitialized { get; }

    string? GetRegistrationFailureMessage(HotkeyActionType hotkeyActionType);

    void Initialize();

    void RefreshRegisteredHotkeys();
}
