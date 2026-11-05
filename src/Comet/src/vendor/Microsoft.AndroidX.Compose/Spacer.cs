namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>Spacer</c> composable — an empty layout used to
/// reserve space (typically via a sizing modifier such as
/// <c>Modifier.Size(8)</c> or <c>Modifier.Width(16)</c>) between
/// siblings inside a <see cref="Row"/> or <see cref="Column"/>.
/// </summary>
/// <remarks>
/// Unlike most Compose composables, Kotlin's <c>Spacer(modifier)</c>
/// has NO default for its modifier parameter — the JVM method has no
/// <c>$default</c> companion. The wrapper materialises
/// <c>Modifier.Companion</c> (the empty modifier) when the caller
/// doesn't supply a chain so the binding receives a non-null value.
/// </remarks>
public sealed partial class Spacer
{
    /// <summary>Empty Spacer (zero-sized). Apply a sizing modifier to give it presence.</summary>
    public Spacer() { }

    /// <summary>Convenience overload that sets <see cref="ComposableNode.Modifier"/>.</summary>
    public Spacer(Modifier modifier) => Modifier = modifier;

    /// <summary>
    /// Creates a <see cref="Spacer"/> with a fixed width and no height. Equivalent
    /// to <c>new Spacer(Modifier.Width(dp))</c>; the <see cref="int"/>-to-<see cref="Dp"/>
    /// implicit conversion lets callers write <c>Spacer.Width(8)</c>.
    /// </summary>
    public static Spacer Width(Dp dp) => new(Modifier.Companion.Width(dp));

    /// <summary>
    /// Creates a <see cref="Spacer"/> with a fixed height and no width. Equivalent
    /// to <c>new Spacer(Modifier.Height(dp))</c>; the <see cref="int"/>-to-<see cref="Dp"/>
    /// implicit conversion lets callers write <c>Spacer.Height(8)</c>.
    /// </summary>
    public static Spacer Height(Dp dp) => new(Modifier.Companion.Height(dp));

    /// <summary>
    /// Creates a square <see cref="Spacer"/> of the given size. Equivalent to
    /// <c>new Spacer(Modifier.Size(dp))</c>.
    /// </summary>
    public static Spacer Size(Dp dp) => new(Modifier.Companion.Size(dp));

    /// <summary>
    /// Creates a rectangular <see cref="Spacer"/> with the given width and height.
    /// Equivalent to <c>new Spacer(Modifier.Size(width, height))</c>.
    /// </summary>
    public static Spacer Size(Dp width, Dp height) => new(Modifier.Companion.Size(width, height));

    /// <summary>
    /// Creates a <see cref="Spacer"/> that takes a share of the leftover main-axis
    /// space inside its parent <see cref="Row"/> or <see cref="Column"/> — the
    /// canonical "push siblings apart" idiom. Equivalent to
    /// <c>new Spacer(Modifier.Weight(weight, fill))</c>. As with
    /// <see cref="Modifier.Weight"/>, only valid inside a Row/Column or
    /// Row/Column-shaped scope.
    /// </summary>
    /// <param name="weight">Relative share of the leftover space (must be positive).</param>
    /// <param name="fill">When <see langword="true"/> (the default) the Spacer
    /// fills its allotted slot; <see langword="false"/> lets it be smaller.</param>
    public static Spacer Weight(float weight, bool fill = true) =>
        new(Modifier.Companion.Weight(weight, fill));
}
