using AndroidX.Compose.Material3;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>LinearProgressIndicator</c>. Defaults to the
/// indeterminate animation; set <see cref="Progress"/> to a non-null
/// 0..1 value to render the determinate (progress-driven) overload:
/// <code>
/// new LinearProgressIndicator { Modifier = Modifier.FillMaxWidth() }
/// new LinearProgressIndicator { Progress = 0.45f }
/// </code>
/// The full state-reading <c>() -&gt; Float</c> overload (lambda-driven
/// progress) isn't wrapped — set <see cref="Progress"/> from a
/// <c>MutableState</c> in your render and Compose will recompose this
/// node at the next frame.
/// </summary>
public sealed class LinearProgressIndicator : ComposableNode
{
    /// <summary>
    /// Optional progress in [0, 1]. When non-null, renders the
    /// determinate overload (filled bar at the given fraction);
    /// when null, renders the indeterminate animation.
    /// </summary>
    public float? Progress { get; set; }

    /// <summary>Optional explicit color. Leave null to use the Material default.</summary>
    public Color? Color { get; set; }

    /// <summary>Optional explicit track color. Leave null to use the Material default.</summary>
    public Color? TrackColor { get; set; }

    public override void Render(IComposer composer)
    {
        var modifier = BuildModifier();

        if (Progress is { } progress)
        {
            // Determinate overload: (Float, Modifier?, J color, J trackColor,
            //                        I strokeCap; Composer; I $changed, I $default).
            // Generated `LinearProgressIndicatorDeterminateDefault` enum
            // covers slots 1..4 (progress on bit 0 is always provided
            // and skipped via the `!` prefix in `ComposeDefaults.cs`).
            var defaults = LinearProgressIndicatorDeterminateDefault.All;
            if (modifier is not null)        defaults &= ~LinearProgressIndicatorDeterminateDefault.Modifier;
            if (Color.HasValue)              defaults &= ~LinearProgressIndicatorDeterminateDefault.Color;
            if (TrackColor.HasValue)         defaults &= ~LinearProgressIndicatorDeterminateDefault.TrackColor;

#pragma warning disable CS0618 // Float-progress overload is deprecated in Compose
                               // 1.7+ in favour of the lambda overload, but the
                               // lambda variant is not yet wrapped here.
            ProgressIndicatorKt.LinearProgressIndicator(
                progress:   progress,
                modifier:   modifier,
                color:      Color      is { } pc ? pc : 0L,
                trackColor: TrackColor is { } pt ? pt : 0L,
                p4:         0,
                _composer:  composer,
                strokeCap:  0,
                _changed:   (int)defaults);
#pragma warning restore CS0618
            return;
        }

        var indeterminateDefaults = LinearProgressIndicatorDefault.All;
        if (modifier is not null)        indeterminateDefaults &= ~LinearProgressIndicatorDefault.Modifier;
        if (Color.HasValue)              indeterminateDefaults &= ~LinearProgressIndicatorDefault.Color;
        if (TrackColor.HasValue)         indeterminateDefaults &= ~LinearProgressIndicatorDefault.TrackColor;

        ProgressIndicatorKt.LinearProgressIndicator(
            modifier:   modifier,
            color:      Color      is { } c ? c : 0L,
            trackColor: TrackColor is { } t ? t : 0L,
            p3:         0,
            gapSize:    0f,
            _composer:  composer,
            strokeCap:  0,
            _changed:   (int)indeterminateDefaults);
    }
}
