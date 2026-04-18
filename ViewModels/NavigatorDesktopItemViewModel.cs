using CommunityToolkit.Mvvm.ComponentModel;
using DeskBorder.Models;
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;

namespace DeskBorder.ViewModels;

public sealed partial class NavigatorDesktopItemViewModel : ObservableObject
{
    public NavigatorDesktopItemViewModel(NavigatorDesktopItemModel navigatorDesktopItemModel, int previewCanvasWidth, int previewCanvasHeight)
    {
        DesktopIdentifier = navigatorDesktopItemModel.DesktopIdentifier;
        DisplayName = navigatorDesktopItemModel.DisplayName;
        IsCurrentDesktop = navigatorDesktopItemModel.IsCurrentDesktop;
        PreviewCanvasWidth = previewCanvasWidth;
        PreviewCanvasHeight = previewCanvasHeight;
        foreach (var navigatorDesktopWindowItemModel in navigatorDesktopItemModel.WindowItems)
            PreviewWindowItems.Add(new NavigatorDesktopWindowItemViewModel(navigatorDesktopWindowItemModel, previewCanvasWidth, previewCanvasHeight));
    }

    public Visibility CurrentDesktopBadgeVisibility => IsCurrentDesktop ? Visibility.Visible : Visibility.Collapsed;

    public double CurrentDesktopHighlightOpacity => IsCurrentDesktop ? 1.0 : 0.0;

    public double DesktopCardOpacity => IsCurrentDesktop || PreviewWindowItems.Count > 0 ? 1.0 : 0.65;

    public ObservableCollection<NavigatorDesktopWindowItemViewModel> PreviewWindowItems { get; } = [];

    public int PreviewCanvasHeight { get; }

    public int PreviewCanvasWidth { get; }

    [ObservableProperty]
    public partial string DesktopIdentifier { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentDesktopBadgeVisibility))]
    [NotifyPropertyChangedFor(nameof(CurrentDesktopHighlightOpacity))]
    [NotifyPropertyChangedFor(nameof(DesktopCardOpacity))]
    public partial bool IsCurrentDesktop { get; set; }
}
