using DeskBorder.Models;

namespace DeskBorder.Services;

public interface IKeyboardModifierAbsorptionService : IDisposable
{
    bool IsRunning { get; }

    void Start();

    void Stop();

    bool AreRequiredModifierInputsPressed(ModifierGateSettings modifierGateSettings, ModifierKeySnapshot modifierKeySnapshot, MouseButtonSnapshot mouseButtonSnapshot);

    void AbsorbPressedKeyboardModifierKeys(KeyboardModifierKeys keyboardModifierKeys);

    ModifierKeySnapshot GetModifierKeySnapshot();

    bool HasRequiredModifierInputs(ModifierGateSettings modifierGateSettings);
}
