using AndroidX.Compose.Material3;
using AndroidX.Compose.Runtime;
using Android.Runtime;
using Kotlin.Ranges;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>RangeSlider</c>. The current value is a
/// <c>(float Start, float End)</c> tuple — at <c>Render</c> time we
/// build a Kotlin <c>ClosedFloatingPointRange&lt;Float&gt;</c> from it
/// and unpack the same shape on every callback:
/// <code>
/// new RangeSlider(
///     value: (range.Value.Start, range.Value.End),
///     onValueChange: r => range.Value = r)
/// </code>
/// Calls the simpler <c>(ClosedFloatingPointRange&lt;Float&gt;, ...)</c>
/// overload — the richer overloads with custom thumb/track slots or a
/// <c>RangeSliderState</c> first param aren't exposed in this facade.
/// </summary>
public sealed class RangeSlider : ComposableNode
{
    readonly (float Start, float End) _value;
    readonly Action<(float Start, float End)> _onValueChange;

    public RangeSlider(
        (float Start, float End) value,
        Action<(float Start, float End)> onValueChange)
    {
        _value = value;
        _onValueChange = onValueChange;
    }

    public override void Render(IComposer composer)
    {
        var range = RangesKt.RangeTo(_value.Start, _value.End);

        var onChange = new ComposableLambda1(v =>
        {
            var r = v?.JavaCast<IClosedFloatingPointRange>();
            if (r is null) return;
            var lo = ((Java.Lang.Float)r.Start).FloatValue();
            var hi = ((Java.Lang.Float)r.EndInclusive).FloatValue();
            _onValueChange((lo, hi));
        });

        var modifier = BuildModifier();
        int defaults = (int)RangeSliderDefault.All;
        if (modifier is not null) defaults &= ~(int)RangeSliderDefault.Modifier;

        SliderKt.RangeSlider(
            value:                  range,
            onValueChange:          onChange,
            modifier:               modifier,
            enabled:                true,
            valueRange:             null,
            p5:                     0,
            onValueChangeFinished:  null,
            colors:                 null,
            _composer:              composer,
            steps:                  0,
            _changed:               defaults);
    }
}
