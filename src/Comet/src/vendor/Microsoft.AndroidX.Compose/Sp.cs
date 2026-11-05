namespace AndroidX.Compose;

/// <summary>
/// Scale-independent pixel — C# mirror of Kotlin's
/// <c>androidx.compose.ui.unit.TextUnit</c> in <c>Sp</c> form
/// (<c>@JvmInline value class TextUnit(val packedValue: Long)</c>).
/// </summary>
/// <remarks>
/// <para>
/// Compose's <c>TextUnit</c> is an inline value class around a <c>Long</c>;
/// the long packs a sign bit, a type tag (Sp/Em/Unspecified), and a value.
/// This struct preserves that wire shape — <see cref="PackedValue"/> is the
/// exact long that <c>TextUnitKt.GetSp(int)</c> returns — so it crosses
/// JNI unchanged.
/// </para>
/// <para>
/// Use <see cref="Sp(int)"/> or the implicit conversion from <see cref="int"/>
/// (which both delegate to the bound <c>TextUnitKt.GetSp</c> factory) to
/// construct a typed Sp value.
/// </para>
/// </remarks>
public readonly struct Sp : IEquatable<Sp>, IComparable<Sp>
{
    /// <summary>
    /// The raw packed <c>TextUnit.packedValue</c>. Pass this directly to
    /// any JNI bridge that expects a <c>long TextUnit</c> argument.
    /// </summary>
    public long PackedValue { get; }

    /// <summary>
    /// Construct an Sp from the raw packed <c>TextUnit</c> long. Use the
    /// other overloads (which take an <see cref="int"/>) for ergonomic
    /// construction; this one is for round-tripping packed values from
    /// existing bound APIs.
    /// </summary>
    public Sp(long packedValue)
    {
        PackedValue = packedValue;
    }

    /// <summary>
    /// Construct an Sp from a scale-independent pixel count by calling
    /// the bound <c>TextUnitKt.GetSp(int)</c> factory.
    /// </summary>
    public Sp(int sp)
        : this(AndroidX.Compose.UI.Unit.TextUnitKt.GetSp(sp))
    {
    }

    /// <summary>The zero-sp constant (equivalent to <c>0.sp</c> in Kotlin).</summary>
    public static Sp Zero => new(0);

    /// <summary>Implicit conversion from <see cref="int"/> for ergonomic call sites.</summary>
    public static implicit operator Sp(int value) => new Sp(value);

    /// <inheritdoc/>
    public bool Equals(Sp other) => PackedValue == other.PackedValue;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Sp sp && Equals(sp);

    /// <inheritdoc/>
    public override int GetHashCode() => PackedValue.GetHashCode();

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Sp left, Sp right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Sp left, Sp right) => !left.Equals(right);

    /// <inheritdoc/>
    /// <remarks>
    /// Delegates to Kotlin's <c>TextUnit.compareTo</c>. Defined for same-tag
    /// values (all <see cref="Sp"/> instances are Sp-tagged, so this is
    /// always safe between two <see cref="Sp"/> values).
    /// </remarks>
    public int CompareTo(Sp other) =>
        AndroidX.Compose.UI.Unit.TextUnit.CompareTo(PackedValue, other.PackedValue);

    /// <summary>
    /// Scale an <see cref="Sp"/> value by a scalar factor. Mirrors Kotlin's
    /// <c>TextUnit.times(Float)</c>.
    /// </summary>
    public static Sp operator *(Sp a, float scalar) =>
        new(AndroidX.Compose.UI.Unit.TextUnit.Times(a.PackedValue, scalar));

    /// <summary>Scale an <see cref="Sp"/> value by a scalar factor.</summary>
    public static Sp operator *(float scalar, Sp a) =>
        new(AndroidX.Compose.UI.Unit.TextUnit.Times(a.PackedValue, scalar));

    /// <summary>
    /// Divide an <see cref="Sp"/> value by a scalar factor. Mirrors Kotlin's
    /// <c>TextUnit.div(Float)</c>.
    /// </summary>
    public static Sp operator /(Sp a, float scalar) =>
        new(AndroidX.Compose.UI.Unit.TextUnit.Div(a.PackedValue, scalar));

    /// <summary>
    /// Negate an <see cref="Sp"/> value. Mirrors Kotlin's
    /// <c>TextUnit.unaryMinus()</c>.
    /// </summary>
    public static Sp operator -(Sp a) =>
        new(AndroidX.Compose.UI.Unit.TextUnit.UnaryMinus(a.PackedValue));

    /// <summary>Less-than comparison.</summary>
    public static bool operator <(Sp a, Sp b) => a.CompareTo(b) < 0;

    /// <summary>Greater-than comparison.</summary>
    public static bool operator >(Sp a, Sp b) => a.CompareTo(b) > 0;

    /// <summary>Less-than-or-equal comparison.</summary>
    public static bool operator <=(Sp a, Sp b) => a.CompareTo(b) <= 0;

    /// <summary>Greater-than-or-equal comparison.</summary>
    public static bool operator >=(Sp a, Sp b) => a.CompareTo(b) >= 0;

    /// <inheritdoc/>
    public override string ToString() => $"Sp(0x{PackedValue:X16})";

    /// <summary>
    /// Pack a nullable <see cref="Sp"/> into the packed <c>TextUnit</c>
    /// long the JNI slot expects. <c>null</c> -&gt; <c>0L</c>; the
    /// auto-mask leaves the matching <c>$default</c> bit set so
    /// Kotlin's real default applies.
    /// </summary>
    public static long Pack(Sp? value) => value?.PackedValue ?? 0L;
}
