using Android.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// Function0&lt;Unit&gt; — zero-arg callbacks (Button.onClick, etc.).
/// A <c>[Register]</c>'d Java.Lang.Object ACW so Compose's bytecode-typed
/// lambda parameter accepts it.
/// </summary>
[Register("net/compose/ComposableLambda0")]
internal sealed class ComposableLambda0 : Java.Lang.Object, IFunction0
{
    readonly Action _body;
    public ComposableLambda0(Action body) => _body = body;

    // Kotlin Function0<Unit> contractually returns Unit.INSTANCE. Returning
    // Java null happens to work today because every Compose call site we
    // touch discards the result, but a future compose-compiler / K2 emit
    // may chain `.let { }` / `.also { }` on the result and add an
    // Intrinsics.checkNotNullExpressionValue. See issue #43.
    public Java.Lang.Object Invoke() { _body(); return Kotlin.Unit.Instance!; }
}
