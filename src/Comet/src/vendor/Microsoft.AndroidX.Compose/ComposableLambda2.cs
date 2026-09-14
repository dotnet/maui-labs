using Android.Runtime;
using AndroidX.Compose.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// Function2&lt;Composer, Integer, Unit&gt; — top-level composition +
/// theme/scope content. <c>p0</c> is the composer, <c>p1</c> is
/// <c>$changed</c>.
/// </summary>
[Register("net/compose/ComposableLambda2")]
internal sealed class ComposableLambda2 : Java.Lang.Object, IFunction2
{
    readonly Action<IComposer> _body;
    public ComposableLambda2(Action<IComposer> body) => _body = body;

    // Kotlin Function2<Composer, Int, Unit> contractually returns
    // Unit.INSTANCE. See ComposableLambda0 / issue #43 for the rationale.
    public Java.Lang.Object Invoke(Java.Lang.Object? p0, Java.Lang.Object? p1)
    {
        ArgumentNullException.ThrowIfNull(p0);
        var composer = Android.Runtime.Extensions.JavaCast<IComposer>(p0);
        _body(composer);
        return Kotlin.Unit.Instance!;
    }
}
