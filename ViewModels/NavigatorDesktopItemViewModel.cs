using CommunityToolkit.Mvvm.ComponentModel;
using DeskBorder.Helpers;
using DeskBorder.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskBorder.ViewModels;

public sealed partial class NavigatorDesktopItemViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;

    public NavigatorDesktopItemViewModel(
        string desktopIdentifier,
        string displayName,
        string description,
        Symbol iconSymbol,
        ILocalizationService localizationService)
    {
        DesktopIdentifier = desktopIdentifier;
        DisplayName = displayName;
        Description = description;
        IconSymbol = iconSymbol;
        _localizationService = localizationService;
        _localizationService.LanguageChanged += OnLocalizationServiceLanguageChanged;
    }

    public double CurrentDesktopHighlightOpacity => IsCurrentDesktop ? 1.0 : 0.0;

    public Visibility CurrentDesktopBadgeVisibility => IsCurrentDesktop ? Visibility.Visible : Visibility.Collapsed;

    public string CurrentDesktopBadgeText => _localizationService.GetString("Navigator.CurrentBadgeText");

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DesktopIdentifier { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Symbol IconSymbol { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentDesktopBadgeVisibility))]
    [NotifyPropertyChangedFor(nameof(CurrentDesktopHighlightOpacity))]
    public partial bool IsCurrentDesktop { get; set; }

    private void OnLocalizationServiceLanguageChanged(object? sender, EventArgs eventArguments)
    {
        _ = sender;
        _ = eventArguments;
        OnPropertyChanged(nameof(CurrentDesktopBadgeText));
    }
}
