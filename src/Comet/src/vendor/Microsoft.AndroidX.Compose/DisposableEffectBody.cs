using Android.Runtime;
using AndroidX.Compose.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// JCW for Compose's <c>DisposableEffect</c> body —
/// <c>Function1&lt;DisposableEffectScope, DisposableEffectResult&gt;</c>.
/// Kotlin invokes the body once per (key change | enter composition)
/// and stores the returned <see cref="IDisposableEffectResult"/>; when
/// keys change or the call site leaves the composition Compose calls
/// <see cref="IDisposableEffectResult.Dispose"/> on the stored result.
/// </summary>
/// <remarks>
/// The C# body returns a <see cref="Action"/> "onDispose"
/// callback. We wrap that callback in a fresh
/// <see cref="ComposableLambda0"/> and hand it to
/// <see cref="DisposableEffectScope.OnDispose(IFunction0)"/>, which
/// is the only way to construct an <see cref="IDisposableEffectResult"/>
/// without subclassing the interface in Java.
/// </remarks>
[Register("net/compose/DisposableEffectBody")]
internal sealed class DisposableEffectBody : Java.Lang.Object, IFunction1
{
    readonly Func<DisposableEffectScope, Action> _body;

    public DisposableEffectBody(Func<DisposableEffectScope, Action> body)
    {
        _body = body;
    }

    public Java.Lang.Object Invoke(Java.Lang.Object? p0)
    {
        // Compose always passes a non-null DisposableEffectScope (the
        // singleton `InternalDisposableEffectScope`). Use JavaCast<T>
        // rather than `p0 as DisposableEffectScope` because Mono.Android's
        // peer cache doesn't know Kotlin's `internal object` subclasses
        // implement the bound interface — a plain `as` returns null even
        // when the underlying Java object does implement the interface.
        if (p0 is null)
            throw new InvalidOperationException(
                "DisposableEffect body received a null DisposableEffectScope");

        DisposableEffectScope scope;
        try
        {
            scope = p0.JavaCast<DisposableEffectScope>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "DisposableEffect body could not project arg ("
                + (p0.Class?.Name ?? "<unknown>")
                + ") as DisposableEffectScope", ex);
        }

        var onDispose = _body(scope)
            ?? throw new InvalidOperationException(
                "DisposableEffect body returned a null onDispose callback. "
                + "Return `() => { }` if there's nothing to clean up.");
        return (Java.Lang.Object)scope.OnDispose(new ComposableLambda0(onDispose));
    }
}
