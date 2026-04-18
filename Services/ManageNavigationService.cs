using DeskBorder.Navigation;
using DeskBorder.Pages;
using Microsoft.UI.Xaml.Controls;

namespace DeskBorder.Services;

public sealed class ManageNavigationService : IManageNavigationService
{
    private static readonly Dictionary<ManageNavigationTarget, Type> s_pageTypes = new()
    {
        [ManageNavigationTarget.Dashboard] = typeof(DashboardPage),
        [ManageNavigationTarget.Settings] = typeof(SettingsPage)
    };

    private Frame? _navigationFrame;

    public bool CanGoBack => _navigationFrame?.CanGoBack == true;

    public ManageNavigationTarget? CurrentNavigationTarget => TryGetCurrentNavigationTarget(out var currentNavigationTarget) ? currentNavigationTarget : null;

    public void GoBack()
    {
        if (_navigationFrame?.CanGoBack == true)
            _navigationFrame.GoBack();
    }

    public bool NavigateTo(ManageNavigationTarget manageNavigationTarget, object? navigationParameter = null, bool clearBackStack = false)
    {
        if (_navigationFrame is null)
            return false;

        var targetPageType = s_pageTypes[manageNavigationTarget];
        if (_navigationFrame.CurrentSourcePageType == targetPageType)
            return false;

        var didNavigate = _navigationFrame.Navigate(targetPageType, navigationParameter);
        if (didNavigate && clearBackStack)
            _navigationFrame.BackStack.Clear();

        return didNavigate;
    }

    public void RegisterFrame(Frame navigationFrame)
    {
        if (ReferenceEquals(_navigationFrame, navigationFrame))
            return;

        _navigationFrame = navigationFrame;
    }

    public void ReloadCurrentPage()
    {
        if (_navigationFrame?.CurrentSourcePageType is not { } currentPageType)
            return;

        var previousBackStackCount = _navigationFrame.BackStack.Count;
        _ = _navigationFrame.Navigate(currentPageType);
        while (_navigationFrame.BackStack.Count > previousBackStackCount)
            _navigationFrame.BackStack.RemoveAt(_navigationFrame.BackStack.Count - 1);
    }

    private bool TryGetCurrentNavigationTarget(out ManageNavigationTarget currentNavigationTarget)
    {
        if (_navigationFrame?.Content is null)
        {
            currentNavigationTarget = default;
            return false;
        }

        foreach (var pageTypeEntry in s_pageTypes)
        {
            if (pageTypeEntry.Value != _navigationFrame.Content.GetType())
                continue;

            currentNavigationTarget = pageTypeEntry.Key;
            return true;
        }

        currentNavigationTarget = default;
        return false;
    }
}
