using CommunityToolkit.Mvvm.ComponentModel;
using DeskBorder.Models;
using System.Collections.ObjectModel;

namespace DeskBorder.ViewModels;

public sealed partial class NavigatorViewModel : ObservableObject
{
    public ObservableCollection<NavigatorDesktopItemViewModel> DesktopItems { get; } = [];

    [ObservableProperty]
    public partial bool IsPointerInsideTriggerArea { get; set; }

    [ObservableProperty]
    public partial bool IsTriggerAreaEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial int PreviewCanvasWidth { get; set; } = 1920;

    [ObservableProperty]
    public partial int PreviewCanvasHeight { get; set; } = 1080;

    public void ReplaceDesktopItems(IEnumerable<NavigatorDesktopItemModel> desktopItems, int previewCanvasWidth, int previewCanvasHeight)
    {
        PreviewCanvasWidth = Math.Max(1, previewCanvasWidth);
        PreviewCanvasHeight = Math.Max(1, previewCanvasHeight);

        DesktopItems.Clear();
        foreach (var desktopItem in desktopItems)
            DesktopItems.Add(new NavigatorDesktopItemViewModel(desktopItem, PreviewCanvasWidth, PreviewCanvasHeight));
    }
}
