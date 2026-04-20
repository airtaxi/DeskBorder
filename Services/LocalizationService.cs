using DeskBorder.Models;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DeskBorder.Services;

public sealed class LocalizationService(IFileLogService fileLogService) : ILocalizationService
{
    private static readonly List<string> s_installedLanguages = ["en-US", "ko-KR", "ja-JP", "zh-Hans", "zh-Hant"];
    private readonly IFileLogService _fileLogService = fileLogService;

    public event EventHandler? LanguageChanged;

    public AppLanguagePreference CurrentLanguagePreference { get; set; } = AppLanguagePreference.System;

    public void ApplyLanguagePreference(AppLanguagePreference appLanguagePreference)
    {
        if (CurrentLanguagePreference == appLanguagePreference && string.Equals(ApplicationLanguages.PrimaryLanguageOverride, GetLanguageTag(appLanguagePreference), StringComparison.Ordinal)) return;

        ApplyLanguagePreferenceOverride(appLanguagePreference);
        App.ResourceLoader = new ResourceLoader();
        CurrentLanguagePreference = appLanguagePreference;
        _fileLogService.WriteInformation(nameof(LocalizationService), $"Applied language preference {appLanguagePreference}.");
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public static void ApplyLanguagePreferenceOverride(AppLanguagePreference appLanguagePreference)
    {
        var languageTag = GetLanguageTag(appLanguagePreference);
        ApplicationLanguages.PrimaryLanguageOverride = languageTag;
        ApplyCurrentThreadCultures(languageTag);
    }

    public string GetFormattedString(string resourceName, params object[] arguments) => string.Format(CultureInfo.CurrentCulture, GetString(resourceName), arguments);

    public string GetString(string resourceName)
    {
        var normalizedResourceName = resourceName.Replace('.', '/');
        string localizedString;
        try { localizedString = App.ResourceLoader.GetString(normalizedResourceName); }
        catch (COMException exception)
        {
            _fileLogService.WriteWarning(nameof(LocalizationService), $"Failed to load resource '{resourceName}'. Falling back to the resource name.", exception);
            localizedString = resourceName;
        }

        return string.IsNullOrWhiteSpace(localizedString) ? resourceName : localizedString;
    }

    private static void ApplyCurrentThreadCultures(string languageTag)
    {
        var resolvedLanguageTag = string.IsNullOrWhiteSpace(languageTag)
            ? ApplicationLanguages.Languages[0]
            : languageTag;
        if (string.IsNullOrWhiteSpace(resolvedLanguageTag))
            return;

        try
        {
            var cultureInfo = CultureInfo.GetCultureInfo(resolvedLanguageTag);
            CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
            CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
        }
        catch (CultureNotFoundException) { }
    }

    private static string GetLanguageTag(AppLanguagePreference appLanguagePreference) => appLanguagePreference switch
    {
        AppLanguagePreference.System => GetDefaultLanguageTag(),
        AppLanguagePreference.Korean => "ko-KR",
        AppLanguagePreference.English => "en-US",
        AppLanguagePreference.Japanese => "ja-JP",
        AppLanguagePreference.ChineseSimplified => "zh-Hans",
        AppLanguagePreference.ChineseTraditional => "zh-Hant",
        _ => throw new ArgumentException($"Unsupported language preference: {appLanguagePreference}", nameof(appLanguagePreference)),
    };

    private static string GetDefaultLanguageTag()
    {
        var installedUserInterfaceCultureName = CultureInfo.InstalledUICulture.Name;
        return s_installedLanguages.Contains(installedUserInterfaceCultureName) ? installedUserInterfaceCultureName : s_installedLanguages.First();
    }
}
