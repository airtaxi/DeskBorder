using DeskBorder.Models;

namespace DeskBorder.Services;

public interface IToastService
{
    bool IsToastVisible { get; }

    Task<ToastPresentationResult> ShowToastAsync(ToastPresentationOptions toastPresentationOptions, CancellationToken cancellationToken = default);

    Task DismissAsync();
}
