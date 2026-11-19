namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>DateRangePicker</c>. Place inside
/// <see cref="DatePickerDialog"/>'s body. Pass an explicit
/// <see cref="DateRangePickerState"/> to read the picked
/// range from a button callback; if none is supplied a fresh state is
/// created internally and the selection is unobservable. Same shape as
/// <see cref="DatePicker"/> — see that facade for the broader pattern.
/// </summary>
/// <remarks>
/// The generated <c>Render()</c> calls
/// <c>ComposeBridges.RememberDateRangePickerState</c> to obtain the JVM
/// state handle, populates the wrapper's <c>Jvm</c> field on first
/// render (so subsequent property reads on the wrapper hit the live
/// state), and forwards the handle into the
/// <c>androidx.compose.material3.DateRangePickerKt.DateRangePicker</c>
/// composable.
/// </remarks>
public sealed partial class DateRangePicker;
