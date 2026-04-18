using DeskBorder.Services;

namespace DeskBorder.Helpers;

public static class LocalizedResourceAccessor
{
    public static string GetFormattedString(string resourceName, params object[] arguments) => App.GetRequiredService<ILocalizationService>().GetFormattedString(resourceName, arguments);

    public static string GetString(string resourceName) => App.GetRequiredService<ILocalizationService>().GetString(resourceName);
}
