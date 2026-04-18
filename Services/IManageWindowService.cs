namespace DeskBorder.Services;

public interface IManageWindowService
{
    bool IsInitialized { get; }

    void ForceClose();

    void Hide();

    void Initialize(Views.ManageWindow manageWindow);

    void Show();
}
