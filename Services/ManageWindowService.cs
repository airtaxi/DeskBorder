using DeskBorder.Views;

namespace DeskBorder.Services;

public sealed class ManageWindowService : IManageWindowService
{
    private ManageWindow? _manageWindow;

    public bool IsInitialized => _manageWindow is not null;

    public void ForceClose()
    {
        if (_manageWindow is not null)
            _manageWindow.ForceClose();
    }

    public void Hide()
    {
        if (_manageWindow is not null)
            _manageWindow.AppWindow.Hide();
    }

    public void Initialize(ManageWindow manageWindow)
    {
        if (_manageWindow is not null)
            return;

        _manageWindow = manageWindow;
    }

    public void Show()
    {
        if (_manageWindow is null)
            return;

        if (!_manageWindow.AppWindow.IsVisible)
            _manageWindow.AppWindow.Show();

        _manageWindow.Activate();
        _manageWindow.BringToFront();
    }
}
