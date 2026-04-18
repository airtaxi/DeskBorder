namespace DeskBorder.Services;

public interface IApplicationBootstrapService
{
    Task InitializeAsync(bool shouldActivateManageWindow);
}
