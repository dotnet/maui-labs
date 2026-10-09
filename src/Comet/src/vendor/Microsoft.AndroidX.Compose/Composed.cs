using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Composer-aware <see cref="ComposableNode"/> that defers tree
/// construction until <see cref="Render(IComposer)"/> runs. The wrapped
/// <see cref="Func{T, TResult}"/> receives the active
/// <see cref="IComposer"/> so the builder can read snapshot-state
/// values, call <see cref="ComposeExtensions.ColorScheme(IComposer)"/>,
/// or invoke any other API that takes an <see cref="IComposer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Use this where you'd otherwise need to subclass
/// <see cref="ComposableNode"/> and override
/// <see cref="ComposableNode.Render(IComposer)"/> — for example a
/// sample-side helper that needs to thread the composer into a static
/// builder method:
/// </para>
/// <code>
/// new Composed(c =&gt;
/// {
///     var scheme = c.ColorScheme();
///     return new Text("Hello")
///     {
///         Color = Color.FromArgb(scheme.OnSurfaceVariant),
///     };
/// })
/// </code>
/// <para>
/// The wrapped builder is invoked once per composition pass; the
/// returned subtree is then rendered with the same
/// <see cref="IComposer"/> instance. The builder is not memoized — if
/// you need to cache a subtree across recompositions, wrap the
/// per-render allocation behind <c>composer.Remember(...)</c>.
/// </para>
/// <para>
/// Returning <see langword="null"/> from the builder is allowed and
/// renders nothing.
/// </para>
/// </remarks>
public sealed class Composed : ComposableNode
{
    readonly Func<IComposer, ComposableNode?> _builder;

    /// <summary>
    /// Create a composer-aware wrapper around <paramref name="builder"/>.
    /// </summary>
    public Composed(Func<IComposer, ComposableNode?> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <summary>
    /// Implicit conversion from a composer-aware builder lambda to a
    /// <see cref="Composed"/>, so callers can drop the explicit
    /// <c>new Composed(...)</c> wrapper whenever the target type is
    /// known to be <see cref="Composed"/>:
    /// <code>
    /// Composed node = c =&gt; new Text(c.ColorScheme().OnSurface.ToString());
    /// </code>
    /// <para>
    /// Note: C# only considers user-defined implicit conversions when the
    /// expression already has a target type that is a delegate
    /// (<see cref="Func{T, TResult}"/> here). A bare lambda assigned to
    /// <see cref="ComposableNode"/> still needs an explicit
    /// <c>new Composed(...)</c> wrapper — collection-initializer support
    /// for the bare-lambda form is provided separately via
    /// <see cref="ComposableContainer.Add(System.Func{AndroidX.Compose.Runtime.IComposer, AndroidX.Compose.ComposableNode?})"/>.
    /// </para>
    /// </summary>
    public static implicit operator Composed(Func<IComposer, ComposableNode?> builder) =>
        new(builder);

    /// <inheritdoc />
    public override void Render(IComposer composer)
    {
        var node = _builder(composer);
        node?.Render(composer);
    }

    /// <summary>
    /// Forward a parent-supplied <c>PaddingValues</c> handle (e.g. from
    /// <see cref="Scaffold"/>'s content lambda) through to the node the
    /// builder produces, so the inner node's <c>BuildModifier</c> picks
    /// it up as if it had been the body itself.
    ///
    /// The base <see cref="ComposableNode"/> implementation stashes the
    /// handle on the receiving node and consumes it in
    /// <c>BuildModifier</c> — but <see cref="Composed"/> never calls
    /// <c>BuildModifier</c> on itself; it just delegates to the
    /// builder's result. Without this override the padding is dropped
    /// on the floor and the inner content renders behind the top /
    /// bottom bars.
    /// </summary>
    internal override void Render(IComposer composer, IntPtr contentPadding)
    {
        var node = _builder(composer);
        if (node is null)
            return;
        if (contentPadding == IntPtr.Zero)
            node.Render(composer);
        else
            node.Render(composer, contentPadding);
    }
}
