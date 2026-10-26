using Android.Runtime;
using AndroidX.Compose.Foundation.Layout;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>BoxWithConstraints</c> — a <see cref="Box"/> variant
/// that hands its content the layout constraints (max/min width and
/// height in dp) it has been given by its parent. Use this when the
/// child layout depends on the available space, e.g. switching between
/// a single-column phone layout and a two-pane tablet layout.
///
/// <code>
/// new BoxWithConstraints(c =&gt;
///     c.MaxWidth &lt; 600
///         ? new Column { /* compact */ }
///         : new Row    { /* wide   */ })
/// </code>
///
/// The callback runs every composition pass, so it must be cheap; if
/// the produced node depends on more than the constraints (counters,
/// remembered state) read those values from inside the callback rather
/// than capturing them in the outer scope.
/// </summary>
public sealed class BoxWithConstraints : ComposableNode
{
    readonly Func<BoxConstraints, ComposableNode> _content;

    /// <summary>
    /// Build a <see cref="BoxWithConstraints"/> that lays out the
    /// composable returned from <paramref name="content"/>. The callback
    /// receives the current layout's <see cref="BoxConstraints"/>.
    /// </summary>
    public BoxWithConstraints(Func<BoxConstraints, ComposableNode> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
    }

    public override void Render(IComposer composer)
    {
        var modifier = BuildModifier();
        var lambda   = ComposableLambdas.Wrap3(composer, (scopeHandle, comp) =>
        {
            // Scope is delivered as a raw JNI handle; wrap with
            // DoNotTransfer so the local ref is freed by Compose, then
            // cast to the typed binding interface.
            var scope = Java.Lang.Object.GetObject<IBoxWithConstraintsScope>(
                scopeHandle, JniHandleOwnership.DoNotTransfer)!;
            var constraints = new BoxConstraints(
                minWidth:  scope.MinWidth,
                maxWidth:  scope.MaxWidth,
                minHeight: scope.MinHeight,
                maxHeight: scope.MaxHeight);
            _content(constraints).Render(comp);
        });

        int defaults = (int)BoxWithConstraintsDefault.All;
        if (modifier is not null) defaults &= ~(int)BoxWithConstraintsDefault.Modifier;

        BoxWithConstraintsKt.BoxWithConstraints(
            modifier:                modifier,
            contentAlignment:        null,
            propagateMinConstraints: false,
            content:                 lambda,
            _composer:               composer,
            p5:                      0,
            _changed:                defaults);
    }
}
