using Android.Runtime;
using Kotlin.Coroutines;
using Kotlin.Coroutines.Intrinsics;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// JCW exposing a <see cref="IFunction2"/> that Compose's
/// <c>pointerInput(key, handler)</c> invokes inside the gesture-detector
/// coroutine, then forwards to <c>detectDragGestures</c>. Mirrors the
/// shape of <see cref="PointerInputBlock"/> but routes to the
/// drag-gesture suspend bridge.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Tail-call semantics.</strong> Inside <see cref="Invoke"/> we
/// call <see cref="ComposeBridges.DetectDragGestures"/> with the OUTER
/// continuation Kotlin handed us. The raw result —
/// <c>COROUTINE_SUSPENDED</c>, <c>kotlin.Unit</c>, or a
/// <c>kotlin.Result.Failure</c> — is returned verbatim. Kotlin
/// cancels the outer coroutine when the modifier is removed or the
/// pointer-input key changes; <c>detectDragGestures</c>' next suspend
/// point throws <c>CancellationException</c>, which propagates
/// through the continuation we forwarded.
/// </para>
/// <para>
/// <strong>Callback freshness.</strong> The four
/// <c>onDragStart</c> / <c>onDragEnd</c> / <c>onDragCancel</c> /
/// <c>onDrag</c> JCW instances are captured at construction time;
/// updating the C# delegates and rebuilding the modifier won't
/// restart this coroutine unless the caller varies the wrapping
/// <c>pointerInput</c> key. See <see cref="PointerInputBlock"/> for
/// the same caveat with <c>detectTapGestures</c>.
/// </para>
/// </remarks>
[Register("net/compose/DragGestureBlock")]
internal sealed class DragGestureBlock : Java.Lang.Object, IFunction2
{
    static IntPtr s_suspendedHandle;

    readonly OffsetCallback? _onDragStart;
    readonly UnitCallback? _onDragEnd;
    readonly UnitCallback? _onDragCancel;
    readonly DragCallback _onDrag;
    // When true, forward to detectDragGesturesAfterLongPress (drag is only
    // recognised after the long-press timeout) instead of detectDragGestures.
    readonly bool _afterLongPress;

    public DragGestureBlock(
        OffsetCallback? onDragStart,
        UnitCallback? onDragEnd,
        UnitCallback? onDragCancel,
        DragCallback onDrag,
        bool afterLongPress = false)
    {
        _onDragStart    = onDragStart;
        _onDragEnd      = onDragEnd;
        _onDragCancel   = onDragCancel;
        _onDrag         = onDrag;
        _afterLongPress = afterLongPress;
    }

    public Java.Lang.Object? Invoke(Java.Lang.Object? scope, Java.Lang.Object? cont)
    {
        if (scope is null)
            throw new InvalidOperationException(
                "DragGestureBlock.Invoke received a null PointerInputScope in slot 0");
        if (cont is null)
            throw new InvalidOperationException(
                "DragGestureBlock.Invoke received a null Continuation in slot 1");

        var scopeHandle = scope.Handle;

        IContinuation continuation;
        try
        {
            continuation = cont.JavaCast<IContinuation>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "DragGestureBlock.Invoke could not project slot 1 ("
                + (cont.Class?.Name ?? "<unknown>")
                + ") as kotlin.coroutines.Continuation", ex);
        }

        try
        {
            var startHandle  = _onDragStart  is null ? (IntPtr?)null : ((Java.Lang.Object)_onDragStart).Handle;
            var endHandle    = _onDragEnd    is null ? (IntPtr?)null : ((Java.Lang.Object)_onDragEnd).Handle;
            var cancelHandle = _onDragCancel is null ? (IntPtr?)null : ((Java.Lang.Object)_onDragCancel).Handle;
            var dragHandle   = ((Java.Lang.Object)_onDrag).Handle;

            var resultHandle = _afterLongPress
                ? ComposeBridges.DetectDragGesturesAfterLongPress(
                    scopeHandle, startHandle, endHandle, cancelHandle, dragHandle, continuation)
                : ComposeBridges.DetectDragGestures(
                    scopeHandle, startHandle, endHandle, cancelHandle, dragHandle, continuation);

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
            GC.KeepAlive(_onDragStart);
            GC.KeepAlive(_onDragEnd);
            GC.KeepAlive(_onDragCancel);
            GC.KeepAlive(_onDrag);
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
