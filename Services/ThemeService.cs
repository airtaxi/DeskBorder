using DeskBorder.Models;
using Microsoft.UI.Xaml;

namespace DeskBorder.Services;

public sealed class ThemeService : IThemeService
{
    private readonly List<WeakReference<FrameworkElement>> _registeredFrameworkElements = [];
    private readonly Lock _registeredFrameworkElementsLock = new();

    public ApplicationThemePreference CurrentApplicationThemePreference { get; private set; } = ApplicationThemePreference.System;

    public void ApplyApplicationThemePreference(ApplicationThemePreference applicationThemePreference)
    {
        CurrentApplicationThemePreference = applicationThemePreference;
        var requestedTheme = ConvertToElementTheme(applicationThemePreference);
        foreach (var registeredFrameworkElement in GetRegisteredFrameworkElementsSnapshot())
            ApplyRequestedTheme(registeredFrameworkElement, requestedTheme);
    }

    public void RegisterFrameworkElement(FrameworkElement frameworkElement)
    {
        ArgumentNullException.ThrowIfNull(frameworkElement);

        lock (_registeredFrameworkElementsLock)
        {
            _registeredFrameworkElements.RemoveAll(static registeredFrameworkElementReference => !registeredFrameworkElementReference.TryGetTarget(out _));
            if (!_registeredFrameworkElements.Exists(registeredFrameworkElementReference => registeredFrameworkElementReference.TryGetTarget(out var registeredFrameworkElement) && ReferenceEquals(registeredFrameworkElement, frameworkElement)))
                _registeredFrameworkElements.Add(new(frameworkElement));
        }

        ApplyRequestedTheme(frameworkElement, ConvertToElementTheme(CurrentApplicationThemePreference));
    }

    private static void ApplyRequestedTheme(FrameworkElement frameworkElement, ElementTheme requestedTheme)
    {
        if (frameworkElement.DispatcherQueue.TryEnqueue(() => frameworkElement.RequestedTheme = requestedTheme))
            return;

        frameworkElement.RequestedTheme = requestedTheme;
    }

    private static ElementTheme ConvertToElementTheme(ApplicationThemePreference applicationThemePreference) => applicationThemePreference switch
    {
        ApplicationThemePreference.Light => ElementTheme.Light,
        ApplicationThemePreference.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    private List<FrameworkElement> GetRegisteredFrameworkElementsSnapshot()
    {
        lock (_registeredFrameworkElementsLock)
        {
            _registeredFrameworkElements.RemoveAll(static registeredFrameworkElementReference => !registeredFrameworkElementReference.TryGetTarget(out _));
            return [.. _registeredFrameworkElements
                .Select(static registeredFrameworkElementReference => registeredFrameworkElementReference.TryGetTarget(out var registeredFrameworkElement) ? registeredFrameworkElement : null)
                .OfType<FrameworkElement>()];
        }
    }
}
