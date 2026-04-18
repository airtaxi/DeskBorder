namespace DeskBorder.ViewModels;

public sealed class SelectionOption<TValue>(TValue value, string displayText) : SelectionOptionBase(displayText)
{
    public TValue Value { get; } = value;

    public override string ToString() => DisplayText;
}
