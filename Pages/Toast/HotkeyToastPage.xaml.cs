using DeskBorder.Models;

namespace DeskBorder.Pages.Toast;

public sealed partial class HotkeyToastPage : ToastPageBase
{
    public HotkeyToastPage(HotkeyToastPresentationOptions hotkeyToastPresentationOptions)
    {
        PresentationOptions = hotkeyToastPresentationOptions;
        InitializeComponent();
    }

    public HotkeyToastPresentationOptions PresentationOptions { get; }
}
