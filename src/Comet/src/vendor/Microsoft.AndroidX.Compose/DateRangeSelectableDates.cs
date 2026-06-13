using Android.Runtime;
using AndroidX.Compose.Material3;

namespace AndroidX.Compose;

/// <summary>
/// <c>SelectableDates</c> adapter that gates calendar dates and years
/// against an inclusive <see cref="MinUtcMillis"/> /
/// <see cref="MaxUtcMillis"/> window plus an inclusive
/// <see cref="MinYear"/> / <see cref="MaxYear"/> window. Designed to be
/// allocated <strong>once per host instance</strong> as a
/// <c>readonly</c> field — the instance reference is part of the
/// <c>RememberDatePickerState</c> cache key, so re-allocating per
/// composition would invalidate the picker's cached state. Mutating
/// the bounds at any time is cheap; Kotlin re-invokes
/// <see cref="IsSelectableDate(long)"/> /
/// <see cref="IsSelectableYear(int)"/> on every grid render.
/// </summary>
/// <remarks>
/// All four bounds are nullable. <c>null</c> means "no bound on that
/// side" — e.g. only setting <see cref="MinUtcMillis"/> blocks past
/// dates while leaving the future open. This mirrors the pattern used
/// by Phase 10's <c>DrawerValueConfirmStateChange</c>: a per-instance
/// JCW that keeps stable JNI identity across recompositions while the
/// managed-side data behind it is free to change.
/// </remarks>
[Register("net/compose/DateRangeSelectableDates")]
public sealed class DateRangeSelectableDates : Java.Lang.Object, ISelectableDates
{
    /// <summary>
    /// Inclusive lower bound on the selectable date in Unix epoch
    /// milliseconds (UTC). <c>null</c> = no lower bound.
    /// </summary>
    public long? MinUtcMillis { get; set; }

    /// <summary>
    /// Inclusive upper bound on the selectable date in Unix epoch
    /// milliseconds (UTC). <c>null</c> = no upper bound.
    /// </summary>
    public long? MaxUtcMillis { get; set; }

    /// <summary>
    /// Inclusive lower bound for selectable years in the year-grid
    /// view. <c>null</c> = no lower bound.
    /// </summary>
    public int? MinYear { get; set; }

    /// <summary>
    /// Inclusive upper bound for selectable years in the year-grid
    /// view. <c>null</c> = no upper bound.
    /// </summary>
    public int? MaxYear { get; set; }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="utcTimeMillis"/> falls
    /// within <see cref="MinUtcMillis"/>..<see cref="MaxUtcMillis"/>
    /// inclusive. Either bound being <c>null</c> disables that side of
    /// the comparison.
    /// </summary>
    public bool IsSelectableDate(long utcTimeMillis) =>
        (MinUtcMillis is not long mn || utcTimeMillis >= mn) &&
        (MaxUtcMillis is not long mx || utcTimeMillis <= mx);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="year"/> falls within
    /// <see cref="MinYear"/>..<see cref="MaxYear"/> inclusive. Either
    /// bound being <c>null</c> disables that side of the comparison.
    /// </summary>
    public bool IsSelectableYear(int year) =>
        (MinYear is not int mn || year >= mn) &&
        (MaxYear is not int mx || year <= mx);
}
