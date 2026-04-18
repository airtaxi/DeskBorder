using DeskBorder.Navigation;
using Microsoft.UI.Xaml.Controls;

namespace DeskBorder.Services;

public interface IManageNavigationService
{
    bool CanGoBack { get; }

    ManageNavigationTarget? CurrentNavigationTarget { get; }

    void GoBack();

    bool NavigateTo(ManageNavigationTarget manageNavigationTarget, object? navigationParameter = null, bool clearBackStack = false);

    void ReloadCurrentPage();

    void RegisterFrame(Frame navigationFrame);
}
