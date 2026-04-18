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

    public KeyboardModifierKeys CreateKeyboardModifierKeys()
    {
        var keyboardModifierKeys = KeyboardModifierKeys.None;
        if (IsShiftEnabled)
            keyboardModifierKeys |= KeyboardModifierKeys.Shift;

        if (IsControlEnabled)
            keyboardModifierKeys |= KeyboardModifierKeys.Control;

        if (IsAlternateEnabled)
            keyboardModifierKeys |= KeyboardModifierKeys.Alternate;

        if (IsWindowsEnabled)
            keyboardModifierKeys |= KeyboardModifierKeys.Windows;

        return keyboardModifierKeys;
    }

    public void Load(KeyboardModifierKeys keyboardModifierKeys)
    {
        IsShiftEnabled = keyboardModifierKeys.HasFlag(KeyboardModifierKeys.Shift);
        IsControlEnabled = keyboardModifierKeys.HasFlag(KeyboardModifierKeys.Control);
        IsAlternateEnabled = keyboardModifierKeys.HasFlag(KeyboardModifierKeys.Alternate);
        IsWindowsEnabled = keyboardModifierKeys.HasFlag(KeyboardModifierKeys.Windows);
    }
}
