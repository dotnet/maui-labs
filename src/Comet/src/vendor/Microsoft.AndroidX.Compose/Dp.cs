namespace AndroidX.Compose;

/// <summary>
/// Density-independent pixel — C# mirror of Kotlin's
/// <c>androidx.compose.ui.unit.Dp</c> (<c>@JvmInline value class Dp(val value: Float)</c>).
/// </summary>
/// <remarks>
/// <para>
/// Compose's <c>Dp</c> is an inline value class around a <c>Float</c>; the
/// wire representation crossing JNI is just the underlying <see cref="float"/>.
/// This struct preserves that wire shape while giving the C# facade a typed
/// surface to accept density-independent values in.
/// </para>
/// <para>
/// Implicit conversions from <see cref="int"/> and <see cref="float"/> mean
/// existing call sites like <c>Modifier.Padding(16)</c> continue to
/// compile unchanged. To explicitly construct a <c>Dp</c>, use the
/// <see cref="Dp(float)"/> constructor or one of the conversion operators.
/// </para>
/// </remarks>
public readonly struct Dp : IEquatable<Dp>, IComparable<Dp>
{
    /// <summary>The underlying density-independent pixel count.</summary>
    public float Value { get; }

    /// <summary>Construct a <see cref="Dp"/> from the underlying float value.</summary>
    public Dp(float value)
    {
        Value = value;
    }

    /// <summary>The zero-dp constant (equivalent to <c>0.dp</c> in Kotlin).</summary>
    public static Dp Zero => new(AndroidX.Compose.UI.Unit.DpKt.GetDp(0));

    /// <summary>Implicit conversion from <see cref="int"/> for ergonomic call sites.</summary>
    public static implicit operator Dp(int value) => new Dp(value);

    /// <summary>Implicit conversion from <see cref="float"/> for ergonomic call sites.</summary>
    public static implicit operator Dp(float value) => new Dp(value);

    /// <inheritdoc/>
    public bool Equals(Dp other) => Value.Equals(other.Value);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Dp dp && Equals(dp);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc/>
    public int CompareTo(Dp other) => Value.CompareTo(other.Value);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Dp left, Dp right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Dp left, Dp right) => !left.Equals(right);

    /// <summary>Add two <see cref="Dp"/> values.</summary>
    public static Dp operator +(Dp a, Dp b) => new(a.Value + b.Value);

    /// <summary>Subtract one <see cref="Dp"/> value from another.</summary>
    public static Dp operator -(Dp a, Dp b) => new(a.Value - b.Value);

    /// <summary>Scale a <see cref="Dp"/> value by a scalar factor.</summary>
    public static Dp operator *(Dp a, float scalar) => new(a.Value * scalar);

    /// <summary>Scale a <see cref="Dp"/> value by a scalar factor.</summary>
    public static Dp operator *(float scalar, Dp a) => new(a.Value * scalar);

    /// <summary>Divide a <see cref="Dp"/> value by a scalar factor.</summary>
    public static Dp operator /(Dp a, float scalar) => new(a.Value / scalar);

    /// <summary>
    /// Divide one <see cref="Dp"/> by another and get the dimensionless ratio
    /// (mirrors Kotlin's <c>Dp.div(Dp): Float</c>).
    /// </summary>
    public static float operator /(Dp a, Dp b) => a.Value / b.Value;

    /// <summary>Negate a <see cref="Dp"/> value.</summary>
    public static Dp operator -(Dp a) => new(-a.Value);

    /// <summary>Less-than comparison.</summary>
    public static bool operator <(Dp a, Dp b) => a.Value < b.Value;

    /// <summary>Greater-than comparison.</summary>
    public static bool operator >(Dp a, Dp b) => a.Value > b.Value;

    /// <summary>Less-than-or-equal comparison.</summary>
    public static bool operator <=(Dp a, Dp b) => a.Value <= b.Value;

    /// <summary>Greater-than-or-equal comparison.</summary>
    public static bool operator >=(Dp a, Dp b) => a.Value >= b.Value;

    /// <inheritdoc/>
    public override string ToString() => $"{Value}.dp";

    /// <summary>
    /// Pack a nullable <see cref="Dp"/> into the raw <c>float</c> the
    /// JNI slot expects. <c>null</c> -&gt; <c>0f</c>, which the auto-mask
    /// in the bridge generator pairs with leaving the matching
    /// <c>$default</c> bit set so Kotlin substitutes its real default.
    /// </summary>
    public static float Pack(Dp? value) => value?.Value ?? 0f;
}
