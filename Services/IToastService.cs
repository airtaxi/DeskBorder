using DeskBorder.Models;

namespace DeskBorder.Services;

public interface IToastService
{
    nint ActiveToastWindowHandle { get; }

    bool IsToastVisible { get; }

    Task<ToastPresentationResult> ShowToastAsync(ToastPresentationOptions toastPresentationOptions, CancellationToken cancellationToken = default);

    Task DismissAsync();
}
