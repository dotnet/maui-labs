using Android.Runtime;
using Kotlin.Coroutines;
using Kotlin.Coroutines.Intrinsics;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// JCW exposing a <see cref="IFunction2"/> that Compose's
/// <c>pointerInput(key, handler)</c> invokes inside the gesture-detector
/// coroutine, then forwards to <c>detectTransformGestures</c>. Mirrors
/// the shape of <see cref="PointerInputBlock"/> but routes to the
/// pinch / zoom / rotate suspend bridge.
/// </summary>
/// <remarks>
/// See <see cref="DragGestureBlock"/> for the tail-call /
/// callback-freshness rationale; the same constraints apply here.
/// The single <see cref="TransformGestureCallback"/> instance is the
/// only callback (Compose's <c>detectTransformGestures</c> doesn't
/// surface separate start / end events — the runtime applies the
/// per-frame deltas as long as enough pointers are down).
/// </remarks>
[Register("net/compose/TransformGestureBlock")]
internal sealed class TransformGestureBlock : Java.Lang.Object, IFunction2
{
    static IntPtr s_suspendedHandle;

    readonly bool _panZoomLock;
    readonly TransformGestureCallback _onGesture;

    public TransformGestureBlock(bool panZoomLock, TransformGestureCallback onGesture)
    {
        _panZoomLock = panZoomLock;
        _onGesture   = onGesture;
    }

    public Java.Lang.Object? Invoke(Java.Lang.Object? scope, Java.Lang.Object? cont)
    {
        if (scope is null)
            throw new InvalidOperationException(
                "TransformGestureBlock.Invoke received a null PointerInputScope in slot 0");
        if (cont is null)
            throw new InvalidOperationException(
                "TransformGestureBlock.Invoke received a null Continuation in slot 1");

        var scopeHandle = scope.Handle;

        IContinuation continuation;
        try
        {
            continuation = cont.JavaCast<IContinuation>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "TransformGestureBlock.Invoke could not project slot 1 ("
                + (cont.Class?.Name ?? "<unknown>")
                + ") as kotlin.coroutines.Continuation", ex);
        }

        try
        {
            var resultHandle = ComposeBridges.DetectTransformGestures(
                scopeHandle,
                _panZoomLock,
                ((Java.Lang.Object)_onGesture).Handle,
                continuation);

            if (resultHandle == IntPtr.Zero)
                return null;

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
            GC.KeepAlive(_onGesture);
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
