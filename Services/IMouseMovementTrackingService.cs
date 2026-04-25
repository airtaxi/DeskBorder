namespace DeskBorder.Services;

public readonly record struct MouseMovementDelta(int HorizontalPixels, int VerticalPixels);

public interface IMouseMovementTrackingService
{
    MouseMovementDelta ConsumePendingMouseMovementDelta();

    void ProcessRawInputMessage(nint rawInputHandle);

    void RegisterWindowHandle(nint windowHandle);
}
