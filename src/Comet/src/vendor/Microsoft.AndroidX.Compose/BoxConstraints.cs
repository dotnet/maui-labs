namespace AndroidX.Compose;

/// <summary>
/// Constraints surfaced to <see cref="BoxWithConstraints"/>'s content
/// callback. Mirrors <c>androidx.compose.foundation.layout.BoxWithConstraintsScope</c>:
/// <see cref="MinWidth"/> / <see cref="MaxWidth"/> /
/// <see cref="MinHeight"/> / <see cref="MaxHeight"/> are layout dp
/// values measured during the current composition pass. Use them to
/// pick a different child layout based on the available space (the
/// idiomatic Compose alternative to runtime "is this a phone or a
/// tablet" checks).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MaxWidth"/> / <see cref="MaxHeight"/> may be
/// <see cref="float.PositiveInfinity"/> when the parent imposes no
/// upper bound — branch on <see cref="float.IsInfinity"/> before
/// comparing against a fixed dp threshold.
/// </para>
/// <para>
/// Constraints are only valid for the call that produced them; do not
/// store the struct and read it after composition completes.
/// </para>
/// </remarks>
public readonly struct BoxConstraints
{
    /// <summary>Minimum width the content may take, in dp. Always finite.</summary>
    public float MinWidth { get; }

    /// <summary>Maximum width the content may take, in dp. May be <see cref="float.PositiveInfinity"/>.</summary>
    public float MaxWidth { get; }

    /// <summary>Minimum height the content may take, in dp. Always finite.</summary>
    public float MinHeight { get; }

    /// <summary>Maximum height the content may take, in dp. May be <see cref="float.PositiveInfinity"/>.</summary>
    public float MaxHeight { get; }

    internal BoxConstraints(float minWidth, float maxWidth, float minHeight, float maxHeight)
    {
        MinWidth  = minWidth;
        MaxWidth  = maxWidth;
        MinHeight = minHeight;
        MaxHeight = maxHeight;
    }
}
