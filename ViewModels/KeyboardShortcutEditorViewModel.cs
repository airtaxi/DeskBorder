using CommunityToolkit.Mvvm.ComponentModel;
using DeskBorder.Helpers;
using DeskBorder.Models;
using Windows.System;

namespace DeskBorder.ViewModels;

public readonly record struct KeyboardShortcutTriggerOptionValue(KeyboardShortcutTriggerType TriggerType, VirtualKey VirtualKey);

public sealed partial class KeyboardShortcutEditorViewModel(List<SelectionOption<KeyboardShortcutTriggerOptionValue>> keyboardShortcutTriggerOptions) : ObservableObject
{
    public ModifierKeySelectionViewModel RequiredKeyboardModifierSelection { get; } = new();

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial SelectionOption<KeyboardShortcutTriggerOptionValue>? SelectedKeyboardShortcutTriggerOption { get; set; }

    public List<SelectionOption<KeyboardShortcutTriggerOptionValue>> KeyboardShortcutTriggerOptions { get; } = keyboardShortcutTriggerOptions;

    public KeyboardShortcutSettings CreateKeyboardShortcutSettings() => new()
    {
        IsEnabled = IsEnabled,
        RequiredKeyboardModifierKeys = RequiredKeyboardModifierSelection.CreateKeyboardModifierKeys(),
        TriggerType = SelectedKeyboardShortcutTriggerOption?.Value.TriggerType ?? KeyboardShortcutTriggerType.VirtualKey,
        Key = SelectedKeyboardShortcutTriggerOption?.Value.VirtualKey ?? VirtualKey.None
    };

    public void Load(KeyboardShortcutSettings keyboardShortcutSettings)
    {
        IsEnabled = keyboardShortcutSettings.IsEnabled;
        RequiredKeyboardModifierSelection.Load(keyboardShortcutSettings.RequiredKeyboardModifierKeys);
        SelectedKeyboardShortcutTriggerOption = KeyboardShortcutTriggerOptions.FirstOrDefault(selectionOption => selectionOption.Value.TriggerType == keyboardShortcutSettings.TriggerType && selectionOption.Value.VirtualKey == keyboardShortcutSettings.Key)
            ?? KeyboardShortcutTriggerOptions.First(selectionOption => selectionOption.Value.TriggerType == KeyboardShortcutTriggerType.VirtualKey && selectionOption.Value.VirtualKey == VirtualKey.None);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!value)
            return;

        if (SelectedKeyboardShortcutTriggerOption is not null
            && KeyboardShortcutHelper.IsKeyboardShortcutSpecified(CreateKeyboardShortcutSettings()))
        {
            return;
        }

        SelectedKeyboardShortcutTriggerOption = GetDefaultKeyboardShortcutTriggerOption();
    }

    private SelectionOption<KeyboardShortcutTriggerOptionValue> GetDefaultKeyboardShortcutTriggerOption() => KeyboardShortcutTriggerOptions.FirstOrDefault(selectionOption => selectionOption.Value.TriggerType != KeyboardShortcutTriggerType.VirtualKey || selectionOption.Value.VirtualKey != VirtualKey.None)
        ?? KeyboardShortcutTriggerOptions.First(selectionOption => selectionOption.Value.TriggerType == KeyboardShortcutTriggerType.VirtualKey && selectionOption.Value.VirtualKey == VirtualKey.None);
}
