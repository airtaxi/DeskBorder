using DeskBorder.Views;

namespace DeskBorder.Services;

public sealed class ManageWindowService(IFileLogService fileLogService) : IManageWindowService
{
    private readonly IFileLogService _fileLogService = fileLogService;
    private ManageWindow? _manageWindow;

    public bool IsInitialized => _manageWindow is not null;

    public void ForceClose()
    {
        _manageWindow?.ForceClose();
        _fileLogService.WriteInformation(nameof(ManageWindowService), "Forced the manage window to close.");
    }

    public void Hide()
    {
        _manageWindow?.AppWindow.Hide();
        _fileLogService.WriteInformation(nameof(ManageWindowService), "Hid the manage window.");
    }

    public void Initialize(ManageWindow manageWindow)
    {
        if (_manageWindow is not null)
            return;

        _manageWindow = manageWindow;
        _fileLogService.WriteInformation(nameof(ManageWindowService), "Initialized the manage window service.");
    }

    public void Show()
    {
        if (_manageWindow is null)
            return;

        if (!_manageWindow.AppWindow.IsVisible)
            _manageWindow.AppWindow.Show();

        _manageWindow.Activate();
        _manageWindow.BringToFront();
        _fileLogService.WriteInformation(nameof(ManageWindowService), "Displayed the manage window.");
    }
}
