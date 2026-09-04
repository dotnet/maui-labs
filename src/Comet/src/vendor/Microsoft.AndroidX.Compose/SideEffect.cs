using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Tree-syntax wrapper around <see cref="ComposeExtensions.SideEffect(Action)"/>.
/// Add to a container to run a callback on every successful
/// recomposition of that container, after the composition has been
/// applied. Useful when you'd rather express the effect as a sibling
/// in your composable tree than as a statement inside a custom
/// <c>Render</c> override:
/// <code>
/// new Column
/// {
///     new Text(_count.Value.ToString()),
///     new SideEffect(() =&gt; Log.Info("rendered count = " + _count.Value)),
/// }
/// </code>
/// </summary>
public sealed class SideEffect : ComposableNode
{
    readonly Action _effect;

    /// <summary>Create the node. <paramref name="effect"/> runs
    /// every successful recomposition.</summary>
    public SideEffect(Action effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _effect = effect;
    }

    public override void Render(IComposer composer)
        => composer.SideEffect(_effect);
}
