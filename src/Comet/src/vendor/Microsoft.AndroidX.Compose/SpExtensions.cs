namespace AndroidX.Compose;

/// <summary>
/// Extension method that lets you write <c>16.Sp()</c> as the C#
/// counterpart of Kotlin's <c>16.sp</c> property syntax, producing a
/// typed <see cref="T:AndroidX.Compose.Sp"/> from any numeric literal or variable.
/// </summary>
/// <remarks>
/// Like <see cref="DpExtensions"/>, this is the right choice when you
/// need an explicit <see cref="T:AndroidX.Compose.Sp"/> on the right-hand side — hoisting
/// a typography constant into a typed local (<c>var caption = 12.Sp();</c>)
/// or participating in arithmetic (<c>base * 1.2f</c>).
/// </remarks>
public static class SpExtensions
{
    /// <summary>
    /// Convert an <see cref="int"/> count of scale-independent pixels into a
    /// typed <see cref="T:AndroidX.Compose.Sp"/>. Equivalent to Kotlin's <c>Int.sp</c>.
    /// Delegates to the bound <c>TextUnitKt.GetSp(int)</c> factory.
    /// </summary>
    public static Sp Sp(this int value) => new(value);
}
