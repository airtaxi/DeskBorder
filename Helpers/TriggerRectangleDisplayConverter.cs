namespace DeskBorder.Helpers;

public static class TriggerRectangleDisplayConverter
{
    public static double ConvertDisplayPercentageToNormalizedLength(double displayPercentage) => displayPercentage / 100.0;

    public static double ConvertDisplayPercentageToNormalizedOffset(double displayPercentage, double normalizedLength) => displayPercentage / 100.0;

    public static double ConvertNormalizedLengthToDisplayPercentage(double normalizedLength) => normalizedLength * 100.0;

    public static double ConvertNormalizedOffsetToDisplayPercentage(double normalizedOffset, double normalizedLength) => normalizedOffset * 100.0;
}
