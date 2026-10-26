namespace AndroidX.Compose;

/// <summary>
/// Extension methods that let you write <c>8.Dp()</c> / <c>16f.Dp()</c> as
/// the C# counterpart of Kotlin's <c>8.dp</c> / <c>16f.dp</c> property
/// syntax, producing a typed <see cref="T:AndroidX.Compose.Dp"/> from any numeric literal or
/// variable without going through the implicit conversion.
/// </summary>
/// <remarks>
/// The implicit <see cref="int"/>/<see cref="float"/> conversions on
/// <see cref="T:AndroidX.Compose.Dp"/> already cover passing a literal directly into a typed
/// <see cref="T:AndroidX.Compose.Dp"/> parameter. These extensions are the right choice when
/// you need an *explicit* <see cref="T:AndroidX.Compose.Dp"/> on the right-hand side — e.g.
/// hoisting a layout constant into a typed local (<c>var pad = 8.Dp();</c>),
/// participating in arithmetic (<c>start + 8.Dp()</c>), or disambiguating
/// the new typed <c>Arrangement.SpacedBy(Dp)</c> overload from the
/// pre-existing <c>SpacedBy(int)</c>.
/// </remarks>
public static class DpExtensions
{
    /// <summary>
    /// Convert an <see cref="int"/> count of density-independent pixels into
    /// a typed <see cref="T:AndroidX.Compose.Dp"/>. Equivalent to Kotlin's <c>Int.dp</c>.
    /// </summary>
    public static Dp Dp(this int value) => new(value);

    /// <summary>
    /// Convert a <see cref="float"/> count of density-independent pixels into
    /// a typed <see cref="T:AndroidX.Compose.Dp"/>. Equivalent to Kotlin's <c>Float.dp</c>.
    /// </summary>
    public static Dp Dp(this float value) => new(value);
}
