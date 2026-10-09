namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>DatePicker</c>. Place inside <see cref="DatePickerDialog"/>'s
/// body. Pass an explicit <see cref="DatePickerState"/> to
/// read the selection from a button callback; if none is supplied a
/// fresh state is created internally and the selection is unobservable.
/// </summary>
/// <remarks>
/// The generated <c>Render()</c> calls
/// <c>ComposeBridges.RememberDatePickerState</c> to obtain the JVM
/// state handle, populates the wrapper's <c>Jvm</c> field on first
/// render (so subsequent property reads on the wrapper hit the live
/// state), and forwards the handle into the
/// <c>androidx.compose.material3.DatePickerKt.DatePicker</c> composable.
/// </remarks>
public sealed partial class DatePicker;
