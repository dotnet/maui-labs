using System.Collections;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Base class for container composables that take a single content
/// lambda holding zero or more children. Implements
/// <see cref="IEnumerable"/> + <see cref="Add(ComposableNode)"/> so
/// C# collection-initializer syntax
/// (<c>new Column { new Text("Hi"), new Text("There") }</c>) compiles.
/// </summary>
public abstract class ComposableContainer : ComposableNode, IEnumerable
{
    readonly List<ComposableNode> _children = new();

    public void Add(ComposableNode? child)
    {
        if (child is not null)
            _children.Add(child);
    }

    /// <summary>
    /// Collection-initializer overload that lets callers drop a
    /// composer-aware builder lambda inline as a child:
    /// <code>
    /// new Column
    /// {
    ///     c =&gt; new Text(c.ColorScheme().OnSurface.ToString()),
    /// }
    /// </code>
    /// The lambda is wrapped in a <see cref="Composed"/> node, so it
    /// runs once per composition pass and may return <see langword="null"/>
    /// to skip rendering. This overload exists alongside the implicit
    /// conversion on <see cref="Composed"/> because C# can only target-
    /// type a bare lambda when the call-site parameter is itself a
    /// delegate — the implicit operator is otherwise invisible.
    /// </summary>
    public void Add(Func<IComposer, ComposableNode?> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _children.Add(new Composed(builder));
    }

    /// <summary>
    /// Collection-initializer overload that lets callers set
    /// <see cref="ComposableNode.Modifier"/> inline alongside children:
    /// <code>new Column { Modifier.Padding(16), new Text("Hi") }</code>
    /// (C# disallows mixing object-initializer assignments with
    /// collection-initializer items in the same braces, so the modifier
    /// is set via <c>Add</c> instead.)
    /// </summary>
    public void Add(Modifier modifier) => Modifier = modifier;
    IEnumerator IEnumerable.GetEnumerator() => _children.GetEnumerator();

    /// <summary>Accessor for derived <c>Render</c> impls.</summary>
    protected IReadOnlyList<ComposableNode> Children => _children;

    /// <summary>
    /// Renders this container's children sequentially into
    /// <paramref name="composer"/>, wrapping each child in a per-position
    /// <c>StartReplaceableGroup</c> whose key combines the sibling index
    /// <em>and</em> the child's runtime <see cref="Type"/>. Without per-
    /// position groups, three sibling <c>SegmentedButton</c>s (same C#
    /// call site → same group key) rely on Compose's positional
    /// disambiguation, which combined with lambda-identity churn
    /// elsewhere can land Reuse/Move ops at the wrong tree index.
    /// Without the type component, a sibling that swaps to a different
    /// <see cref="ComposableNode"/> subclass at the same position
    /// (e.g. tab navigation flipping a <c>PullToRefreshBox</c> for a
    /// <c>HorizontalUncontainedCarousel</c>) would re-enter the prior
    /// occupant's group and read its slot-table entries, throwing
    /// <c>ClassCastException</c> from inside <c>rememberSaveable</c>
    /// when the prior slot held an incompatible type. Same-typed
    /// siblings at the same position keep their identity and slot
    /// state intact — that is intentional Compose positional identity.
    /// </summary>
    protected void RenderChildren(IComposer composer)
    {
        for (int i = 0; i < _children.Count; i++)
        {
            var child = _children[i];
            composer.StartReplaceableGroup(HashCode.Combine(i, child.GetType()));
            try { child.Render(composer); }
            finally { composer.EndReplaceableGroup(); }
        }
    }

    /// <summary>
    /// Renders this container's children sequentially while publishing
    /// the per-child row position via
    /// <see cref="RenderContext.PushRow"/> + <c>SetIndex</c>. Child
    /// composables (e.g. <see cref="SegmentedButton"/>) read
    /// <see cref="RenderContext.CurrentRowChildIndex"/> and
    /// <see cref="RenderContext.CurrentRowChildCount"/> to compute
    /// Kotlin defaults that depend on their position in the row
    /// (start/end shape, etc.). Each child still gets the same
    /// per-position <c>StartReplaceableGroup</c> key as
    /// <see cref="RenderChildren"/> — see that method for the
    /// positional-identity rationale.
    /// </summary>
    private protected void RenderChildrenIndexed(IComposer composer)
    {
        using var rows = RenderContext.PushRow(_children.Count);
        for (int i = 0; i < _children.Count; i++)
        {
            rows.SetIndex(i);
            var child = _children[i];
            composer.StartReplaceableGroup(HashCode.Combine(i, child.GetType()));
            try { child.Render(composer); }
            finally { composer.EndReplaceableGroup(); }
        }
    }
}
