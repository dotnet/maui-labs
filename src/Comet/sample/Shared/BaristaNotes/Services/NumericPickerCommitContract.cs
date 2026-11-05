#nullable enable

namespace CometBaristaNotes.Services;

public enum NumericPickerDismissal
{
    Close,
    Done,
}

public static class NumericPickerCommitContract
{
    public static decimal Resolve(
        decimal committedValue,
        decimal pendingValue,
        bool pendingValueChanged,
        NumericPickerDismissal dismissal) =>
        dismissal == NumericPickerDismissal.Done && pendingValueChanged
            ? pendingValue
            : committedValue;
}
