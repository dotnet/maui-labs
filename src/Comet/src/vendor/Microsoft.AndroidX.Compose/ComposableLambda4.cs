using Android.Runtime;
using AndroidX.Compose.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// Function4&lt;Scope, T, Composer, Integer, Unit&gt; — the shape Compose's
/// <c>LazyListScope.items</c> and <c>LazyGridScope.items</c> use for
/// their per-item content lambda. <c>p0</c> is the lazy item / grid item
/// scope (raw <see cref="IntPtr"/>), <c>p1</c> is the boxed
/// per-item value (an <see cref="Java.Lang.Integer"/> index for the
/// count-based overload, or the boxed user item for the list-based one),
/// <c>p2</c> is the composer, <c>p3</c> is <c>$changed</c>.
/// </summary>
[Register("net/compose/ComposableLambda4")]
internal sealed class ComposableLambda4 : Java.Lang.Object, IFunction4
{
    readonly Action<IntPtr, Java.Lang.Object?, IComposer> _body;

    public ComposableLambda4(Action<IntPtr, Java.Lang.Object?, IComposer> body) => _body = body;

    public Java.Lang.Object? Invoke(Java.Lang.Object? p0, Java.Lang.Object? p1, Java.Lang.Object? p2, Java.Lang.Object? p3)
    {
        ArgumentNullException.ThrowIfNull(p2);
        var composer = Android.Runtime.Extensions.JavaCast<IComposer>(p2);
        _body(p0?.Handle ?? IntPtr.Zero, p1, composer);
        return Kotlin.Unit.Instance!;
    }
}
