using AndroidX.Compose.Runtime;
using AndroidX.Compose.UI;

namespace AndroidX.Compose;

/// <summary>
/// Base type of every composable in the AndroidX.Compose tree-style facade.
///
/// A <see cref="ComposableNode"/> is a passive AST node: building it
/// (<c>new Text("Hi")</c>, <c>new Column { ... }</c>) does NOT call into
/// Compose. The activity walks the tree and calls
/// <see cref="Render(IComposer)"/> during composition, threading the
/// <see cref="IComposer"/> through container nodes to their children.
///
/// This is the C# moral equivalent of Kotlin Compose's IR transform —
/// the composer is explicit at the implementation layer (honest mirror
/// of <c>ComposerParamTransformer</c>), invisible at the user layer.
/// </summary>
public abstract class ComposableNode
{
    /// <summary>
    /// Optional <see cref="AndroidX.Compose.Modifier"/> chain applied to this
    /// composable. For leaf composables, set via object-initializer:
    /// <code>new Text("Hi") { Modifier = Modifier.Padding(8) }</code>
    /// For container composables (which use collection-initializer syntax),
    /// add the modifier inline alongside the children:
    /// <code>new Column { Modifier.Padding(16), new Text("Hi") }</code>
    /// Composables without a Kotlin <c>modifier</c> parameter
    /// (e.g. <see cref="MaterialTheme"/>) ignore this property.
    /// </summary>
    public Modifier? Modifier { get; set; }

    Modifier? _prepended;
    Modifier? _appended;

    // PaddingValues handle handed to this node by a parent layout
    // (e.g. Scaffold) via Render(IComposer, IntPtr). The next call
    // to BuildModifier on this node prepends a Modifier.padding(values)
    // op for it. Per-instance so descendants don't inherit it;
    // save/restored by the virtual so re-entrant renders nest cleanly.
    // See issue #46.
    IntPtr _contentPadding;

    /// <summary>
    /// Set the <see cref="AndroidX.Compose.Modifier"/> to prepend at the
    /// START of this node's modifier chain on the next call to
    /// <see cref="BuildModifier"/>. Each call REPLACES any prior
    /// prepended modifier — use <see cref="Modifier.Then"/>
    /// at the call site to combine multiple ops into one.
    ///
    /// Intended use: a parent layout that needs to pass a runtime
    /// modifier into a child without inserting a wrapper layout node.
    /// For the specific case of forwarding a <c>PaddingValues</c>
    /// handle from <see cref="Scaffold"/> to its body, prefer the
    /// internal <c>Render(IComposer, IntPtr)</c> overload — it skips
    /// the managed <see cref="AndroidX.Compose.Modifier"/> wrapper altogether
    /// (see issue #46).
    ///
    /// Caveat: the injected modifier is silently dropped if the child's
    /// <c>Render</c> never calls <see cref="BuildModifier"/> (i.e.
    /// composables whose Kotlin counterpart has no <c>modifier</c>
    /// parameter — <see cref="MaterialTheme"/>, the drawer sheets).
    /// Replace-semantics keeps the stored modifier bounded to one ref
    /// even when the child never consumes it.
    ///
    /// Ordering: when both a prepended modifier and a runtime
    /// <c>contentPadding</c> (from <c>Render(IComposer, IntPtr)</c>)
    /// are present on the same node, the content padding runs OUTSIDE
    /// the prepended op — i.e.
    /// <c>contentPadding → prepended → Modifier → appended</c>.
    /// </summary>
    public void PrependModifier(Modifier modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        _prepended = modifier;
    }

    /// <summary>
    /// Set the <see cref="AndroidX.Compose.Modifier"/> to append at the END
    /// of this node's modifier chain on the next call to
    /// <see cref="BuildModifier"/>. Mirror of
    /// <see cref="PrependModifier"/> — see that method for semantics,
    /// caveats, and replace-vs-accumulate notes.
    /// </summary>
    public void AppendModifier(Modifier modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        _appended = modifier;
    }

    /// <summary>
    /// Materialize <see cref="Modifier"/> for the JNI call, folding in
    /// any pending <see cref="PrependModifier"/> / <see cref="AppendModifier"/>
    /// contributions and clearing them so the next composition starts
    /// fresh. When this node was rendered via the
    /// <c>Render(IComposer, IntPtr)</c> overload, also prepends a
    /// <c>Modifier.padding(contentPadding)</c> op to the chain without
    /// allocating a managed <see cref="Modifier"/> wrapper (issue #46).
    /// Returns <c>null</c> when the combined chain is empty —
    /// callers should leave the Kotlin <c>$default</c> bit set so
    /// Compose substitutes its real default.
    /// </summary>
    internal IModifier? BuildModifier()
    {
        var prepended = _prepended;
        var appended  = _appended;
        _prepended = null;
        _appended  = null;

        // Consume the PaddingValues handle stashed by
        // Render(IComposer, IntPtr); the wrapper restores the prior
        // value in its finally block so nested renders nest cleanly
        // without leaking.
        IntPtr contentPadding = _contentPadding;
        _contentPadding = IntPtr.Zero;

        Modifier? combined = prepended;
        if (Modifier is not null)
            combined = combined is null ? Modifier : combined.Then(Modifier);
        if (appended is not null)
            combined = combined is null ? appended : combined.Then(appended);

        if (contentPadding == IntPtr.Zero)
            return combined?.Build();
        return (combined ?? Modifier.Companion).Build(contentPadding);
    }

    /// <summary>
    /// Compose this node's contribution into <paramref name="composer"/>.
    ///
    /// The C# moral equivalent of an <c>@Composable</c> function body
    /// in Kotlin: anything that runs at composition time — emitting
    /// child nodes, reading <see cref="CompositionLocal{T}.Current(IComposer)"/>,
    /// calling <see cref="ComposeExtensions.Remember{T}(Func{T}, int, string)"/>,
    /// etc. — happens here. To compose a child node from inside a
    /// custom override, build it and call <c>child.Render(composer)</c>
    /// directly; for richer container shapes derive from
    /// <see cref="ComposableContainer"/> and use <c>RenderChildren</c>.
    ///
    /// Must be invoked with the live <see cref="IComposer"/> handed to
    /// the enclosing composition pass — calling it outside a
    /// composition has no useful effect (and will fault when child
    /// composables read snapshot state).
    /// </summary>
    public abstract void Render(IComposer composer);

    /// <summary>
    /// Render this node as the body of a parent layout that supplies a
    /// runtime <c>PaddingValues</c> handle (e.g.
    /// <see cref="Scaffold"/>'s content lambda). The default
    /// implementation stashes <paramref name="contentPadding"/> in a
    /// per-instance slot and delegates to the regular
    /// <see cref="Render(IComposer)"/>; the next call to
    /// <see cref="BuildModifier"/> on this same node consumes the
    /// handle and prepends a <c>Modifier.padding(contentPadding)</c>
    /// op directly via JNI — no per-measure managed
    /// <see cref="AndroidX.Compose.Modifier"/> allocations.
    ///
    /// Container facades whose Kotlin counterpart accepts a
    /// <c>contentPadding</c> parameter directly (<see cref="LazyColumn{T}"/>,
    /// <see cref="LazyRow{T}"/>, the lazy grids) can override this to
    /// forward the handle into the binding instead of materializing a
    /// <c>Modifier.padding(values)</c> chain. See issue #46.
    /// </summary>
    internal virtual void Render(IComposer composer, IntPtr contentPadding)
    {
        var prev = _contentPadding;
        _contentPadding = contentPadding;
        try
        {
            Render(composer);
        }
        finally
        {
            _contentPadding = prev;
        }
    }
}
