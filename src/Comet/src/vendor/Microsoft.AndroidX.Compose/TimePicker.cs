namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>TimePicker</c>. The generator wires the
/// <c>rememberTimePickerState(initialHour, initialMinute, is24Hour, composer)</c>
/// round-trip and auto-creates a <see cref="TimePickerState"/> wrapper
/// when the caller leaves the <c>state</c> ctor argument null, so reading
/// <see cref="TimePickerState.Hour"/> / <see cref="TimePickerState.Minute"/>
/// from a button callback Just Works without manual <c>remember</c> plumbing.
/// </summary>
/// <remarks>
/// Opts in to <c>StateHolder.SharedState</c> so a caller-supplied
/// <see cref="TimePickerState"/> can be passed to a sibling
/// <see cref="TimeInput"/> facade without triggering a duplicate
/// <c>rememberTimePickerState</c> call — both facades reuse the same
/// Kotlin peer and stay in sync as the user toggles between clock and
/// keyboard entry.
/// </remarks>
public sealed partial class TimePicker;

