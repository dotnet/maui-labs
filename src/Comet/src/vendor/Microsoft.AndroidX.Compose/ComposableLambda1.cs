using Android.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// Function1&lt;T, Unit&gt; — single-arg callbacks (TextField's
/// onValueChange, onCheckedChange, etc.). The body receives the raw
/// Java arg and is responsible for unboxing it.
/// </summary>
[Register("net/compose/ComposableLambda1")]
internal sealed class ComposableLambda1 : Java.Lang.Object, IFunction1
{
    readonly Action<Java.Lang.Object?> _body;
    public ComposableLambda1(Action<Java.Lang.Object?> body) => _body = body;

    // Kotlin Function1<T, Unit> contractually returns Unit.INSTANCE. See
    // ComposableLambda0 / issue #43 for the rationale.
    public Java.Lang.Object Invoke(Java.Lang.Object? p0)
    {
        _body(p0);
        return Kotlin.Unit.Instance!;
    }
}
