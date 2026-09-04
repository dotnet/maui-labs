using Android.Runtime;
using AndroidX.Compose.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// Function3&lt;Scope, Composer, Integer, Unit&gt; — Column/Row/Box/Button
/// content. <c>p0</c> is the receiver scope (RowScope/ColumnScope),
/// <c>p1</c> is the composer, <c>p2</c> is <c>$changed</c>.
///
/// Three ctors:
/// <list type="bullet">
///   <item><description><c>Action&lt;IComposer&gt;</c> discards the scope —
///     used wherever children don't need to know it (Column, Box,
///     Button).</description></item>
///   <item><description><c>Action&lt;IntPtr, IComposer&gt;</c> receives the
///     raw scope handle, used by container composables whose children
///     are extension-receiver composables (<c>RowScope.NavigationBarItem</c>).
///     The scope is published via <see cref="RenderContext"/> so the
///     child <c>Render</c> can read it.</description></item>
///   <item><description><c>Action&lt;Java.Lang.Object?, IComposer&gt;</c>
///     receives the boxed <c>p0</c> as a <see cref="Java.Lang.Object"/>.
///     Used by value-typed Function3 slots — e.g.
///     <c>Crossfade</c>'s <c>content: @Composable (T) -&gt; Unit</c>
///     where <c>p0</c> is the boxed targetState rather than a scope
///     receiver — so the body can unbox to the user-facing
///     <c>T</c>.</description></item>
/// </list>
/// </summary>
[Register("net/compose/ComposableLambda3")]
internal sealed class ComposableLambda3 : Java.Lang.Object, IFunction3
{
    readonly Action<Java.Lang.Object?, IComposer> _body;

    public ComposableLambda3(Action<IComposer> body)
        : this((Java.Lang.Object? _, IComposer c) => body(c)) { }

    public ComposableLambda3(Action<IntPtr, IComposer> body)
        : this((Java.Lang.Object? p0, IComposer c) => body(p0?.Handle ?? IntPtr.Zero, c)) { }

    public ComposableLambda3(Action<Java.Lang.Object?, IComposer> body) => _body = body;

    // Kotlin Function3<Scope, Composer, Int, Unit> contractually returns
    // Unit.INSTANCE. See ComposableLambda0 / issue #43 for the rationale.
    public Java.Lang.Object Invoke(Java.Lang.Object? p0, Java.Lang.Object? p1, Java.Lang.Object? p2)
    {
        ArgumentNullException.ThrowIfNull(p1);
        var composer = Android.Runtime.Extensions.JavaCast<IComposer>(p1);
        _body(p0, composer);
        return Kotlin.Unit.Instance!;
    }
}
