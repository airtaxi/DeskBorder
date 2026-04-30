using CommunityToolkit.Mvvm.ComponentModel;
using DeskBorder.Helpers;
using DeskBorder.Models;
using Windows.System;

namespace DeskBorder.ViewModels;

public readonly record struct InputTriggerOptionValue(InputTriggerType TriggerType, VirtualKey VirtualKey);

public sealed partial class KeyboardShortcutEditorViewModel(List<SelectionOption<InputTriggerOptionValue>> inputTriggerOptions) : ObservableObject
{
    public ModifierKeySelectionViewModel RequiredKeyboardModifierSelection { get; } = new();

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial SelectionOption<InputTriggerOptionValue>? SelectedInputTriggerOption { get; set; }

    public List<SelectionOption<InputTriggerOptionValue>> InputTriggerOptions { get; } = inputTriggerOptions;

    public KeyboardShortcutSettings CreateKeyboardShortcutSettings() => new()
    {
        IsEnabled = IsEnabled,
        RequiredKeyboardModifierKeys = RequiredKeyboardModifierSelection.CreateKeyboardModifierKeys(),
        TriggerType = SelectedInputTriggerOption?.Value.TriggerType ?? InputTriggerType.VirtualKey,
        Key = SelectedInputTriggerOption?.Value.VirtualKey ?? VirtualKey.None
    };

    public void Load(KeyboardShortcutSettings keyboardShortcutSettings)
    {
        IsEnabled = keyboardShortcutSettings.IsEnabled;
        RequiredKeyboardModifierSelection.Load(keyboardShortcutSettings.RequiredKeyboardModifierKeys);
        SelectedInputTriggerOption = InputTriggerOptions.FirstOrDefault(selectionOption => selectionOption.Value.TriggerType == keyboardShortcutSettings.TriggerType && selectionOption.Value.VirtualKey == keyboardShortcutSettings.Key)
            ?? InputTriggerOptions.First(selectionOption => selectionOption.Value.TriggerType == InputTriggerType.VirtualKey && selectionOption.Value.VirtualKey == VirtualKey.None);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!value)
            return;

        if (SelectedInputTriggerOption is not null
            && KeyboardShortcutHelper.IsKeyboardShortcutSpecified(CreateKeyboardShortcutSettings()))
        {
            return;
        }

        SelectedInputTriggerOption = GetDefaultInputTriggerOption();
    }

    private SelectionOption<InputTriggerOptionValue> GetDefaultInputTriggerOption() => InputTriggerOptions.FirstOrDefault(selectionOption => selectionOption.Value.TriggerType != InputTriggerType.VirtualKey || selectionOption.Value.VirtualKey != VirtualKey.None)
        ?? InputTriggerOptions.First(selectionOption => selectionOption.Value.TriggerType == InputTriggerType.VirtualKey && selectionOption.Value.VirtualKey == VirtualKey.None);
}
