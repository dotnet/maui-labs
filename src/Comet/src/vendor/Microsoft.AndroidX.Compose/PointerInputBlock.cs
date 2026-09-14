using Android.Runtime;
using Kotlin.Coroutines;
using Kotlin.Coroutines.Intrinsics;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// JCW exposing a <see cref="IFunction2"/> that Compose's
/// <c>pointerInput(key, handler)</c> invokes inside the gesture-detector
/// coroutine. The Java-side
/// <c>net.compose.PointerInputEventHandlerImpl</c> (shipped via
/// <c>&lt;AndroidJavaSource&gt;</c>) implements the bound, but
/// otherwise-unreachable, <c>PointerInputEventHandler</c> interface by
/// forwarding to this Function2 — the only shape we can implement from
/// C# given that the binder strips
/// <c>PointerInputEventHandler.invoke</c>'s value-class-mangled
/// signature.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Tail-call semantics.</strong> Inside <see cref="Invoke"/> we
/// call <see cref="ComposeBridges.DetectTapGestures"/> with the OUTER
/// continuation Kotlin handed us. The raw result —
/// <c>COROUTINE_SUSPENDED</c>, <c>kotlin.Unit</c>, or a
/// <c>kotlin.Result.Failure</c> — is returned verbatim. There is no
/// <c>SuspendBridge</c> involvement and no managed <c>Task</c>: when
/// Compose cancels the outer coroutine (e.g. modifier removed, key
/// changed), <c>detectTapGestures</c>' next suspend point throws
/// <c>CancellationException</c>, which propagates through the
/// continuation we forwarded — no extra plumbing required.
/// </para>
/// <para>
/// <strong>Callback freshness.</strong> The <c>onTap</c> / <c>onPress</c>
/// / <c>onLongPress</c> / <c>onDoubleTap</c> JCW instances are
/// captured at construction time; updating the C# delegates and
/// rebuilding the modifier will NOT restart this coroutine unless the
/// caller also varies the <c>key</c> parameter (Compose's
/// <c>pointerInput</c> only resets on key change, not on handler
/// instance change). Mirror Kotlin's idiom: pass a varying key when
/// callbacks need to refresh, or read mutable state via
/// <c>State&lt;T&gt;</c> from inside the callbacks.
/// </para>
/// </remarks>
[Register("net/compose/PointerInputBlock")]
internal sealed class PointerInputBlock : Java.Lang.Object, IFunction2
{
    static IntPtr s_suspendedHandle;

    readonly OffsetCallback? _onTap;
    readonly OffsetPressCallback? _onPress;
    readonly OffsetCallback? _onLongPress;
    readonly OffsetCallback? _onDoubleTap;

    public PointerInputBlock(
        OffsetCallback? onTap,
        OffsetPressCallback? onPress,
        OffsetCallback? onLongPress,
        OffsetCallback? onDoubleTap)
    {
        _onTap = onTap;
        _onPress = onPress;
        _onLongPress = onLongPress;
        _onDoubleTap = onDoubleTap;
    }

    public Java.Lang.Object? Invoke(Java.Lang.Object? scope, Java.Lang.Object? cont)
    {
        if (scope is null)
            throw new InvalidOperationException(
                "PointerInputBlock.Invoke received a null PointerInputScope in slot 0");
        if (cont is null)
            throw new InvalidOperationException(
                "PointerInputBlock.Invoke received a null Continuation in slot 1");

        var scopeHandle = scope.Handle;

        // Kotlin's createCoroutineUnintercepted hands us a synthesized
        // outer continuation whose runtime class isn't in Mono.Android's
        // peer registry, so a plain `as IContinuation` returns null.
        // JavaCast<T> synthesizes the interface peer from the raw handle.
        // Same trick as LaunchedEffectBody.Invoke.
        IContinuation continuation;
        try
        {
            continuation = cont.JavaCast<IContinuation>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "PointerInputBlock.Invoke could not project slot 1 ("
                + (cont.Class?.Name ?? "<unknown>")
                + ") as kotlin.coroutines.Continuation", ex);
        }

        try
        {
            var resultHandle = ComposeBridges.DetectTapGestures(
                scopeHandle,
                _onDoubleTap is null ? null : ((Java.Lang.Object)_onDoubleTap).Handle,
                _onLongPress is null ? null : ((Java.Lang.Object)_onLongPress).Handle,
                _onPress is null ? null : ((Java.Lang.Object)_onPress).Handle,
                _onTap is null ? null : ((Java.Lang.Object)_onTap).Handle,
                continuation);

            if (resultHandle == IntPtr.Zero)
                return null;

            // The COROUTINE_SUSPENDED sentinel is a Kotlin singleton.
            // Promoting it via Java.Lang.Object.GetObject collides with
            // Mono's peer cache (the cached managed wrapper holds a
            // GLOBAL ref while we'd hand it a LOCAL one). Detect it and
            // return a stable cached wrapper instead. Same trick as
            // SuspendBridge.IsCoroutineSuspended.
            EnsureSuspendedHandle();
            if (JNIEnv.IsSameObject(resultHandle, s_suspendedHandle))
            {
                JNIEnv.DeleteLocalRef(resultHandle);
                return IntrinsicsKt.COROUTINE_SUSPENDED;
            }

            return Java.Lang.Object.GetObject<Java.Lang.Object>(
                resultHandle, JniHandleOwnership.TransferLocalRef);
        }
        finally
        {
            GC.KeepAlive(scope);
            GC.KeepAlive(cont);
            GC.KeepAlive(_onTap);
            GC.KeepAlive(_onPress);
            GC.KeepAlive(_onLongPress);
            GC.KeepAlive(_onDoubleTap);
        }
    }

    static void EnsureSuspendedHandle()
    {
        if (s_suspendedHandle != IntPtr.Zero) return;
        var inst = IntrinsicsKt.COROUTINE_SUSPENDED;
        var gref = JNIEnv.NewGlobalRef(inst.Handle);
        if (Interlocked.CompareExchange(
                ref s_suspendedHandle, gref, IntPtr.Zero) != IntPtr.Zero)
            JNIEnv.DeleteGlobalRef(gref);
        GC.KeepAlive(inst);
    }
}
