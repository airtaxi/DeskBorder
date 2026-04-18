using DeskBorder.Models;

namespace DeskBorder.Services;

public interface ILocalizationService
{
    event EventHandler? LanguageChanged;

    AppLanguagePreference CurrentLanguagePreference { get; }

    void ApplyLanguagePreference(AppLanguagePreference appLanguagePreference);

    string GetFormattedString(string resourceName, params object[] arguments);

    string GetString(string resourceName);
}
