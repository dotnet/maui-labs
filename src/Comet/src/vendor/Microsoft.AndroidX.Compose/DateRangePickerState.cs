using AndroidX.Compose.Material3;

namespace AndroidX.Compose;

/// <summary>
/// Caller-supplied state holder for <see cref="DateRangePicker"/>. The
/// underlying JVM <c>androidx.compose.material3.DateRangePickerState</c>
/// is created lazily the first time a <see cref="DateRangePicker"/>
/// bound to this state is rendered; before that point reads from
/// <see cref="SelectedStartDateMillis"/> /
/// <see cref="SelectedEndDateMillis"/> return <c>null</c> and calls to
/// <see cref="SetSelection"/> are no-ops.
/// </summary>
/// <remarks>
/// Typical usage — <c>Remember</c> a state instance, pass it to a
/// <see cref="DateRangePicker"/>, and read the picked range from the
/// dialog's <c>ConfirmButton.OnClick</c>:
/// <code>
/// var rangeState = Remember(() =&gt; new DateRangePickerState());
///
/// new DatePickerDialog(onDismissRequest: ...)
/// {
///     ConfirmButton = new Button(onClick: () =&gt;
///     {
///         var start = rangeState.SelectedStartDateMillis;
///         var end   = rangeState.SelectedEndDateMillis;
///         // start/end are Unix epoch milliseconds (UTC), nullable.
///     })
///     { new Text("OK") },
///     Body          = new DateRangePicker(rangeState),
/// }
/// </code>
/// </remarks>
public sealed class DateRangePickerState
{
    internal IDateRangePickerState? Jvm;

    /// <summary>
    /// The start of the currently selected range as Unix epoch
    /// milliseconds (UTC), or <c>null</c> if no start date is selected.
    /// Mirrors Kotlin's <c>DateRangePickerState.selectedStartDateMillis: Long?</c>.
    /// Returns <c>null</c> until the first <see cref="DateRangePicker"/>
    /// render binds this state to the JVM picker. Setting either end of
    /// the range goes through the JVM <c>setSelection</c> helper to keep
    /// the start/end pair consistent.
    /// </summary>
    public long? SelectedStartDateMillis
    {
        get => Jvm?.SelectedStartDateMillis?.LongValue();
        set => SetSelection(value, SelectedEndDateMillis);
    }

    /// <summary>
    /// The end of the currently selected range as Unix epoch
    /// milliseconds (UTC), or <c>null</c> if no end date is selected.
    /// Mirrors Kotlin's <c>DateRangePickerState.selectedEndDateMillis: Long?</c>.
    /// Returns <c>null</c> until the state is bound.
    /// </summary>
    public long? SelectedEndDateMillis
    {
        get => Jvm?.SelectedEndDateMillis?.LongValue();
        set => SetSelection(SelectedStartDateMillis, value);
    }

    /// <summary>
    /// First-of-month milliseconds for the month currently shown by the
    /// picker. Mirrors Kotlin's <c>DateRangePickerState.displayedMonthMillis</c>.
    /// Returns <c>0</c> until the state is bound.
    /// </summary>
    public long DisplayedMonthMillis
    {
        get => Jvm?.DisplayedMonthMillis ?? 0L;
        set { if (Jvm is not null) Jvm.DisplayedMonthMillis = value; }
    }

    /// <summary>
    /// Sets both ends of the selected range in a single JVM round-trip.
    /// Use this when both endpoints change at once to avoid the
    /// intermediate "start without end" / "end without start" state the
    /// individual property setters would otherwise produce.
    /// </summary>
    public void SetSelection(long? startDateMillis, long? endDateMillis)
    {
        if (Jvm is null) return;
        var start = startDateMillis is long s ? Java.Lang.Long.ValueOf(s) : null;
        var end   = endDateMillis   is long e ? Java.Lang.Long.ValueOf(e) : null;
        Jvm.SetSelection(start, end);
    }
}
