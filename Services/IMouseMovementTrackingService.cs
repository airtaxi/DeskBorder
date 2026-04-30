namespace DeskBorder.Services;

public readonly record struct MouseMovementDelta(int HorizontalPixels, int VerticalPixels);

public interface IMouseMovementTrackingService
{
    bool IsRawInputRegistered { get; }

    MouseMovementDelta ConsumePendingMouseMovementDelta();

    void ProcessRawInputMessage(nint rawInputHandle);

    void RegisterWindowHandle(nint windowHandle);

    void SetRawInputTrackingEnabled(bool isEnabled);
}
