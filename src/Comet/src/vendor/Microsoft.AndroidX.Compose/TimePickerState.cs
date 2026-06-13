using AndroidX.Compose.Material3;

namespace AndroidX.Compose;

/// <summary>
/// Caller-supplied state holder for <see cref="TimePicker"/> /
/// <see cref="TimePickerDialog"/>. Mirrors Kotlin's
/// <c>TimePickerState</c>: <see cref="Hour"/>/<see cref="Minute"/>
/// expose the live values; <see cref="Is24Hour"/> reflects the format.
/// </summary>
/// <remarks>
/// Typical usage — <c>Remember</c> a state instance with the desired
/// initial values, pass it to a <see cref="TimePicker"/>, and read the
/// picked time from the dialog's <c>ConfirmButton.OnClick</c>:
/// <code>
/// var pickerState = Remember(() =&gt; new TimePickerState(initialHour: 9, initialMinute: 30));
///
/// new TimePickerDialog(onDismissRequest: ...)
/// {
///     ConfirmButton = new Button(onClick: () =&gt;
///     {
///         var h = pickerState.Hour;
///         var m = pickerState.Minute;
///     })
///     { new Text("OK") },
///     Body          = new TimePicker(pickerState),
/// }
/// </code>
/// </remarks>
public sealed class TimePickerState
{
    internal ITimePickerState? Jvm;

    internal int  InitialHour   { get; }
    internal int  InitialMinute { get; }
    internal bool InitialIs24Hour { get; }

    public TimePickerState(int initialHour = 12, int initialMinute = 0, bool is24Hour = true)
    {
        InitialHour     = initialHour;
        InitialMinute   = initialMinute;
        InitialIs24Hour = is24Hour;
    }

    /// <summary>Currently displayed hour (0–23). Falls back to the
    /// constructor's <c>initialHour</c> until bound.</summary>
    public int Hour
    {
        get => Jvm?.Hour ?? InitialHour;
        set { if (Jvm is not null) Jvm.Hour = value; }
    }

    /// <summary>Currently displayed minute (0–59). Falls back to the
    /// constructor's <c>initialMinute</c> until bound.</summary>
    public int Minute
    {
        get => Jvm?.Minute ?? InitialMinute;
        set { if (Jvm is not null) Jvm.Minute = value; }
    }

    /// <summary>Whether the picker is in 24-hour mode (vs. 12-hour with
    /// AM/PM). Falls back to the constructor's <c>is24Hour</c> until
    /// bound.</summary>
    public bool Is24Hour => Jvm?.Is24hour() ?? InitialIs24Hour;
}
