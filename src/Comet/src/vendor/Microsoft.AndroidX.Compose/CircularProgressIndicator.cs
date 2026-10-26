using AndroidX.Compose.Material3;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 indeterminate <c>CircularProgressIndicator</c>. Use to show
/// loading state when the duration is unknown:
/// <code>
/// new CircularProgressIndicator()
/// </code>
/// The determinate (progress-driven) overload is not yet wrapped — the
/// progress callback parameter requires a state-reading <c>Function0&lt;Float&gt;</c>
/// adapter that hasn't been added yet.
/// </summary>
public sealed class CircularProgressIndicator : ComposableNode
{
    /// <summary>Optional explicit color. Leave null to use the Material default.</summary>
    public Color? Color { get; set; }

    /// <summary>Optional stroke width in Dp. Leave null for the Material default.</summary>
    public float? StrokeWidthDp { get; set; }

    /// <summary>Optional explicit track color. Leave null to use the Material default.</summary>
    public Color? TrackColor { get; set; }

    public override void Render(IComposer composer)
    {
        var modifier = BuildModifier();

        int defaults = (int)CircularProgressIndicatorDefault.All;
        if (modifier is not null)        defaults &= ~(int)CircularProgressIndicatorDefault.Modifier;
        if (Color.HasValue)              defaults &= ~(int)CircularProgressIndicatorDefault.Color;
        if (StrokeWidthDp.HasValue)      defaults &= ~(int)CircularProgressIndicatorDefault.StrokeWidth;
        if (TrackColor.HasValue)         defaults &= ~(int)CircularProgressIndicatorDefault.TrackColor;

        ProgressIndicatorKt.CircularProgressIndicator(
            modifier:    modifier,
            color:       Color      is { } c ? c : 0L,
            strokeWidth: StrokeWidthDp   ?? 0f,
            trackColor:  TrackColor is { } t ? t : 0L,
            p4:          0,
            gapSize:     0f,
            _composer:   composer,
            strokeCap:   0,
            _changed:    defaults);
    }
}
