using AndroidX.Compose.Material3;
using Kotlin.Ranges;

namespace AndroidX.Compose;

/// <summary>
/// Caller-supplied state holder for <see cref="DatePicker"/> /
/// <see cref="DatePickerDialog"/>. The underlying JVM
/// <c>androidx.compose.material3.DatePickerState</c> is created lazily
/// the first time a <see cref="DatePicker"/> bound to this state is
/// rendered; reads/writes to <see cref="SelectedDateMillis"/> before
/// that point are no-ops/fallbacks.
/// </summary>
/// <remarks>
/// Typical usage — <c>Remember</c> a state instance, pass it to a
/// <see cref="DatePicker"/>, and read the picked value from the
/// dialog's <c>ConfirmButton.OnClick</c>:
/// <code>
/// var pickerState = Remember(() =&gt; new DatePickerState());
///
/// new DatePickerDialog(onDismissRequest: ...)
/// {
///     ConfirmButton = new Button(onClick: () =&gt;
///     {
///         var ms = pickerState.SelectedDateMillis;
///         // ms is the picked date as Unix epoch milliseconds (UTC).
///     })
///     { new Text("OK") },
///     Body          = new DatePicker(pickerState),
/// }
/// </code>
///
/// The constructor's optional parameters seed the underlying state on
/// first composition (via the Phase 4b <c>RememberDatePickerState</c>
/// bridge). They're read once when the JVM state is first allocated;
/// mutating <see cref="InitialSelectedDateMillis"/> /
/// <see cref="InitialYearRange"/> / <see cref="InitialSelectableDates"/>
/// after binding has no effect unless the host re-keys the surrounding
/// <c>composer.Remember(...)</c> to force a fresh allocation.
/// </remarks>
public sealed class DatePickerState
{
    internal IDatePickerState? Jvm;

    /// <summary>
    /// Constructs an empty state holder. The underlying JVM
    /// <c>DatePickerState</c> is allocated on first <see cref="DatePicker"/>
    /// render with all values left at Kotlin's defaults.
    /// </summary>
    public DatePickerState()
    {
    }

    /// <summary>
    /// Constructs a state holder seeded with the supplied initial values.
    /// All parameters are optional — passing <c>null</c> keeps Kotlin's
    /// default for that slot (auto-default-mask leaves the matching
    /// <c>$default</c> bit set).
    /// </summary>
    /// <param name="initialSelectedDateMillis">Initial selection as Unix
    /// epoch milliseconds (UTC), or <c>null</c> for "no initial selection".</param>
    /// <param name="initialYearRange">Inclusive range of selectable years
    /// shown in the year-grid. <c>null</c> keeps Kotlin's default
    /// (1900–2100).</param>
    /// <param name="initialSelectableDates">Per-day enable/disable
    /// adapter. <c>null</c> keeps Kotlin's default (every date
    /// selectable).</param>
    public DatePickerState(
        long?              initialSelectedDateMillis = null,
        IntRange?          initialYearRange          = null,
        ISelectableDates?  initialSelectableDates    = null)
    {
        if (initialSelectedDateMillis is long ms)
            InitialSelectedDateMillis = Java.Lang.Long.ValueOf(ms);
        InitialYearRange       = initialYearRange;
        InitialSelectableDates = initialSelectableDates;
    }

    /// <summary>
    /// Initial selection as a boxed <see cref="Java.Lang.Long"/>
    /// (Kotlin's <c>Long?</c>). Read by the Phase 4b
    /// <c>RememberDatePickerState</c> bridge on first composition;
    /// mutating after binding has no effect (the live value lives in
    /// <see cref="SelectedDateMillis"/>). Most callers should use the
    /// <c>long?</c> constructor parameter instead of touching this
    /// directly.
    /// </summary>
    public Java.Lang.Long? InitialSelectedDateMillis { get; set; }

    /// <summary>
    /// First-of-month milliseconds for the initial month shown by the
    /// picker. Kotlin's <c>initialDisplayedMonthMillis</c> slot —
    /// usually left <c>null</c> so Kotlin defaults to the month
    /// containing <see cref="InitialSelectedDateMillis"/>.
    /// </summary>
    public Java.Lang.Long? InitialDisplayedMonthMillis { get; set; }

    /// <summary>
    /// Inclusive year range for the year-grid view. Read once by the
    /// Phase 4b <c>RememberDatePickerState</c> bridge on first
    /// composition.
    /// </summary>
    public IntRange? InitialYearRange { get; set; }

    /// <summary>
    /// Initial display mode (<c>DatePicker</c> or <c>Input</c>). Maps
    /// to Kotlin's <c>DisplayMode</c> packed-int enum. <c>null</c> uses
    /// Kotlin's default (calendar mode).
    /// </summary>
    public int? InitialDisplayMode { get; set; }

    /// <summary>
    /// Per-day / per-year enable/disable adapter. Read once by the
    /// Phase 4b <c>RememberDatePickerState</c> bridge on first
    /// composition. The adapter itself can mutate state — Kotlin
    /// re-invokes <c>isSelectableDate</c> / <c>isSelectableYear</c> on
    /// every grid render — but the adapter <i>instance</i> reference
    /// participates in <c>remember</c>'s key, so callers must hold a
    /// stable reference (e.g. allocate one adapter per host instance
    /// as a <c>readonly</c> field, mirroring the Phase 10
    /// <c>ConfirmStateChange</c> pattern).
    /// </summary>
    public ISelectableDates? InitialSelectableDates { get; set; }

    /// <summary>
    /// The currently selected date as Unix epoch milliseconds (UTC), or
    /// <c>null</c> if no date is selected. Mirrors Kotlin's
    /// <c>DatePickerState.selectedDateMillis: Long?</c>. Returns
    /// <c>null</c> until the first <see cref="DatePicker"/> render binds
    /// this state to the JVM picker.
    /// </summary>
    public long? SelectedDateMillis
    {
        get => Jvm?.SelectedDateMillis?.LongValue();
        set
        {
            if (Jvm is not null)
                Jvm.SelectedDateMillis = value is long ms ? Java.Lang.Long.ValueOf(ms) : null;
        }
    }

    /// <summary>
    /// First-of-month milliseconds for the month currently shown by the
    /// picker. Mirrors Kotlin's <c>DatePickerState.displayedMonthMillis</c>.
    /// Returns <c>0</c> until the state is bound.
    /// </summary>
    public long DisplayedMonthMillis
    {
        get => Jvm?.DisplayedMonthMillis ?? 0L;
        set { if (Jvm is not null) Jvm.DisplayedMonthMillis = value; }
    }
}
