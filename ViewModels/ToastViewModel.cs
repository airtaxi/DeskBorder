using CommunityToolkit.Mvvm.ComponentModel;

namespace DeskBorder.ViewModels;

public sealed partial class ToastViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string ActionButtonText { get; set; } = "Undo";

    [ObservableProperty]
    public partial string ActionCardTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;
}
