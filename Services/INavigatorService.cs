using DeskBorder.Models;
using DeskBorder.ViewModels;
using DeskBorder.Views;

namespace DeskBorder.Services;

public interface INavigatorService
{
    event EventHandler<NavigatorDesktopSelectionRequestedEventArgs>? DesktopSelectionRequested;

    bool IsInitialized { get; }

    bool IsVisible { get; }

    NavigatorViewModel ViewModel { get; }

    void CloseFromKeyboard();

    void Hide();

    void Initialize(NavigatorWindow navigatorWindow);

    void NotifyWindowActivated();

    void NotifyWindowDeactivated();

    void RequestDesktopSelection(string desktopIdentifier);

    void SetCurrentDesktop(string desktopIdentifier);

    void SetDesktopItems(IReadOnlyList<NavigatorDesktopItemModel> desktopItems, string? currentDesktopIdentifier = null);

    void SetDesktopPlaceholders(int desktopCount, int currentDesktopNumber);

    bool ShowFromTriggerArea();

    void ShowOverlay();

    void ToggleOverlay();

    void UpdateTriggerAreaState(bool isEnabled, TriggerRectangleSettings triggerRectangleSettings);

    bool UpdateTriggerAreaPointerState(bool isPointerInsideTriggerArea);
}
