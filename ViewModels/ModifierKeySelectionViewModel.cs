using CommunityToolkit.Mvvm.ComponentModel;
using DeskBorder.Models;

namespace DeskBorder.ViewModels;

public sealed partial class ModifierKeySelectionViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsAlternateEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsControlEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsShiftEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsWindowsEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsLeftMouseButtonEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsMiddleMouseButtonEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsRightMouseButtonEnabled { get; set; }

    public KeyboardModifierKeys CreateKeyboardModifierKeys()
    {
        var keyboardModifierKeys = KeyboardModifierKeys.None;
        if (IsShiftEnabled) keyboardModifierKeys |= KeyboardModifierKeys.Shift;

        if (IsControlEnabled) keyboardModifierKeys |= KeyboardModifierKeys.Control;

        if (IsAlternateEnabled) keyboardModifierKeys |= KeyboardModifierKeys.Alternate;

        if (IsWindowsEnabled) keyboardModifierKeys |= KeyboardModifierKeys.Windows;

        return keyboardModifierKeys;
    }

    public InputTriggerType[] CreateMouseModifierButtonTriggers()
    {
        var inputTriggerTypes = new List<InputTriggerType>(3);
        if (IsLeftMouseButtonEnabled) inputTriggerTypes.Add(InputTriggerType.MouseLeftButton);

        if (IsMiddleMouseButtonEnabled) inputTriggerTypes.Add(InputTriggerType.MouseMiddleButton);

        if (IsRightMouseButtonEnabled) inputTriggerTypes.Add(InputTriggerType.MouseRightButton);

        return [.. inputTriggerTypes];
    }

    public bool HasAnyModifierInput() => CreateKeyboardModifierKeys() != KeyboardModifierKeys.None || CreateMouseModifierButtonTriggers().Length > 0;

    public void Load(KeyboardModifierKeys keyboardModifierKeys)
    {
        IsShiftEnabled = keyboardModifierKeys.HasFlag(KeyboardModifierKeys.Shift);
        IsControlEnabled = keyboardModifierKeys.HasFlag(KeyboardModifierKeys.Control);
        IsAlternateEnabled = keyboardModifierKeys.HasFlag(KeyboardModifierKeys.Alternate);
        IsWindowsEnabled = keyboardModifierKeys.HasFlag(KeyboardModifierKeys.Windows);
        IsLeftMouseButtonEnabled = false;
        IsMiddleMouseButtonEnabled = false;
        IsRightMouseButtonEnabled = false;
    }

    public void Load(ModifierGateSettings modifierGateSettings)
    {
        Load(modifierGateSettings.RequiredKeyboardModifierKeys);
        var requiredMouseModifierButtonTriggers = modifierGateSettings.RequiredMouseModifierButtonTriggers;
        IsLeftMouseButtonEnabled = requiredMouseModifierButtonTriggers.Contains(InputTriggerType.MouseLeftButton);
        IsMiddleMouseButtonEnabled = requiredMouseModifierButtonTriggers.Contains(InputTriggerType.MouseMiddleButton);
        IsRightMouseButtonEnabled = requiredMouseModifierButtonTriggers.Contains(InputTriggerType.MouseRightButton);
    }
}
