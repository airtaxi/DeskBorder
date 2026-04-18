using DeskBorder.Models;
using DeskBorder.ViewModels;
using DeskBorder.Views;

namespace DeskBorder.Services;

public interface INavigatorService
{
    bool IsInitialized { get; }

    bool IsVisible { get; }

    NavigatorViewModel ViewModel { get; }

    void CloseFromKeyboard();

    void Hide();

    void Initialize(NavigatorWindow navigatorWindow);

    void RefreshPreview();

    bool ShowFromTriggerArea();

    void ToggleOverlay();

    void UpdateTriggerAreaState(bool isEnabled, TriggerRectangleSettings triggerRectangleSettings);

    bool UpdateTriggerAreaPointerState(bool isPointerInsideTriggerArea);
}
