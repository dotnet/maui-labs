namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>TimeInput</c> — the keyboard-entry counterpart to
/// <see cref="TimePicker"/>. Share a single <see cref="TimePickerState"/>
/// across both facades and the user can switch between the clock and
/// keyboard layouts without losing the entered value; the generator
/// emits a shared-state preamble (<c>StateHolder.SharedState</c>) that
/// reuses the same <c>rememberTimePickerState</c> peer for both
/// facades so the cached hour/minute survive the toggle.
/// </summary>
/// <remarks>
/// As with <see cref="TimePicker"/>, leaving the <c>state</c> ctor
/// argument null auto-creates a fresh <see cref="TimePickerState"/>
/// wrapper. Reading <see cref="TimePickerState.Hour"/> /
/// <see cref="TimePickerState.Minute"/> from a button callback Just
/// Works without manual <c>remember</c> plumbing.
/// </remarks>
public sealed partial class TimeInput;
