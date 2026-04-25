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
    ReservedByWindowsDesktopSwitch,
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

    private static readonly DesktopSwitchMouseLocationOption[] s_desktopSwitchMouseLocationOptions =
    [
        DesktopSwitchMouseLocationOption.OppositeSide,
        DesktopSwitchMouseLocationOption.VirtualScreenCenter,
        DesktopSwitchMouseLocationOption.PrimaryMonitorCenter,
        DesktopSwitchMouseLocationOption.TargetMonitorCenter,
        DesktopSwitchMouseLocationOption.InputMonitorCenter,
        DesktopSwitchMouseLocationOption.DoNotMove
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

    private static readonly KeyboardShortcutTriggerType[] s_mouseKeyboardShortcutTriggerTypes =
    [
        KeyboardShortcutTriggerType.MouseWheelUp,
        KeyboardShortcutTriggerType.MouseWheelDown,
        KeyboardShortcutTriggerType.MouseLeftButton,
        KeyboardShortcutTriggerType.MouseRightButton
    ];

    public SettingsPageViewModel()
    {
        var keyboardShortcutTriggerOptions = CreateKeyboardShortcutTriggerOptions();
        ApplicationThemePreferenceOptions = CreateSelectionOptions(s_applicationThemePreferences, SettingsDisplayFormatter.FormatApplicationThemePreference);
        AppLanguagePreferenceOptions = CreateSelectionOptions(s_appLanguagePreferences, SettingsDisplayFormatter.FormatAppLanguagePreference);
        DesktopSwitchMouseLocationOptions = CreateSelectionOptions(s_desktopSwitchMouseLocationOptions, SettingsDisplayFormatter.FormatDesktopSwitchMouseLocationOption);
        MultiDisplayBehaviorOptions = CreateSelectionOptions(s_multiDisplayBehaviors, SettingsDisplayFormatter.FormatMultiDisplayBehavior);
        ToggleDeskBorderEnabledHotkeyEditor = new KeyboardShortcutEditorViewModel(keyboardShortcutTriggerOptions);
        SwitchToPreviousDesktopHotkeyEditor = new KeyboardShortcutEditorViewModel(keyboardShortcutTriggerOptions);
        SwitchToNextDesktopHotkeyEditor = new KeyboardShortcutEditorViewModel(keyboardShortcutTriggerOptions);
        MoveFocusedWindowToPreviousDesktopHotkeyEditor = new KeyboardShortcutEditorViewModel(keyboardShortcutTriggerOptions);
        MoveFocusedWindowToNextDesktopHotkeyEditor = new KeyboardShortcutEditorViewModel(keyboardShortcutTriggerOptions);
        NavigatorToggleHotkeyEditor = new KeyboardShortcutEditorViewModel(keyboardShortcutTriggerOptions);
        RegisterKeyboardShortcutEditor(ToggleDeskBorderEnabledHotkeyEditor);
        RegisterKeyboardShortcutEditor(SwitchToPreviousDesktopHotkeyEditor);
        RegisterKeyboardShortcutEditor(SwitchToNextDesktopHotkeyEditor);
        RegisterKeyboardShortcutEditor(MoveFocusedWindowToPreviousDesktopHotkeyEditor);
        RegisterKeyboardShortcutEditor(MoveFocusedWindowToNextDesktopHotkeyEditor);
        RegisterKeyboardShortcutEditor(NavigatorToggleHotkeyEditor);
    }

    public List<SelectionOption<ApplicationThemePreference>> ApplicationThemePreferenceOptions { get; }

    public List<SelectionOption<AppLanguagePreference>> AppLanguagePreferenceOptions { get; }

    public ObservableCollection<string> BlacklistedProcessNames { get; } = [];

    public ObservableCollection<string> WhitelistedProcessNames { get; } = [];

    public ModifierKeySelectionViewModel CreateDesktopModifierSelection { get; } = new();

    public ModifierKeySelectionViewModel SwitchDesktopWhileMouseButtonsArePressedModifierSelection { get; } = new();

    [ObservableProperty]
    public partial double BottomDesktopEdgeIgnorePercentage { get; set; }

    [ObservableProperty]
    public partial bool IsAutoDeleteEnabled { get; set; }

    [ObservableProperty]
    public partial double AutoDeleteWarningTimeoutSeconds { get; set; }

    [ObservableProperty]
    public partial bool IsAutoDeleteWarningEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsAutoDeleteCompletionToastEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsDesktopCreationEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsDesktopCreationSkippedWhenCurrentDesktopIsEmpty { get; set; }

    [ObservableProperty]
    public partial bool IsDesktopEdgeAdditionalTriggerDistanceEnabled { get; set; }

    [ObservableProperty]
    public partial double DesktopEdgeAdditionalTriggerDistancePercentage { get; set; } = 5.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMultiDisplayBehaviorSelectionEnabled))]
    [NotifyPropertyChangedFor(nameof(AreDesktopEdgeIgnoreZoneControlsEnabled))]
    [NotifyPropertyChangedFor(nameof(AreVerticalDesktopSwitchingOptionControlsVisible))]
    public partial bool IsVerticalDesktopSwitchingEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsVerticalDesktopSwitchDirectionReversed { get; set; }

    [ObservableProperty]
    public partial bool IsVerticalDesktopSwitchingOnlyInMultiDisplayEnvironment { get; set; }

    [ObservableProperty]
    public partial bool IsDeskBorderEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsLaunchOnStartupEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsAlwaysRunAsAdministratorEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsNavigatorTriggerAreaEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsStoreUpdateCheckEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsWindowsOnlyModifierWarningSuppressed { get; set; }

    public List<SelectionOption<DesktopSwitchMouseLocationOption>> DesktopSwitchMouseLocationOptions { get; }

    public KeyboardShortcutEditorViewModel SwitchToNextDesktopHotkeyEditor { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwitchToNextDesktopHotkeyValidationState))]
    public partial string? SwitchToNextDesktopHotkeyRegistrationFailureMessage { get; set; }

    public KeyboardShortcutValidationState SwitchToNextDesktopHotkeyValidationState => GetKeyboardShortcutValidationState(
        SwitchToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
        [
            ToggleDeskBorderEnabledHotkeyEditor.CreateKeyboardShortcutSettings(),
            SwitchToPreviousDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            MoveFocusedWindowToPreviousDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            MoveFocusedWindowToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            NavigatorToggleHotkeyEditor.CreateKeyboardShortcutSettings()
        ],
        SwitchToNextDesktopHotkeyRegistrationFailureMessage);

    public KeyboardShortcutEditorViewModel SwitchToPreviousDesktopHotkeyEditor { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwitchToPreviousDesktopHotkeyValidationState))]
    public partial string? SwitchToPreviousDesktopHotkeyRegistrationFailureMessage { get; set; }

    public KeyboardShortcutValidationState SwitchToPreviousDesktopHotkeyValidationState => GetKeyboardShortcutValidationState(
        SwitchToPreviousDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
        [
            ToggleDeskBorderEnabledHotkeyEditor.CreateKeyboardShortcutSettings(),
            SwitchToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            MoveFocusedWindowToPreviousDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            MoveFocusedWindowToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            NavigatorToggleHotkeyEditor.CreateKeyboardShortcutSettings()
        ],
        SwitchToPreviousDesktopHotkeyRegistrationFailureMessage);

    public KeyboardShortcutEditorViewModel MoveFocusedWindowToNextDesktopHotkeyEditor { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoveFocusedWindowToNextDesktopHotkeyValidationState))]
    public partial string? MoveFocusedWindowToNextDesktopHotkeyRegistrationFailureMessage { get; set; }

    public KeyboardShortcutValidationState MoveFocusedWindowToNextDesktopHotkeyValidationState => GetKeyboardShortcutValidationState(
        MoveFocusedWindowToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
        [
            ToggleDeskBorderEnabledHotkeyEditor.CreateKeyboardShortcutSettings(),
            SwitchToPreviousDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            SwitchToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
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
            SwitchToPreviousDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            SwitchToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
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
            SwitchToPreviousDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            SwitchToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
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
    public partial SelectionOption<DesktopSwitchMouseLocationOption>? SelectedDesktopEdgeTriggeredMouseLocationOption { get; set; }

    [ObservableProperty]
    public partial SelectionOption<DesktopSwitchMouseLocationOption>? SelectedHotkeyTriggeredMouseLocationOption { get; set; }

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
            SwitchToPreviousDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            SwitchToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            MoveFocusedWindowToPreviousDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            MoveFocusedWindowToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            NavigatorToggleHotkeyEditor.CreateKeyboardShortcutSettings()
        ],
        ToggleDeskBorderEnabledHotkeyRegistrationFailureMessage);

    public bool AreDesktopEdgeIgnoreZoneControlsEnabled => !IsVerticalDesktopSwitchingEnabled;

    public bool AreVerticalDesktopSwitchingOptionControlsVisible => IsVerticalDesktopSwitchingEnabled;

    public bool IsMultiDisplayBehaviorSelectionEnabled => !IsVerticalDesktopSwitchingEnabled;

    public string NavigatorTriggerAreaSummary => SettingsDisplayFormatter.FormatTriggerRectangle(CreateNavigatorTriggerRectangleSettings());

    public bool AddBlacklistedProcessNames(IEnumerable<string> processNames)
    {
        var blacklistedProcessNameSet = BlacklistedProcessNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedProcessNames = NormalizeProcessNames(processNames)
            .Where(blacklistedProcessNameSet.Add)
            .ToArray();
        if (normalizedProcessNames.Length == 0)
            return false;

        foreach (var normalizedProcessName in normalizedProcessNames)
            BlacklistedProcessNames.Add(normalizedProcessName);

        SortBlacklistedProcessNames();
        return true;
    }

    public bool AddWhitelistedProcessNames(IEnumerable<string> processNames)
    {
        var whitelistedProcessNameSet = WhitelistedProcessNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedProcessNames = NormalizeProcessNames(processNames)
            .Where(whitelistedProcessNameSet.Add)
            .ToArray();
        if (normalizedProcessNames.Length == 0)
            return false;

        foreach (var normalizedProcessName in normalizedProcessNames)
        {
            _ = BlacklistedProcessNames.Remove(normalizedProcessName);
            WhitelistedProcessNames.Add(normalizedProcessName);
        }

        SortBlacklistedProcessNames();
        SortWhitelistedProcessNames();
        return true;
    }

    public DeskBorderSettings CreateSettings() => new()
    {
        SchemaVersion = 3,
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
        SwitchDesktopWhileMouseButtonsArePressedModifierSettings = new ModifierGateSettings
        {
            RequiredKeyboardModifierKeys = SwitchDesktopWhileMouseButtonsArePressedModifierSelection.CreateKeyboardModifierKeys()
        },
        IsDesktopCreationEnabled = IsDesktopCreationEnabled,
        IsDesktopCreationSkippedWhenCurrentDesktopIsEmpty = IsDesktopCreationSkippedWhenCurrentDesktopIsEmpty,
        IsAutoDeleteEnabled = IsAutoDeleteEnabled,
        IsAutoDeleteWarningEnabled = IsAutoDeleteWarningEnabled,
        IsAutoDeleteCompletionToastEnabled = IsAutoDeleteCompletionToastEnabled,
        IsDesktopEdgeAdditionalTriggerDistanceEnabled = IsDesktopEdgeAdditionalTriggerDistanceEnabled,
        DesktopEdgeAdditionalTriggerDistancePercentage = DesktopEdgeAdditionalTriggerDistancePercentage,
        IsVerticalDesktopSwitchingEnabled = IsVerticalDesktopSwitchingEnabled,
        IsVerticalDesktopSwitchDirectionReversed = IsVerticalDesktopSwitchDirectionReversed,
        IsVerticalDesktopSwitchingOnlyInMultiDisplayEnvironment = IsVerticalDesktopSwitchingOnlyInMultiDisplayEnvironment,
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
        DesktopSwitchHotkeySettings = new DesktopSwitchHotkeySettings
        {
            SwitchToPreviousDesktopHotkey = SwitchToPreviousDesktopHotkeyEditor.CreateKeyboardShortcutSettings(),
            SwitchToNextDesktopHotkey = SwitchToNextDesktopHotkeyEditor.CreateKeyboardShortcutSettings()
        },
        DesktopSwitchMouseLocationSettings = new DesktopSwitchMouseLocationSettings
        {
            HotkeyTriggeredMouseLocationOption = SelectedHotkeyTriggeredMouseLocationOption?.Value ?? DesktopSwitchMouseLocationOption.DoNotMove,
            DesktopEdgeTriggeredMouseLocationOption = SelectedDesktopEdgeTriggeredMouseLocationOption?.Value ?? DesktopSwitchMouseLocationOption.OppositeSide
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
        WhitelistedProcessNames = [.. WhitelistedProcessNames],
        IsLaunchOnStartupEnabled = IsLaunchOnStartupEnabled,
        IsAlwaysRunAsAdministratorEnabled = IsAlwaysRunAsAdministratorEnabled,
        IsStoreUpdateCheckEnabled = IsStoreUpdateCheckEnabled,
        IsWindowsOnlyModifierWarningSuppressed = IsWindowsOnlyModifierWarningSuppressed,
        AppLanguagePreference = SelectedAppLanguagePreferenceOption?.Value ?? AppLanguagePreference.System,
        ApplicationThemePreference = SelectedApplicationThemePreferenceOption?.Value ?? ApplicationThemePreference.System
    };

    public void Load(DeskBorderSettings deskBorderSettings)
    {
        IsDeskBorderEnabled = deskBorderSettings.IsDeskBorderEnabled;
        IsLaunchOnStartupEnabled = deskBorderSettings.IsLaunchOnStartupEnabled;
        IsAlwaysRunAsAdministratorEnabled = deskBorderSettings.IsAlwaysRunAsAdministratorEnabled;
        IsStoreUpdateCheckEnabled = deskBorderSettings.IsStoreUpdateCheckEnabled;
        IsWindowsOnlyModifierWarningSuppressed = deskBorderSettings.IsWindowsOnlyModifierWarningSuppressed;
        IsDesktopCreationEnabled = deskBorderSettings.IsDesktopCreationEnabled;
        IsDesktopCreationSkippedWhenCurrentDesktopIsEmpty = deskBorderSettings.IsDesktopCreationSkippedWhenCurrentDesktopIsEmpty;
        IsAutoDeleteEnabled = deskBorderSettings.IsAutoDeleteEnabled;
        IsAutoDeleteWarningEnabled = deskBorderSettings.IsAutoDeleteWarningEnabled;
        IsAutoDeleteCompletionToastEnabled = deskBorderSettings.IsAutoDeleteCompletionToastEnabled;
        IsDesktopEdgeAdditionalTriggerDistanceEnabled = deskBorderSettings.IsDesktopEdgeAdditionalTriggerDistanceEnabled;
        DesktopEdgeAdditionalTriggerDistancePercentage = deskBorderSettings.DesktopEdgeAdditionalTriggerDistancePercentage;
        IsVerticalDesktopSwitchingEnabled = deskBorderSettings.IsVerticalDesktopSwitchingEnabled;
        IsVerticalDesktopSwitchDirectionReversed = deskBorderSettings.IsVerticalDesktopSwitchDirectionReversed;
        IsVerticalDesktopSwitchingOnlyInMultiDisplayEnvironment = deskBorderSettings.IsVerticalDesktopSwitchingOnlyInMultiDisplayEnvironment;
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
        SelectedHotkeyTriggeredMouseLocationOption = FindSelectionOption(
            DesktopSwitchMouseLocationOptions,
            deskBorderSettings.DesktopSwitchMouseLocationSettings.HotkeyTriggeredMouseLocationOption);
        SelectedDesktopEdgeTriggeredMouseLocationOption = FindSelectionOption(
            DesktopSwitchMouseLocationOptions,
            deskBorderSettings.DesktopSwitchMouseLocationSettings.DesktopEdgeTriggeredMouseLocationOption);
        SwitchDesktopModifierSelection.Load(deskBorderSettings.SwitchDesktopModifierSettings.RequiredKeyboardModifierKeys);
        CreateDesktopModifierSelection.Load(deskBorderSettings.CreateDesktopModifierSettings.RequiredKeyboardModifierKeys);
        SwitchDesktopWhileMouseButtonsArePressedModifierSelection.Load(deskBorderSettings.SwitchDesktopWhileMouseButtonsArePressedModifierSettings.RequiredKeyboardModifierKeys);
        ToggleDeskBorderEnabledHotkeyEditor.Load(deskBorderSettings.ApplicationHotkeySettings.ToggleDeskBorderEnabledHotkey);
        SwitchToPreviousDesktopHotkeyEditor.Load(deskBorderSettings.DesktopSwitchHotkeySettings.SwitchToPreviousDesktopHotkey);
        SwitchToNextDesktopHotkeyEditor.Load(deskBorderSettings.DesktopSwitchHotkeySettings.SwitchToNextDesktopHotkey);
        MoveFocusedWindowToPreviousDesktopHotkeyEditor.Load(deskBorderSettings.FocusedWindowMoveHotkeySettings.MoveToPreviousDesktopHotkey);
        MoveFocusedWindowToNextDesktopHotkeyEditor.Load(deskBorderSettings.FocusedWindowMoveHotkeySettings.MoveToNextDesktopHotkey);
        NavigatorToggleHotkeyEditor.Load(deskBorderSettings.NavigatorSettings.ToggleHotkey);

        BlacklistedProcessNames.Clear();
        foreach (var blacklistedProcessName in deskBorderSettings.BlacklistedProcessNames)
            BlacklistedProcessNames.Add(blacklistedProcessName);

        WhitelistedProcessNames.Clear();
        foreach (var whitelistedProcessName in deskBorderSettings.WhitelistedProcessNames)
            WhitelistedProcessNames.Add(whitelistedProcessName);

        SortBlacklistedProcessNames();
        SortWhitelistedProcessNames();
        NotifyKeyboardShortcutValidationStatesChanged();
    }

    public bool RemoveBlacklistedProcessName(string processName) => BlacklistedProcessNames.Remove(processName);

    public bool RemoveWhitelistedProcessName(string processName) => WhitelistedProcessNames.Remove(processName);

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

            case HotkeyActionType.SwitchToPreviousDesktop:
                SwitchToPreviousDesktopHotkeyRegistrationFailureMessage = registrationFailureMessage;
                return;

            case HotkeyActionType.SwitchToNextDesktop:
                SwitchToNextDesktopHotkeyRegistrationFailureMessage = registrationFailureMessage;
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

    private static List<SelectionOption<KeyboardShortcutTriggerOptionValue>> CreateKeyboardShortcutTriggerOptions()
    {
        var keyboardShortcutTriggerOptions = new List<SelectionOption<KeyboardShortcutTriggerOptionValue>>(s_virtualKeys.Length + s_mouseKeyboardShortcutTriggerTypes.Length);
        foreach (var virtualKey in s_virtualKeys)
        {
            keyboardShortcutTriggerOptions.Add(new(
                new KeyboardShortcutTriggerOptionValue(KeyboardShortcutTriggerType.VirtualKey, virtualKey),
                SettingsDisplayFormatter.FormatKeyboardShortcutTrigger(KeyboardShortcutTriggerType.VirtualKey, virtualKey)));
        }

        foreach (var mouseKeyboardShortcutTriggerType in s_mouseKeyboardShortcutTriggerTypes)
        {
            keyboardShortcutTriggerOptions.Add(new(
                new KeyboardShortcutTriggerOptionValue(mouseKeyboardShortcutTriggerType, VirtualKey.None),
                SettingsDisplayFormatter.FormatKeyboardShortcutTrigger(mouseKeyboardShortcutTriggerType, VirtualKey.None)));
        }

        return keyboardShortcutTriggerOptions;
    }

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

        if (!KeyboardShortcutHelper.IsKeyboardShortcutSpecified(currentKeyboardShortcutSettings))
            return KeyboardShortcutValidationState.MissingKey;

        if (KeyboardShortcutHelper.IsReservedByWindowsDesktopSwitchHotkey(currentKeyboardShortcutSettings))
            return KeyboardShortcutValidationState.ReservedByWindowsDesktopSwitch;

        foreach (var otherKeyboardShortcutSetting in otherKeyboardShortcutSettings)
        {
            if (!otherKeyboardShortcutSetting.IsEnabled || !KeyboardShortcutHelper.IsKeyboardShortcutSpecified(otherKeyboardShortcutSetting))
                continue;

            if (KeyboardShortcutHelper.CreateKeyboardShortcutIdentity(otherKeyboardShortcutSetting) != KeyboardShortcutHelper.CreateKeyboardShortcutIdentity(currentKeyboardShortcutSettings))
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
        OnPropertyChanged(nameof(SwitchToPreviousDesktopHotkeyValidationState));
        OnPropertyChanged(nameof(SwitchToNextDesktopHotkeyValidationState));
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

    private static IEnumerable<string> NormalizeProcessNames(IEnumerable<string> processNames) => processNames
        .Where(processName => !string.IsNullOrWhiteSpace(processName))
        .Select(processName => processName.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase);

    private void SortBlacklistedProcessNames()
    {
        var sortedBlacklistedProcessNames = BlacklistedProcessNames
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        BlacklistedProcessNames.Clear();
        foreach (var blacklistedProcessName in sortedBlacklistedProcessNames)
            BlacklistedProcessNames.Add(blacklistedProcessName);
    }

    private void SortWhitelistedProcessNames()
    {
        var sortedWhitelistedProcessNames = WhitelistedProcessNames
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        WhitelistedProcessNames.Clear();
        foreach (var whitelistedProcessName in sortedWhitelistedProcessNames)
            WhitelistedProcessNames.Add(whitelistedProcessName);
    }
}
