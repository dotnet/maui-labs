using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Container composable that installs one or more
/// <see cref="ProvidedValue"/>s for the duration of its child
/// composition. Mirrors Kotlin's
/// <c>CompositionLocalProvider(*values) { ... }</c>:
/// <code>
/// new CompositionLocalProvider {
///     LocalMyTheme.Provides(customTheme),
///     LocalContext.Provides(scopedContext),
///     new Text("inherits theme + context"),
/// }
/// </code>
///
/// <para><b>Ordering rule.</b> All <see cref="ProvidedValue"/> entries
/// in a collection initializer must come <em>before</em> any
/// <see cref="ComposableNode"/> children. Mixing them would suggest
/// the provider only applies to the children that follow it visually,
/// but in fact a single provider call installs every value for the
/// whole subtree. Adding a child first and then a provided value
/// throws <see cref="InvalidOperationException"/> to keep the source
/// layout honest.</para>
/// </summary>
public sealed class CompositionLocalProvider : ComposableContainer
{
    readonly List<ProvidedValue> _provided = new();

    /// <summary>
    /// Collection-initializer hook for <see cref="ProvidedValue"/>.
    /// Must precede every child in the initializer block; see the
    /// type-level remarks for why.
    /// </summary>
    public void Add(ProvidedValue value)
    {
        if (value is null) return;
        if (Children.Count != 0)
            throw new InvalidOperationException(
                "CompositionLocalProvider: every ProvidedValue must be listed "
                + "before any ComposableNode child. The provided values apply "
                + "to the entire child subtree, not only the children that "
                + "follow them in source order.");
        _provided.Add(value);
    }

    public override void Render(IComposer composer)
    {
        if (_provided.Count == 0)
        {
            // Nothing to install — render children transparently so an
            // empty provider behaves like a plain group.
            RenderChildren(composer);
            return;
        }

        var content = ComposableLambdas.Wrap2(composer, c => RenderChildren(c));

        if (_provided.Count == 1)
        {
            // Use the single-value overload — it's the common case for
            // theme overrides and is cheaper than allocating a 1-length
            // array.
            CompositionLocalKt.CompositionLocalProvider(
                value:     _provided[0].Peer,
                content:   content,
                _composer: composer,
                _changed:  0);
            return;
        }

        var array = new AndroidX.Compose.Runtime.ProvidedValue[_provided.Count];
        for (int i = 0; i < _provided.Count; i++)
            array[i] = _provided[i].Peer;

        CompositionLocalKt.CompositionLocalProvider(
            values:    array,
            content:   content,
            _composer: composer,
            _changed:  0);
    }
}
