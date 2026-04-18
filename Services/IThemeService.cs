using DeskBorder.Models;
using Microsoft.UI.Xaml;

namespace DeskBorder.Services;

public interface IThemeService
{
    ApplicationThemePreference CurrentApplicationThemePreference { get; }

    void ApplyApplicationThemePreference(ApplicationThemePreference applicationThemePreference);

    void RegisterFrameworkElement(FrameworkElement frameworkElement);
}
