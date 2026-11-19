namespace AndroidX.Compose;

/// <summary>
/// Mirror of Kotlin's <c>androidx.compose.ui.graphics.TransformOrigin</c> —
/// the pivot point used by transform-aware modifiers such as
/// <see cref="Modifier.GraphicsLayer"/> for scale and rotation. Compose
/// defines it as a <c>@JvmInline value class TransformOrigin(val packedValue: Long)</c>
/// that packs two <see cref="float"/>s (fractions in <c>[0, 1]</c>)
/// into a single <see cref="long"/>: <c>x</c> in the upper 32 bits,
/// <c>y</c> in the lower 32 bits. Because the binding generator
/// strips inline value-class types, the <see cref="Modifier.GraphicsLayer"/>
/// API takes the raw packed <see cref="long"/>; <see cref="Pack"/>
/// builds one from a <c>(x, y)</c> pair and <see cref="Center"/>
/// supplies the common (0.5, 0.5) default.
/// </summary>
public static class TransformOrigin
{
    /// <summary>
    /// Packed <see cref="long"/> equivalent to Compose's
    /// <c>TransformOrigin.Center</c> — pivot (0.5, 0.5), i.e. the
    /// geometric center of the composable. This is the default
    /// pivot Compose uses when none is supplied.
    /// </summary>
    public static long Center { get; } = Pack(0.5f, 0.5f);

    /// <summary>
    /// Pack a (<paramref name="pivotFractionX"/>,
    /// <paramref name="pivotFractionY"/>) pair into the
    /// <c>TransformOrigin</c> bit layout expected by
    /// <see cref="Modifier.GraphicsLayer"/>. Both fractions are
    /// typically in <c>[0, 1]</c>: <c>0</c> = left/top,
    /// <c>0.5</c> = center, <c>1</c> = right/bottom. Values outside
    /// that range are allowed but rarely useful.
    /// </summary>
    public static long Pack(float pivotFractionX, float pivotFractionY)
    {
        long x = unchecked((uint)BitConverter.SingleToInt32Bits(pivotFractionX));
        long y = unchecked((uint)BitConverter.SingleToInt32Bits(pivotFractionY));
        return (x << 32) | y;
    }
}
