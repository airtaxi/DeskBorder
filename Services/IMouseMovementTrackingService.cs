namespace DeskBorder.Services;

public interface IMouseMovementTrackingService
{
    int ConsumePendingHorizontalMovement();

    void ProcessRawInputMessage(nint rawInputHandle);

    void RegisterWindowHandle(nint windowHandle);
}
