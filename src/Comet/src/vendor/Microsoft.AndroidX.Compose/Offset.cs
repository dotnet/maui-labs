namespace AndroidX.Compose;

/// <summary>
/// C# wrapper around Kotlin's <c>androidx.compose.ui.geometry.Offset</c>.
/// </summary>
/// <remarks>
/// <para>
/// In Kotlin source, <c>Offset</c> is a <c>@JvmInline value class
/// Offset(val packedValue: Long)</c> that packs two <see cref="float"/>
/// coordinates into a 64-bit value: <see cref="X"/> in the high 32 bits
/// (raw IEEE-754 bits), <see cref="Y"/> in the low 32 bits. Compose uses
/// it as the position type for pointer events, layout offsets, gesture
/// detector callbacks, etc.
/// </para>
/// <para>
/// At the JVM boundary, the value-class lowering means a function
/// returning <c>Offset</c> actually returns a plain <c>long</c>, and
/// <c>Function1&lt;Offset, Unit&gt;</c> receives a boxed
/// <c>androidx.compose.ui.geometry.Offset</c> wrapper. The internal
/// <see cref="FromPacked"/> / <see cref="Packed"/> helpers handle both
/// representations; the typical C# caller works only with
/// <see cref="X"/> / <see cref="Y"/>.
/// </para>
/// <para>
/// Will swap to bound <c>AndroidX.Compose.UI.Geometry.Offset</c> once
/// <see href="https://github.com/dotnet/android-libraries/pull/1440"/>
/// ships and the binder stops stripping inline-class APIs.
/// </para>
/// </remarks>
public readonly struct Offset : IEquatable<Offset>
{
    readonly long _packed;

    /// <summary>Construct from raw X/Y coordinates (typically pixels).</summary>
    public Offset(float x, float y)
    {
        // Match Kotlin's `packFloats(x, y)`: X is the high 32 bits, Y the low.
        // Use BitConverter so NaN / +0 / -0 round-trip exactly.
        long hi = BitConverter.SingleToInt32Bits(x) & 0xFFFFFFFFL;
        long lo = BitConverter.SingleToInt32Bits(y) & 0xFFFFFFFFL;
        _packed = (hi << 32) | lo;
    }

    Offset(long packed) => _packed = packed;

    internal long Packed => _packed;

    internal static Offset FromPacked(long packed) => new Offset(packed);

    /// <summary>The X coordinate (horizontal, typically pixels).</summary>
    public float X => BitConverter.Int32BitsToSingle((int)((_packed >> 32) & 0xFFFFFFFFL));

    /// <summary>The Y coordinate (vertical, typically pixels).</summary>
    public float Y => BitConverter.Int32BitsToSingle((int)(_packed & 0xFFFFFFFFL));

    /// <summary>Origin — <c>(0, 0)</c>.</summary>
    public static Offset Zero => new Offset(0f, 0f);

    /// <summary>
    /// The <c>Offset.Infinite</c> sentinel — Compose uses
    /// <c>(+∞, +∞)</c> as the "fill the whole drawing region"
    /// endpoint (e.g. the default <c>end</c> of
    /// <c>Brush.linearGradient</c>, which resolves to the
    /// bottom-right corner at draw time).
    /// </summary>
    public static Offset Infinite => new Offset(float.PositiveInfinity, float.PositiveInfinity);

    /// <summary>
    /// The <c>Offset.Unspecified</c> sentinel — Compose uses this to
    /// mean "no value". Equal-by-value to Kotlin's
    /// <c>Offset.Unspecified</c> (packed value <c>0x7FC000007FC00000</c>,
    /// i.e. <see cref="float.NaN"/> for both components).
    /// </summary>
    public static Offset Unspecified => new Offset(unchecked((long)0x7FC000007FC00000UL));

    /// <inheritdoc/>
    public bool Equals(Offset other) => _packed == other._packed;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Offset o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode() => _packed.GetHashCode();

    /// <summary>Value equality (by packed representation).</summary>
    public static bool operator ==(Offset a, Offset b) => a._packed == b._packed;

    /// <summary>Value inequality (by packed representation).</summary>
    public static bool operator !=(Offset a, Offset b) => a._packed != b._packed;

    /// <inheritdoc/>
    public override string ToString() =>
        this == Unspecified ? "Offset.Unspecified" : $"Offset({X}, {Y})";
}
