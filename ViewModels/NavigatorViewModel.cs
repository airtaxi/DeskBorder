using CommunityToolkit.Mvvm.ComponentModel;
using DeskBorder.Helpers;
using DeskBorder.Models;
using DeskBorder.Services;
using System.Collections.ObjectModel;

namespace DeskBorder.ViewModels;

public sealed partial class NavigatorViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;

    public NavigatorViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
        _localizationService.LanguageChanged += OnLocalizationServiceLanguageChanged;
    }

    public ObservableCollection<NavigatorDesktopItemViewModel> DesktopItems { get; } = [];

    public string InteractionStatusText => IsVisible
        ? IsWindowActive
            ? LocalizedResourceAccessor.GetString("Navigator.Interaction.Active")
            : LocalizedResourceAccessor.GetString("Navigator.Interaction.Visible")
        : LocalizedResourceAccessor.GetString("Navigator.Interaction.Hidden");

    [ObservableProperty]
    public partial bool IsPointerInsideTriggerArea { get; set; }

    [ObservableProperty]
    public partial bool IsTriggerAreaEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InteractionStatusText))]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InteractionStatusText))]
    public partial bool IsWindowActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastRequestedDesktopDisplayText))]
    public partial string? LastRequestedDesktopIdentifier { get; set; }

    [ObservableProperty]
    public partial string TriggerAreaDescription { get; set; } = LocalizedResourceAccessor.GetString("Navigator.TriggerAreaDisabled");

    public string LastRequestedDesktopDisplayText => LastRequestedDesktopIdentifier ?? LocalizedResourceAccessor.GetString("Navigator.LastRequestedDesktop.None");

    public void ReplaceDesktopItems(IEnumerable<NavigatorDesktopItemModel> desktopItems, string? currentDesktopIdentifier)
    {
        DesktopItems.Clear();

        foreach (var desktopItem in desktopItems)
        {
            DesktopItems.Add(new NavigatorDesktopItemViewModel(
                desktopItem.DesktopIdentifier,
                desktopItem.DisplayName,
                desktopItem.Description,
                desktopItem.IconSymbol,
                _localizationService)
            {
                IsCurrentDesktop = string.Equals(desktopItem.DesktopIdentifier, currentDesktopIdentifier, StringComparison.Ordinal)
            });
        }
    }

    private void OnLocalizationServiceLanguageChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        if (!IsTriggerAreaEnabled)
            TriggerAreaDescription = LocalizedResourceAccessor.GetString("Navigator.TriggerAreaDisabled");

        OnPropertyChanged(nameof(InteractionStatusText));
        if (LastRequestedDesktopIdentifier is null)
            OnPropertyChanged(nameof(LastRequestedDesktopDisplayText));
    }
}
