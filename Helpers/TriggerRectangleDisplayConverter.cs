namespace DeskBorder.Helpers;

public static class TriggerRectangleDisplayConverter
{
    public static double ConvertDisplayPercentageToNormalizedLength(double displayPercentage) => displayPercentage / 100.0;

    public static double ConvertDisplayPercentageToNormalizedOffset(double displayPercentage, double normalizedLength)
    {
        if (normalizedLength >= 1.0)
            return 0.0;

        var normalizedDisplay = displayPercentage / 100.0;
        var normalizedOffset = normalizedDisplay > 0.5
            ? normalizedDisplay - normalizedLength
            : normalizedDisplay;
        return Math.Clamp(normalizedOffset, 0.0, 1.0 - normalizedLength);
    }

    public static double ConvertNormalizedLengthToDisplayPercentage(double normalizedLength) => normalizedLength * 100.0;

    public static double ConvertNormalizedOffsetToDisplayPercentage(double normalizedOffset, double normalizedLength)
    {
        if (normalizedLength >= 1.0)
            return 100.0;

        var centerPosition = normalizedOffset + normalizedLength / 2.0;
        var normalizedDisplay = centerPosition > 0.5
            ? normalizedOffset + normalizedLength
            : normalizedOffset;
        return normalizedDisplay * 100.0;
    }
}
