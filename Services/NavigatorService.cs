using DeskBorder.Helpers;
using DeskBorder.Models;
using DeskBorder.ViewModels;
using DeskBorder.Views;
using Microsoft.UI.Xaml.Controls;

namespace DeskBorder.Services;

public sealed class NavigatorService(ILocalizationService localizationService) : INavigatorService
{
    private readonly ILocalizationService _localizationService = localizationService;
    private NavigatorWindow? _navigatorWindow;

    public event EventHandler<NavigatorDesktopSelectionRequestedEventArgs>? DesktopSelectionRequested;

    public bool IsInitialized => _navigatorWindow is not null;

    public bool IsVisible => ViewModel.IsVisible;

    public NavigatorViewModel ViewModel { get; } = new(localizationService);

    public void CloseFromKeyboard() => Hide();

    public void Hide()
    {
        ViewModel.IsVisible = false;
        ViewModel.IsWindowActive = false;

        if (_navigatorWindow?.AppWindow.IsVisible == true)
            _navigatorWindow.AppWindow.Hide();
    }

    public void Initialize(NavigatorWindow navigatorWindow)
    {
        if (_navigatorWindow is not null)
            return;

        _navigatorWindow = navigatorWindow;
        SetDesktopPlaceholders(4, 1);
    }

    public void NotifyWindowActivated() => ViewModel.IsWindowActive = true;

    public void NotifyWindowDeactivated()
    {
        ViewModel.IsWindowActive = false;
        if (!ViewModel.IsPointerInsideTriggerArea)
            Hide();
    }

    public void RequestDesktopSelection(string desktopIdentifier)
    {
        if (!ViewModel.DesktopItems.Any(desktopItem => string.Equals(desktopItem.DesktopIdentifier, desktopIdentifier, StringComparison.Ordinal)))
            return;

        ViewModel.LastRequestedDesktopIdentifier = desktopIdentifier;
        DesktopSelectionRequested?.Invoke(this, new NavigatorDesktopSelectionRequestedEventArgs(desktopIdentifier));
        Hide();
    }

    public void SetCurrentDesktop(string desktopIdentifier)
    {
        foreach (var desktopItem in ViewModel.DesktopItems)
            desktopItem.IsCurrentDesktop = string.Equals(desktopItem.DesktopIdentifier, desktopIdentifier, StringComparison.Ordinal);
    }

    public void SetDesktopItems(IReadOnlyList<NavigatorDesktopItemModel> desktopItems, string? currentDesktopIdentifier = null)
    {
        var resolvedCurrentDesktopIdentifier = currentDesktopIdentifier ?? desktopItems.FirstOrDefault()?.DesktopIdentifier;
        ViewModel.ReplaceDesktopItems(desktopItems, resolvedCurrentDesktopIdentifier);
    }

    public void SetDesktopPlaceholders(int desktopCount, int currentDesktopNumber)
    {
        var sanitizedDesktopCount = Math.Max(1, desktopCount);
        var sanitizedCurrentDesktopNumber = Math.Clamp(currentDesktopNumber, 1, sanitizedDesktopCount);
        var desktopItems = Enumerable.Range(1, sanitizedDesktopCount)
            .Select(desktopNumber => new NavigatorDesktopItemModel
            {
                DesktopIdentifier = $"desktop-{desktopNumber}",
                DisplayName = SettingsDisplayFormatter.FormatDesktopDisplayName(desktopNumber),
                Description = desktopNumber == sanitizedCurrentDesktopNumber
                    ? LocalizedResourceAccessor.GetString("Navigator.DesktopDescription.PlaceholderCurrent")
                    : LocalizedResourceAccessor.GetString("Navigator.DesktopDescription.PlaceholderPreview"),
                IconSymbol = desktopNumber == sanitizedCurrentDesktopNumber ? Symbol.Switch : Symbol.AllApps
            })
            .ToArray();

        SetDesktopItems(desktopItems, $"desktop-{sanitizedCurrentDesktopNumber}");
    }

    public bool ShowFromTriggerArea()
    {
        if (!ViewModel.IsTriggerAreaEnabled)
            return false;

        ShowOverlay();
        return true;
    }

    public void ShowOverlay()
    {
        ViewModel.IsVisible = true;
        _navigatorWindow?.ShowOverlay();
    }

    public void ToggleOverlay()
    {
        if (IsVisible)
            Hide();
        else
            ShowOverlay();
    }

    public void UpdateTriggerAreaState(bool isEnabled, TriggerRectangleSettings triggerRectangleSettings)
    {
        ViewModel.IsTriggerAreaEnabled = isEnabled;
        ViewModel.TriggerAreaDescription = isEnabled
            ? LocalizedResourceAccessor.GetFormattedString("Navigator.TriggerAreaEnabledFormat", SettingsDisplayFormatter.FormatTriggerRectangle(triggerRectangleSettings))
            : LocalizedResourceAccessor.GetString("Navigator.TriggerAreaDisabled");
    }

    public bool UpdateTriggerAreaPointerState(bool isPointerInsideTriggerArea)
    {
        if (ViewModel.IsPointerInsideTriggerArea == isPointerInsideTriggerArea)
            return false;

        ViewModel.IsPointerInsideTriggerArea = isPointerInsideTriggerArea;

        if (isPointerInsideTriggerArea)
            return ShowFromTriggerArea();

        if (!ViewModel.IsWindowActive)
            Hide();

        return true;
    }
}
