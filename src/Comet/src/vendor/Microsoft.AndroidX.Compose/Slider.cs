namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>Slider</c>:
/// <code>
/// new Slider(value: pos.Value, onValueChange: v => pos.Value = v)
/// </code>
/// Calls the simple <c>(Float, (Float) -&gt; Unit)</c> overload — the
/// richer overloads (with custom <c>thumb</c> / <c>track</c> slots, or
/// a <c>SliderState</c> first param) aren't exposed in this facade.
///
/// Optional <c>ValueRange</c> property surfaces Kotlin's
/// <c>ClosedFloatingPointRange&lt;Float&gt;</c> — build with
/// <c>Kotlin.Ranges.RangesKt.RangeTo(min, max)</c>. Optional
/// <c>Colors</c> property accepts a
/// <see cref="AndroidX.Compose.Material3.SliderColors"/>; build via
/// <c>composer.SliderColors(...)</c> to override individual color slots
/// without restating the full theme.
/// </summary>
public sealed partial class Slider;
