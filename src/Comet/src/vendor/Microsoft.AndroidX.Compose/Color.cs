using System.Globalization;

namespace AndroidX.Compose;

/// <summary>
/// C# mirror of Kotlin's
/// <c>androidx.compose.ui.graphics.Color</c>
/// (<c>@JvmInline value class Color(val value: ULong)</c>).
/// </summary>
/// <remarks>
/// <para>
/// Compose's <c>Color</c> is an inline value class around an unsigned 64-bit
/// integer; the wire representation crossing JNI is just the underlying
/// <see cref="long"/>. This struct preserves that wire shape while giving
/// the C# facade a typed, ergonomic surface to express colors in.
/// </para>
/// <para>
/// For the sRGB color space (the only one in common use) the packed layout
/// is <c>0xAARRGGBB_00000000UL</c> — the 32-bit ARGB value occupies the
/// high half of the long, and the low 6 bits encode the color space (0
/// for sRGB). <see cref="FromArgb(byte, byte, byte, byte)"/> and the
/// named constants follow this layout.
/// </para>
/// <para>
/// An implicit conversion to <see cref="long"/> means every bridge and
/// binding method that already accepts a packed color value
/// (<c>Modifier.Background(long)</c>, <c>Text.Color</c>,
/// <c>ColorSchemeKt.LightColorScheme(...)</c>, etc.) accepts a
/// <see cref="Color"/> directly without any conversion ceremony at the
/// call site.
/// </para>
/// </remarks>
public readonly struct Color : IEquatable<Color>
{
    /// <summary>The underlying 64-bit packed value (matches Kotlin's <c>ULong</c>).</summary>
    public ulong PackedValue { get; }

    /// <summary>Construct a <see cref="Color"/> from a raw packed value.</summary>
    /// <param name="packedValue">
    /// Packed sRGB layout: <c>0xAARRGGBB_00000000UL</c>. Use the
    /// <see cref="FromArgb(byte, byte, byte, byte)"/> /
    /// <see cref="FromHex(string)"/> factories for typical construction.
    /// </param>
    public Color(ulong packedValue)
    {
        PackedValue = packedValue;
    }

    /// <summary>Construct a <see cref="Color"/> from the packed value as a signed long.</summary>
    public Color(long packedValue)
    {
        PackedValue = unchecked((ulong)packedValue);
    }

    /// <summary>Construct an opaque sRGB color from 8-bit RGB components.</summary>
    public Color(byte red, byte green, byte blue) : this(0xFF, red, green, blue) { }

    /// <summary>Construct an sRGB color from 8-bit ARGB components.</summary>
    public Color(byte alpha, byte red, byte green, byte blue)
    {
        uint argb = ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | blue;
        PackedValue = (ulong)argb << 32;
    }

    /// <summary>The 8-bit alpha channel (0-255).</summary>
    public byte A => (byte)(PackedValue >> 56);

    /// <summary>The 8-bit red channel (0-255).</summary>
    public byte R => (byte)(PackedValue >> 48);

    /// <summary>The 8-bit green channel (0-255).</summary>
    public byte G => (byte)(PackedValue >> 40);

    /// <summary>The 8-bit blue channel (0-255).</summary>
    public byte B => (byte)(PackedValue >> 32);

    /// <summary>Construct an sRGB color from 8-bit ARGB components.</summary>
    public static Color FromArgb(byte alpha, byte red, byte green, byte blue)
        => new(alpha, red, green, blue);

    /// <summary>Construct an opaque sRGB color from 8-bit RGB components.</summary>
    public static Color FromRgb(byte red, byte green, byte blue)
        => new(0xFF, red, green, blue);

    /// <summary>Construct an sRGB color from a packed 32-bit ARGB integer (<c>0xAARRGGBB</c>).</summary>
    public static Color FromArgb(uint argb) => new((ulong)argb << 32);

    /// <summary>
    /// Parse a CSS-style hex color literal. Accepts <c>#RGB</c>, <c>#RRGGBB</c>,
    /// or <c>#AARRGGBB</c>, with or without the leading <c>#</c>. <c>#RGB</c>
    /// and <c>#RRGGBB</c> are treated as fully opaque.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="hex"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The string is not a valid color literal.</exception>
    public static Color FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var span = hex.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#') span = span.Slice(1);
        switch (span.Length)
        {
            case 3:
                {
                    int r = HexNibble(span[0]);
                    int g = HexNibble(span[1]);
                    int b = HexNibble(span[2]);
                    return FromArgb(0xFF, (byte)(r * 0x11), (byte)(g * 0x11), (byte)(b * 0x11));
                }
            case 6:
                {
                    uint rgb = uint.Parse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    return FromArgb(0xFF000000u | rgb);
                }
            case 8:
                {
                    uint argb = uint.Parse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    return FromArgb(argb);
                }
            default:
                throw new FormatException(
                    $"Color hex literal must be #RGB, #RRGGBB, or #AARRGGBB; got '{hex}'.");
        }
    }

    static int HexNibble(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => throw new FormatException($"Invalid hex digit '{c}'."),
    };

    /// <summary>
    /// Return a copy of this color with the alpha channel replaced.
    /// </summary>
    public Color WithAlpha(byte alpha) => new(alpha, R, G, B);

    /// <summary>
    /// Return a copy of this color with the alpha channel set to
    /// <paramref name="opacity"/> &#215; 255 (clamped to 0..1).
    /// </summary>
    public Color WithOpacity(float opacity)
    {
        if (opacity < 0f) opacity = 0f;
        if (opacity > 1f) opacity = 1f;
        return WithAlpha((byte)(opacity * 255f + 0.5f));
    }

    /// <summary>
    /// Implicit conversion to the packed <see cref="long"/> the Compose
    /// bridges and bindings accept directly.
    /// </summary>
    public static implicit operator long(Color c) => unchecked((long)c.PackedValue);

    /// <summary>
    /// Implicit conversion from the packed <see cref="long"/> the Compose
    /// bindings surface (the Kotlin
    /// <c>@JvmInline value class Color(val value: ULong)</c> lowers to a
    /// packed long across JNI). Lets callers pass <c>scheme.OnSurface</c>
    /// directly anywhere a <see cref="Color"/> is expected without
    /// wrapping in <c>new Color(...)</c>.
    /// </summary>
    public static implicit operator Color(long packedValue) => new(packedValue);

    /// <summary>
    /// Whether this color carries a real value. Matches Kotlin's
    /// <c>Color.isSpecified</c> &#8212; useful for "fall back to theme"
    /// branches where <see cref="Unspecified"/> is the sentinel.
    /// </summary>
    public bool IsSpecified => PackedValue != Unspecified.PackedValue;

    /// <summary>
    /// Inverse of <see cref="IsSpecified"/> &#8212; matches Kotlin's
    /// <c>Color.isUnspecified</c>.
    /// </summary>
    public bool IsUnspecified => PackedValue == Unspecified.PackedValue;

    // ----- Named constants matching androidx.compose.ui.graphics.Color.Companion -----

    /// <summary>Fully-opaque black (<c>#FF000000</c>).</summary>
    public static Color Black => new(0xFF000000_00000000UL);

    /// <summary>Fully-transparent / zero-alpha black — matches Kotlin's <c>Color.Transparent</c>.</summary>
    public static Color Transparent => new(0UL);

    /// <summary>Fully-opaque white (<c>#FFFFFFFF</c>).</summary>
    public static Color White => new(0xFFFFFFFF_00000000UL);

    /// <summary>Fully-opaque red (<c>#FFFF0000</c>).</summary>
    public static Color Red => new(0xFFFF0000_00000000UL);

    /// <summary>Fully-opaque green (<c>#FF00FF00</c>).</summary>
    public static Color Green => new(0xFF00FF00_00000000UL);

    /// <summary>Fully-opaque blue (<c>#FF0000FF</c>).</summary>
    public static Color Blue => new(0xFF0000FF_00000000UL);

    /// <summary>Fully-opaque cyan (<c>#FF00FFFF</c>).</summary>
    public static Color Cyan => new(0xFF00FFFF_00000000UL);

    /// <summary>Fully-opaque magenta (<c>#FFFF00FF</c>).</summary>
    public static Color Magenta => new(0xFFFF00FF_00000000UL);

    /// <summary>Fully-opaque yellow (<c>#FFFFFF00</c>).</summary>
    public static Color Yellow => new(0xFFFFFF00_00000000UL);

    /// <summary>Fully-opaque mid-gray — matches Kotlin's <c>Color.Gray</c> (<c>#FF888888</c>).</summary>
    public static Color Gray => new(0xFF888888_00000000UL);

    /// <summary>Fully-opaque light gray — matches Kotlin's <c>Color.LightGray</c> (<c>#FFCCCCCC</c>).</summary>
    public static Color LightGray => new(0xFFCCCCCC_00000000UL);

    /// <summary>Fully-opaque dark gray — matches Kotlin's <c>Color.DarkGray</c> (<c>#FF444444</c>).</summary>
    public static Color DarkGray => new(0xFF444444_00000000UL);

    /// <summary>
    /// Sentinel "no color specified" value — matches Kotlin's
    /// <c>Color.Unspecified</c>. Many Compose APIs treat this as
    /// "fall back to the parent / theme default".
    /// </summary>
    public static Color Unspecified => new(0x10UL);

    /// <inheritdoc/>
    public bool Equals(Color other) => PackedValue == other.PackedValue;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Color c && Equals(c);

    /// <inheritdoc/>
    public override int GetHashCode() => PackedValue.GetHashCode();

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Color left, Color right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Color left, Color right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString()
    {
        if (PackedValue == Unspecified.PackedValue)
            return "Color.Unspecified";
        if ((PackedValue & 0xFFFFFFFFUL) != 0)
            return $"Color(0x{PackedValue:X16})";
        return $"Color(0x{A:X2}{R:X2}{G:X2}{B:X2})";
    }
}
