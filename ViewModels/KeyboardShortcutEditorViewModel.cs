using CommunityToolkit.Mvvm.ComponentModel;
using DeskBorder.Models;
using Windows.System;

namespace DeskBorder.ViewModels;

public sealed partial class KeyboardShortcutEditorViewModel(List<SelectionOption<VirtualKey>> virtualKeyOptions) : ObservableObject
{
    public ModifierKeySelectionViewModel RequiredKeyboardModifierSelection { get; } = new();

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial SelectionOption<VirtualKey>? SelectedVirtualKeyOption { get; set; }

    public List<SelectionOption<VirtualKey>> VirtualKeyOptions { get; } = virtualKeyOptions;

    public KeyboardShortcutSettings CreateKeyboardShortcutSettings() => new()
    {
        IsEnabled = IsEnabled,
        RequiredKeyboardModifierKeys = RequiredKeyboardModifierSelection.CreateKeyboardModifierKeys(),
        Key = SelectedVirtualKeyOption?.Value ?? VirtualKey.None
    };

    public void Load(KeyboardShortcutSettings keyboardShortcutSettings)
    {
        IsEnabled = keyboardShortcutSettings.IsEnabled;
        RequiredKeyboardModifierSelection.Load(keyboardShortcutSettings.RequiredKeyboardModifierKeys);
        SelectedVirtualKeyOption = VirtualKeyOptions.FirstOrDefault(selectionOption => selectionOption.Value == keyboardShortcutSettings.Key) ?? VirtualKeyOptions.First(selectionOption => selectionOption.Value == VirtualKey.None);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!value)
            return;

        if (SelectedVirtualKeyOption is not null && SelectedVirtualKeyOption.Value != VirtualKey.None)
            return;

        SelectedVirtualKeyOption = GetDefaultVirtualKeyOption();
    }

    private SelectionOption<VirtualKey> GetDefaultVirtualKeyOption() => VirtualKeyOptions.FirstOrDefault(selectionOption => selectionOption.Value != VirtualKey.None) ?? VirtualKeyOptions.First(selectionOption => selectionOption.Value == VirtualKey.None);
}
