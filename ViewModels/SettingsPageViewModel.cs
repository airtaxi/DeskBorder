using CommunityToolkit.Mvvm.ComponentModel;
using DeskBorder.Helpers;
using DeskBorder.Models;
using DeskBorder.Services;
using System.ComponentModel;
using System.Collections.ObjectModel;
using Windows.System;

namespace DeskBorder.ViewModels;

public enum KeyboardShortcutValidationState
{
    Disabled,
    Valid,
    MissingKey,
    Duplicate,
    RegistrationFailed,
}

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private static readonly ApplicationThemePreference[] s_applicationThemePreferences =
    [
        ApplicationThemePreference.System,
        ApplicationThemePreference.Light,
        ApplicationThemePreference.Dark
    ];

    private static readonly AppLanguagePreference[] s_appLanguagePreferences =
    [
        AppLanguagePreference.System,
        AppLanguagePreference.Korean,
        AppLanguagePreference.English,
        AppLanguagePreference.Japanese,
        AppLanguagePreference.ChineseSimplified,
        AppLanguagePreference.ChineseTraditional
    ];

    private static readonly MultiDisplayBehavior[] s_multiDisplayBehaviors =
    [
        MultiDisplayBehavior.DisableInMultiDisplayEnvironment,
        MultiDisplayBehavior.UseOuterDisplayEdges
    ];

    private static readonly VirtualKey[] s_virtualKeys =
    [
        VirtualKey.None,
        VirtualKey.A,
        VirtualKey.B,
        VirtualKey.C,
        VirtualKey.D,
        VirtualKey.E,
        VirtualKey.F,
        VirtualKey.G,
        VirtualKey.H,
        VirtualKey.I,
        VirtualKey.J,
        VirtualKey.K,
        VirtualKey.L,
        VirtualKey.M,
        VirtualKey.N,
        VirtualKey.O,
        VirtualKey.P,
        VirtualKey.Q,
        VirtualKey.R,
        VirtualKey.S,
        VirtualKey.T,
        VirtualKey.U,
        VirtualKey.V,
        VirtualKey.W,
        VirtualKey.X,
        VirtualKey.Y,
        VirtualKey.Z,
        VirtualKey.Number0,
        VirtualKey.Number1,
        VirtualKey.Number2,
        VirtualKey.Number3,
        VirtualKey.Number4,
        VirtualKey.Number5,
        VirtualKey.Number6,
        VirtualKey.Number7,
        VirtualKey.Number8,
        VirtualKey.Number9,
        VirtualKey.F1,
        VirtualKey.F2,
        VirtualKey.F3,
        VirtualKey.F4,
        VirtualKey.F5,
        VirtualKey.F6,
        VirtualKey.F7,
        VirtualKey.F8,
        VirtualKey.F9,
        VirtualKey.F10,
        VirtualKey.F11,
        VirtualKey.F12,
        VirtualKey.Space,
        VirtualKey.Tab,
        VirtualKey.Escape,
        VirtualKey.Insert,
        VirtualKey.Delete,
        VirtualKey.Home,
        VirtualKey.End,
        VirtualKey.PageUp,
        VirtualKey.PageDown,
        VirtualKey.Left,
        VirtualKey.Right,
        VirtualKey.Up,
        VirtualKey.Down
    ];

    public SettingsPageViewModel()
    {
        var virtualKeyOptions = CreateSelectionOptions(s_virtualKeys, SettingsDisplayFormatter.FormatVirtualKey);
        ApplicationThemePreferenceOptions = CreateSelectionOptions(s_applicationThemePreferences, SettingsDisplayFormatter.FormatApplicationThemePreference);
        AppLanguagePreferenceOptions = CreateSelectionOptions(s_appLanguagePreferences, SettingsDisplayFormatter.FormatAppLanguagePreference);
        MultiDisplayBehaviorOptions = CreateSelectionOptions(s_multiDisplayBehaviors, SettingsDisplayFormatter.FormatMultiDisplayBehavior);
        ToggleDeskBorderEnabledHotkeyEditor = new KeyboardShortcutEditorViewModel(virtualKeyOptions);
        MoveFocusedWindowToPreviousDesktopHotkeyEditor = new KeyboardShortcutEditorViewModel(virtualKeyOptions);
        MoveFocusedWindowToNextDesktopHotkeyEditor = new KeyboardShortcutEditorViewModel(virtualKeyOptions);
        NavigatorToggleHotkeyEditor = new KeyboardShortcutEditorViewModel(virtualKeyOptions);
        RegisterKeyboardShortcutEditor(ToggleDeskBorderEnabledHotkeyEditor);
        RegisterKeyboardShortcutEditor(MoveFocusedWindowToPreviousDesktopHotkeyEditor);
        RegisterKeyboardShortcutEditor(MoveFocusedWindowToNextDesktopHotkeyEditor);
        RegisterKeyboardShortcutEditor(NavigatorToggleHotkeyEditor);
    }

    public List<SelectionOption<ApplicationThemePreference>> ApplicationThemePreferenceOptions { get; }

    public List<SelectionOption<AppLanguagePreference>> AppLanguagePreferenceOptions { get; }

    public ObservableCollection<string> BlacklistedProcessNames { get; } = [];

    public ModifierKeySelectionViewModel CreateDesktopModifierSelection { get; } = new();

    [ObservableProperty]
    public partial double BottomDesktopEdgeIgnorePercentage { get; set; }

    [ObservableProperty]
    public partial bool IsAutoDeleteEnabled { get; set; }

    [ObservableProperty]
    public partial double AutoDeleteWarningTimeoutSeconds { get; set; }

    [ObservableProperty]
    public partial bool IsAutoDeleteWarningEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsDesktopCreationEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsDeskBorderEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsLaunchOnStartupEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsNavigatorTriggerAreaEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsStoreUpdateCheckEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsWindowsOnlyModifierWarningSuppressed { get; set; }

    public KeyboardShortcutEditorViewModel MoveFocusedWindowToNextDesktopHotkeyEditor { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoveFocusedWindowToNextDesktopHotkeyValidationState))]
    public partial string? MoveFocusedWindowToNextDesktopHotkeyRegistrationFailureMessage { get; set; }

    public KeyboardShortcutValidationState MoveFocusedWindowToNextDesktopHotkeyValidationState => GetKeyboardShortcutValidationState(
        MoveFocusedWindowToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
        [
            ToggleDeskBorderEnabledHotkeyEditor.CreateKeyboardShortcutSettings(),
            MoveFocusedWindowToPreviousDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            NavigatorToggleHotkeyEditor.CreateKeyboardShortcutSettings()
        ],
        MoveFocusedWindowToNextDesktopHotkeyRegistrationFailureMessage);

    public KeyboardShortcutEditorViewModel MoveFocusedWindowToPreviousDesktopHotkeyEditor { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoveFocusedWindowToPreviousDesktopHotkeyValidationState))]
    public partial string? MoveFocusedWindowToPreviousDesktopHotkeyRegistrationFailureMessage { get; set; }

    public KeyboardShortcutValidationState MoveFocusedWindowToPreviousDesktopHotkeyValidationState => GetKeyboardShortcutValidationState(
        MoveFocusedWindowToPreviousDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
        [
            ToggleDeskBorderEnabledHotkeyEditor.CreateKeyboardShortcutSettings(),
            MoveFocusedWindowToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            NavigatorToggleHotkeyEditor.CreateKeyboardShortcutSettings()
        ],
        MoveFocusedWindowToPreviousDesktopHotkeyRegistrationFailureMessage);

    public List<SelectionOption<MultiDisplayBehavior>> MultiDisplayBehaviorOptions { get; }

    public KeyboardShortcutEditorViewModel NavigatorToggleHotkeyEditor { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigatorToggleHotkeyValidationState))]
    public partial string? NavigatorToggleHotkeyRegistrationFailureMessage { get; set; }

    public KeyboardShortcutValidationState NavigatorToggleHotkeyValidationState => GetKeyboardShortcutValidationState(
        NavigatorToggleHotkeyEditor.CreateKeyboardShortcutSettings(),
        [
            ToggleDeskBorderEnabledHotkeyEditor.CreateKeyboardShortcutSettings(),
            MoveFocusedWindowToPreviousDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            MoveFocusedWindowToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings()
        ],
        NavigatorToggleHotkeyRegistrationFailureMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigatorTriggerAreaSummary))]
    public partial double NavigatorTriggerHeightPercentage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigatorTriggerAreaSummary))]
    public partial double NavigatorTriggerLeftPercentage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigatorTriggerAreaSummary))]
    public partial double NavigatorTriggerTopPercentage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigatorTriggerAreaSummary))]
    public partial double NavigatorTriggerWidthPercentage { get; set; }

    [ObservableProperty]
    public partial double TopDesktopEdgeIgnorePercentage { get; set; }

    [ObservableProperty]
    public partial SelectionOption<AppLanguagePreference>? SelectedAppLanguagePreferenceOption { get; set; }

    [ObservableProperty]
    public partial SelectionOption<ApplicationThemePreference>? SelectedApplicationThemePreferenceOption { get; set; }

    [ObservableProperty]
    public partial SelectionOption<MultiDisplayBehavior>? SelectedMultiDisplayBehaviorOption { get; set; }

    public ModifierKeySelectionViewModel SwitchDesktopModifierSelection { get; } = new();

    public KeyboardShortcutEditorViewModel ToggleDeskBorderEnabledHotkeyEditor { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleDeskBorderEnabledHotkeyValidationState))]
    public partial string? ToggleDeskBorderEnabledHotkeyRegistrationFailureMessage { get; set; }

    public KeyboardShortcutValidationState ToggleDeskBorderEnabledHotkeyValidationState => GetKeyboardShortcutValidationState(
        ToggleDeskBorderEnabledHotkeyEditor.CreateKeyboardShortcutSettings(),
        [
            MoveFocusedWindowToPreviousDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            MoveFocusedWindowToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            NavigatorToggleHotkeyEditor.CreateKeyboardShortcutSettings()
        ],
        ToggleDeskBorderEnabledHotkeyRegistrationFailureMessage);

    public string NavigatorTriggerAreaSummary => SettingsDisplayFormatter.FormatTriggerRectangle(CreateNavigatorTriggerRectangleSettings());

    public bool AddBlacklistedProcessNames(IEnumerable<string> processNames)
    {
        var blacklistedProcessNameSet = BlacklistedProcessNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedProcessNames = processNames
            .Where(processName => !string.IsNullOrWhiteSpace(processName))
            .Select(processName => processName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(blacklistedProcessNameSet.Add)
            .ToArray();
        if (normalizedProcessNames.Length == 0)
            return false;

        foreach (var normalizedProcessName in normalizedProcessNames)
            BlacklistedProcessNames.Add(normalizedProcessName);

        SortBlacklistedProcessNames();
        return true;
    }

    public DeskBorderSettings CreateSettings() => new()
    {
        SchemaVersion = 1,
        IsDeskBorderEnabled = IsDeskBorderEnabled,
        MultiDisplayBehavior = SelectedMultiDisplayBehaviorOption?.Value ?? MultiDisplayBehavior.DisableInMultiDisplayEnvironment,
        SwitchDesktopModifierSettings = new ModifierGateSettings
        {
            RequiredKeyboardModifierKeys = SwitchDesktopModifierSelection.CreateKeyboardModifierKeys()
        },
        CreateDesktopModifierSettings = new ModifierGateSettings
        {
            RequiredKeyboardModifierKeys = CreateDesktopModifierSelection.CreateKeyboardModifierKeys()
        },
        IsDesktopCreationEnabled = IsDesktopCreationEnabled,
        IsAutoDeleteEnabled = IsAutoDeleteEnabled,
        IsAutoDeleteWarningEnabled = IsAutoDeleteWarningEnabled,
        AutoDeleteWarningTimeoutSeconds = AutoDeleteWarningTimeoutSeconds,
        DesktopEdgeIgnoreZoneSettings = new DesktopEdgeIgnoreZoneSettings
        {
            TopIgnorePercentage = TopDesktopEdgeIgnorePercentage,
            BottomIgnorePercentage = BottomDesktopEdgeIgnorePercentage
        },
        ApplicationHotkeySettings = new ApplicationHotkeySettings
        {
            ToggleDeskBorderEnabledHotkey = ToggleDeskBorderEnabledHotkeyEditor.CreateKeyboardShortcutSettings()
        },
        FocusedWindowMoveHotkeySettings = new FocusedWindowMoveHotkeySettings
        {
            MoveToPreviousDesktopHotkey = MoveFocusedWindowToPreviousDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            MoveToNextDesktopHotkey = MoveFocusedWindowToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings()
        },
        NavigatorSettings = new NavigatorSettings
        {
            ToggleHotkey = NavigatorToggleHotkeyEditor.CreateKeyboardShortcutSettings(),
            IsTriggerAreaEnabled = IsNavigatorTriggerAreaEnabled,
            TriggerRectangle = CreateNavigatorTriggerRectangleSettings()
        },
        BlacklistedProcessNames = [.. BlacklistedProcessNames],
        IsLaunchOnStartupEnabled = IsLaunchOnStartupEnabled,
        IsStoreUpdateCheckEnabled = IsStoreUpdateCheckEnabled,
        IsWindowsOnlyModifierWarningSuppressed = IsWindowsOnlyModifierWarningSuppressed,
        AppLanguagePreference = SelectedAppLanguagePreferenceOption?.Value ?? AppLanguagePreference.System,
        ApplicationThemePreference = SelectedApplicationThemePreferenceOption?.Value ?? ApplicationThemePreference.System
    };

    public void Load(DeskBorderSettings deskBorderSettings)
    {
        IsDeskBorderEnabled = deskBorderSettings.IsDeskBorderEnabled;
        IsLaunchOnStartupEnabled = deskBorderSettings.IsLaunchOnStartupEnabled;
        IsStoreUpdateCheckEnabled = deskBorderSettings.IsStoreUpdateCheckEnabled;
        IsWindowsOnlyModifierWarningSuppressed = deskBorderSettings.IsWindowsOnlyModifierWarningSuppressed;
        IsDesktopCreationEnabled = deskBorderSettings.IsDesktopCreationEnabled;
        IsAutoDeleteEnabled = deskBorderSettings.IsAutoDeleteEnabled;
        IsAutoDeleteWarningEnabled = deskBorderSettings.IsAutoDeleteWarningEnabled;
        AutoDeleteWarningTimeoutSeconds = deskBorderSettings.AutoDeleteWarningTimeoutSeconds;
        TopDesktopEdgeIgnorePercentage = deskBorderSettings.DesktopEdgeIgnoreZoneSettings.TopIgnorePercentage;
        BottomDesktopEdgeIgnorePercentage = deskBorderSettings.DesktopEdgeIgnoreZoneSettings.BottomIgnorePercentage;
        IsNavigatorTriggerAreaEnabled = deskBorderSettings.NavigatorSettings.IsTriggerAreaEnabled;
        SetNavigatorTriggerRectangle(deskBorderSettings.NavigatorSettings.TriggerRectangle);
        SelectedMultiDisplayBehaviorOption = FindSelectionOption(
            MultiDisplayBehaviorOptions,
            deskBorderSettings.MultiDisplayBehavior);
        SelectedAppLanguagePreferenceOption = FindSelectionOption(
            AppLanguagePreferenceOptions,
            deskBorderSettings.AppLanguagePreference);
        SelectedApplicationThemePreferenceOption = FindSelectionOption(
            ApplicationThemePreferenceOptions,
            deskBorderSettings.ApplicationThemePreference);
        SwitchDesktopModifierSelection.Load(deskBorderSettings.SwitchDesktopModifierSettings.RequiredKeyboardModifierKeys);
        CreateDesktopModifierSelection.Load(deskBorderSettings.CreateDesktopModifierSettings.RequiredKeyboardModifierKeys);
        ToggleDeskBorderEnabledHotkeyEditor.Load(deskBorderSettings.ApplicationHotkeySettings.ToggleDeskBorderEnabledHotkey);
        MoveFocusedWindowToPreviousDesktopHotkeyEditor.Load(deskBorderSettings.FocusedWindowMoveHotkeySettings.MoveToPreviousDesktopHotkey);
        MoveFocusedWindowToNextDesktopHotkeyEditor.Load(deskBorderSettings.FocusedWindowMoveHotkeySettings.MoveToNextDesktopHotkey);
        NavigatorToggleHotkeyEditor.Load(deskBorderSettings.NavigatorSettings.ToggleHotkey);

        BlacklistedProcessNames.Clear();
        foreach (var blacklistedProcessName in deskBorderSettings.BlacklistedProcessNames)
            BlacklistedProcessNames.Add(blacklistedProcessName);

        SortBlacklistedProcessNames();
        NotifyKeyboardShortcutValidationStatesChanged();
    }

    public bool RemoveBlacklistedProcessName(string processName) => BlacklistedProcessNames.Remove(processName);

    public void SetNavigatorTriggerRectangle(TriggerRectangleSettings triggerRectangleSettings)
    {
        NavigatorTriggerLeftPercentage = TriggerRectangleDisplayConverter.ConvertNormalizedOffsetToDisplayPercentage(triggerRectangleSettings.Left, triggerRectangleSettings.Width);
        NavigatorTriggerTopPercentage = TriggerRectangleDisplayConverter.ConvertNormalizedOffsetToDisplayPercentage(triggerRectangleSettings.Top, triggerRectangleSettings.Height);
        NavigatorTriggerWidthPercentage = TriggerRectangleDisplayConverter.ConvertNormalizedLengthToDisplayPercentage(triggerRectangleSettings.Width);
        NavigatorTriggerHeightPercentage = TriggerRectangleDisplayConverter.ConvertNormalizedLengthToDisplayPercentage(triggerRectangleSettings.Height);
    }

    public void UpdateHotkeyRegistrationFailureMessage(HotkeyActionType hotkeyActionType, string? registrationFailureMessage)
    {
        switch (hotkeyActionType)
        {
            case HotkeyActionType.ToggleDeskBorderEnabled:
                ToggleDeskBorderEnabledHotkeyRegistrationFailureMessage = registrationFailureMessage;
                return;

            case HotkeyActionType.MoveFocusedWindowToPreviousDesktop:
                MoveFocusedWindowToPreviousDesktopHotkeyRegistrationFailureMessage = registrationFailureMessage;
                return;

            case HotkeyActionType.MoveFocusedWindowToNextDesktop:
                MoveFocusedWindowToNextDesktopHotkeyRegistrationFailureMessage = registrationFailureMessage;
                return;

            case HotkeyActionType.ToggleNavigator:
                NavigatorToggleHotkeyRegistrationFailureMessage = registrationFailureMessage;
                return;

            default:
                throw new InvalidOperationException("The requested hotkey action type is not supported.");
        }
    }

    private static List<SelectionOption<TValue>> CreateSelectionOptions<TValue>(IReadOnlyList<TValue> values, Func<TValue, string> displayTextSelector) where TValue : notnull => [.. values.Select(value => new SelectionOption<TValue>(value, displayTextSelector(value)))];

    private TriggerRectangleSettings CreateNavigatorTriggerRectangleSettings() => new()
    {
        Left = TriggerRectangleDisplayConverter.ConvertDisplayPercentageToNormalizedOffset(
            NavigatorTriggerLeftPercentage,
            TriggerRectangleDisplayConverter.ConvertDisplayPercentageToNormalizedLength(NavigatorTriggerWidthPercentage)),
        Top = TriggerRectangleDisplayConverter.ConvertDisplayPercentageToNormalizedOffset(
            NavigatorTriggerTopPercentage,
            TriggerRectangleDisplayConverter.ConvertDisplayPercentageToNormalizedLength(NavigatorTriggerHeightPercentage)),
        Width = TriggerRectangleDisplayConverter.ConvertDisplayPercentageToNormalizedLength(NavigatorTriggerWidthPercentage),
        Height = TriggerRectangleDisplayConverter.ConvertDisplayPercentageToNormalizedLength(NavigatorTriggerHeightPercentage)
    };

    private static SelectionOption<TValue> FindSelectionOption<TValue>(List<SelectionOption<TValue>> selectionOptions, TValue value) where TValue : notnull
    {
        foreach (var selectionOption in selectionOptions)
        {
            if (EqualityComparer<TValue>.Default.Equals(selectionOption.Value, value))
                return selectionOption;
        }

        return selectionOptions[0];
    }

    private static KeyboardShortcutValidationState GetKeyboardShortcutValidationState(
        KeyboardShortcutSettings currentKeyboardShortcutSettings,
        IReadOnlyList<KeyboardShortcutSettings> otherKeyboardShortcutSettings,
        string? registrationFailureMessage)
    {
        if (!currentKeyboardShortcutSettings.IsEnabled)
            return KeyboardShortcutValidationState.Disabled;

        if (currentKeyboardShortcutSettings.Key == VirtualKey.None)
            return KeyboardShortcutValidationState.MissingKey;

        foreach (var otherKeyboardShortcutSetting in otherKeyboardShortcutSettings)
        {
            if (!otherKeyboardShortcutSetting.IsEnabled || otherKeyboardShortcutSetting.Key == VirtualKey.None)
                continue;

            if (otherKeyboardShortcutSetting.Key != currentKeyboardShortcutSettings.Key)
                continue;

            if (otherKeyboardShortcutSetting.RequiredKeyboardModifierKeys != currentKeyboardShortcutSettings.RequiredKeyboardModifierKeys)
                continue;

            return KeyboardShortcutValidationState.Duplicate;
        }

        if (!string.IsNullOrWhiteSpace(registrationFailureMessage))
            return KeyboardShortcutValidationState.RegistrationFailed;

        return KeyboardShortcutValidationState.Valid;
    }

    private void NotifyKeyboardShortcutValidationStatesChanged()
    {
        OnPropertyChanged(nameof(ToggleDeskBorderEnabledHotkeyValidationState));
        OnPropertyChanged(nameof(MoveFocusedWindowToPreviousDesktopHotkeyValidationState));
        OnPropertyChanged(nameof(MoveFocusedWindowToNextDesktopHotkeyValidationState));
        OnPropertyChanged(nameof(NavigatorToggleHotkeyValidationState));
    }

    private void OnKeyboardShortcutEditorPropertyChanged(object? sender, PropertyChangedEventArgs propertyChangedEventArgs)
    {
        _ = sender;
        _ = propertyChangedEventArgs;
        NotifyKeyboardShortcutValidationStatesChanged();
    }

    private void RegisterKeyboardShortcutEditor(KeyboardShortcutEditorViewModel keyboardShortcutEditor)
    {
        keyboardShortcutEditor.PropertyChanged += OnKeyboardShortcutEditorPropertyChanged;
        keyboardShortcutEditor.RequiredKeyboardModifierSelection.PropertyChanged += OnKeyboardShortcutEditorPropertyChanged;
    }

    private void SortBlacklistedProcessNames()
    {
        var sortedBlacklistedProcessNames = BlacklistedProcessNames
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        BlacklistedProcessNames.Clear();
        foreach (var blacklistedProcessName in sortedBlacklistedProcessNames)
            BlacklistedProcessNames.Add(blacklistedProcessName);
    }
}
