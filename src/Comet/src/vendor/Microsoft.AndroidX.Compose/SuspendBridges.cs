using Android.Runtime;
using Kotlin.Coroutines;

namespace AndroidX.Compose;

// JNI bridges for Kotlin `suspend` functions. Bodies are emitted by
// ComposeBridgeGenerator from `[ComposeBridge(Suspend = true)]`. The
// trailing `IContinuation cont` parameter is detected as a Kotlin
// continuation slot; the generator takes care of method-id caching,
// the JValue array, the receiver/extension-receiver, $default mask
// auto-build, the synthetic marker (for $default extensions), and
// `try`/`finally { GC.KeepAlive(cont) }`.
//
// Suspend bridges always return raw `IntPtr` — wrapping the
// COROUTINE_SUSPENDED sentinel via `Java.Lang.Object.GetObject(..,
// TransferLocalRef)` collides with Mono's peer cache (the cached
// managed wrapper holds a GLOBAL ref while we'd hand it a LOCAL one)
// and aborts under CheckJNI. The generator enforces this (CN2009).
//
// `AndroidUiDispatcherMain` below stays hand-written because it's a
// static-field getter chasing a nested Companion type, not a method
// call — it doesn't fit any `[ComposeBridge]` shape.
internal static partial class ComposeBridges
{
    // androidx.compose.foundation.ScrollState.scrollTo(int, Continuation): Object
    //
    // The two-arg suspend overload IS exposed in the binding (returns
    // Java.Lang.Object), but we call it via raw JNI here so the
    // returned handle stays as a plain IntPtr — SuspendBridge needs
    // raw-handle semantics to avoid the COROUTINE_SUSPENDED peer-cache
    // pitfall.
    [ComposeBridge(Suspend = true,
        Class = "androidx/compose/foundation/ScrollState",
        JvmName = "scrollTo",
        Signature = "(ILkotlin/coroutines/Continuation;)Ljava/lang/Object;")]
    internal static partial IntPtr ScrollStateScrollTo(
        IntPtr state, int value, IContinuation cont);

    // androidx.compose.foundation.ScrollState.animateScrollTo$default(
    //     ScrollState this, int value, AnimationSpec spec,
    //     Continuation cont, int $default, Object marker): Object
    //
    // Why raw JNI: animateScrollTo lives in the Kotlin extension class
    // `androidx/compose/foundation/gestures/AnimateScrollExtensionsKt`,
    // which is stripped wholesale from `Xamarin.AndroidX.Compose.Foundation.Android.dll`
    // — the `AnimationSpec<Float>` parameter triggers Kotlin's generic
    // + `@JvmInline` mangling (`animateScrollTo-XXXXXXXX`) that the
    // generator-bindings parser drops. Same root cause as the mangled
    // Compose Material3 overloads tracked by dotnet/java-interop#1440.
    //
    // The single nullable `animationSpec` slot is left to the auto-mask
    // (passing null → $default bit set → Kotlin substitutes SpringSpec()).
    [ComposeBridge(Suspend = true,
        Class = "androidx/compose/foundation/ScrollState",
        JvmName = "animateScrollTo$default",
        Signature = "(Landroidx/compose/foundation/ScrollState;ILandroidx/compose/animation/core/AnimationSpec;Lkotlin/coroutines/Continuation;ILjava/lang/Object;)Ljava/lang/Object;",
        Defaults = typeof(ScrollStateAnimateScrollToDefault))]
    internal static partial IntPtr ScrollStateAnimateScrollTo(
        IntPtr state, int value, IntPtr? animationSpec, IContinuation cont);

    // androidx.compose.material3.DrawerState.open(Continuation): Object
    [ComposeBridge(Suspend = true,
        Class = "androidx/compose/material3/DrawerState",
        JvmName = "open",
        Signature = "(Lkotlin/coroutines/Continuation;)Ljava/lang/Object;")]
    internal static partial IntPtr DrawerStateOpen(IntPtr state, IContinuation cont);

    // androidx.compose.material3.DrawerState.close(Continuation): Object
    [ComposeBridge(Suspend = true,
        Class = "androidx/compose/material3/DrawerState",
        JvmName = "close",
        Signature = "(Lkotlin/coroutines/Continuation;)Ljava/lang/Object;")]
    internal static partial IntPtr DrawerStateClose(IntPtr state, IContinuation cont);

    // androidx.compose.material3.SheetState.show(Continuation): Object
    [ComposeBridge(Suspend = true,
        Class = "androidx/compose/material3/SheetState",
        JvmName = "show",
        Signature = "(Lkotlin/coroutines/Continuation;)Ljava/lang/Object;")]
    internal static partial IntPtr SheetStateShow(IntPtr state, IContinuation cont);

    // androidx.compose.material3.SheetState.hide(Continuation): Object
    [ComposeBridge(Suspend = true,
        Class = "androidx/compose/material3/SheetState",
        JvmName = "hide",
        Signature = "(Lkotlin/coroutines/Continuation;)Ljava/lang/Object;")]
    internal static partial IntPtr SheetStateHide(IntPtr state, IContinuation cont);

    // androidx.compose.material3.SheetState.expand(Continuation): Object
    [ComposeBridge(Suspend = true,
        Class = "androidx/compose/material3/SheetState",
        JvmName = "expand",
        Signature = "(Lkotlin/coroutines/Continuation;)Ljava/lang/Object;")]
    internal static partial IntPtr SheetStateExpand(IntPtr state, IContinuation cont);

    // androidx.compose.material3.SheetState.partialExpand(Continuation): Object
    [ComposeBridge(Suspend = true,
        Class = "androidx/compose/material3/SheetState",
        JvmName = "partialExpand",
        Signature = "(Lkotlin/coroutines/Continuation;)Ljava/lang/Object;")]
    internal static partial IntPtr SheetStatePartialExpand(IntPtr state, IContinuation cont);

    // androidx.compose.foundation.lazy.LazyListState
    //     .scrollToItem(int index, int scrollOffset, Continuation): Object
    [ComposeBridge(Suspend = true,
        Class = "androidx/compose/foundation/lazy/LazyListState",
        JvmName = "scrollToItem",
        Signature = "(IILkotlin/coroutines/Continuation;)Ljava/lang/Object;")]
    internal static partial IntPtr LazyListStateScrollToItem(
        IntPtr state, int index, int scrollOffset, IContinuation cont);

    // androidx.compose.foundation.lazy.LazyListState
    //     .animateScrollToItem(int index, int scrollOffset, Continuation): Object
    //
    // Requires a MonotonicFrameClock in the continuation context
    // (calls `withFrameNanos` internally) — supplied by
    // SuspendContinuation.Context which returns AndroidUiDispatcher.Main.
    [ComposeBridge(Suspend = true,
        Class = "androidx/compose/foundation/lazy/LazyListState",
        JvmName = "animateScrollToItem",
        Signature = "(IILkotlin/coroutines/Continuation;)Ljava/lang/Object;")]
    internal static partial IntPtr LazyListStateAnimateScrollToItem(
        IntPtr state, int index, int scrollOffset, IContinuation cont);

    // androidx.compose.foundation.gestures.TapGestureDetectorKt
    //     .detectTapGestures$default(
    //         PointerInputScope scope,
    //         ((Offset) -> Unit)? onDoubleTap = null,
    //         ((Offset) -> Unit)? onLongPress = null,
    //         (suspend PressGestureScope.(Offset) -> Unit) onPress = NoPressGesture,
    //         ((Offset) -> Unit)? onTap = null,
    //         Continuation cont, int $default, Object marker): Object
    //
    // Unlike the other bridges in this file, the caller (PointerInputBlock)
    // is itself inside a Kotlin suspend body and supplies the OUTER
    // continuation directly — this is a *tail call* into Kotlin. No
    // SuspendBridge / SuspendContinuation involvement; the raw IntPtr
    // we return (COROUTINE_SUSPENDED, kotlin.Unit, or kotlin.Result$Failure)
    // is forwarded unchanged.
    //
    // Each callback handle is `IntPtr?` so the auto-default-mask flips the
    // corresponding bit when null — Kotlin substitutes its real default
    // (null for the nullable slots, NoPressGesture for onPress).
    [ComposeBridge(Suspend = true,
        Class = "androidx/compose/foundation/gestures/TapGestureDetectorKt",
        JvmName = "detectTapGestures$default",
        Signature = "(Landroidx/compose/ui/input/pointer/PointerInputScope;Lkotlin/jvm/functions/Function1;Lkotlin/jvm/functions/Function1;Lkotlin/jvm/functions/Function3;Lkotlin/jvm/functions/Function1;Lkotlin/coroutines/Continuation;ILjava/lang/Object;)Ljava/lang/Object;",
        Defaults = typeof(DetectTapGesturesDefault))]
    internal static partial IntPtr DetectTapGestures(
        IntPtr scope,
        IntPtr? onDoubleTap,
        IntPtr? onLongPress,
        IntPtr? onPress,
        IntPtr? onTap,
        IContinuation cont);

    // androidx.compose.foundation.gestures.DragGestureDetectorKt
    //     .detectDragGestures$default(
    //         PointerInputScope scope,
    //         ((Offset) -> Unit) onDragStart = {},
    //         (() -> Unit)      onDragEnd   = {},
    //         (() -> Unit)      onDragCancel = {},
    //         ((PointerInputChange, Offset) -> Unit) onDrag,
    //         Continuation cont, int $default, Object marker): Object
    //
    // Same tail-call shape as detectTapGestures — DragGestureBlock is
    // the outer Function2 invoked from `pointerInput`, and forwards
    // its OUTER continuation here. Three of the four callbacks are
    // defaultable; auto-default-mask leaves a bit set when null is
    // passed (Kotlin substitutes its empty-lambda default). onDrag
    // is required and not in the default mask.
    [ComposeBridge(Suspend = true,
        Class = "androidx/compose/foundation/gestures/DragGestureDetectorKt",
        JvmName = "detectDragGestures$default",
        Signature = "(Landroidx/compose/ui/input/pointer/PointerInputScope;Lkotlin/jvm/functions/Function1;Lkotlin/jvm/functions/Function0;Lkotlin/jvm/functions/Function0;Lkotlin/jvm/functions/Function2;Lkotlin/coroutines/Continuation;ILjava/lang/Object;)Ljava/lang/Object;",
        Defaults = typeof(DetectDragGesturesDefault))]
    internal static partial IntPtr DetectDragGestures(
        IntPtr scope,
        IntPtr? onDragStart,
        IntPtr? onDragEnd,
        IntPtr? onDragCancel,
        IntPtr  onDrag,
        IContinuation cont);

    // androidx.compose.foundation.gestures.TransformGestureDetectorKt
    //     .detectTransformGestures$default(
    //         PointerInputScope scope,
    //         boolean panZoomLock = false,
    //         ((Offset, Offset, Float, Float) -> Unit) onGesture,
    //         Continuation cont, int $default, Object marker): Object
    //
    // panZoomLock is the only defaultable param. onGesture is required.
    [ComposeBridge(Suspend = true,
        Class = "androidx/compose/foundation/gestures/TransformGestureDetectorKt",
        JvmName = "detectTransformGestures$default",
        Signature = "(Landroidx/compose/ui/input/pointer/PointerInputScope;ZLkotlin/jvm/functions/Function4;Lkotlin/coroutines/Continuation;ILjava/lang/Object;)Ljava/lang/Object;",
        Defaults = typeof(DetectTransformGesturesDefault))]
    internal static partial IntPtr DetectTransformGestures(
        IntPtr scope,
        bool   panZoomLock,
        IntPtr onGesture,
        IContinuation cont);

    static IntPtr s_androidUiDispatcherMain_handle;

    // androidx.compose.ui.platform.AndroidUiDispatcher.Companion.getMain(): kotlin.coroutines.CoroutineContext
    //
    // Returns the singleton CoroutineContext that combines a main-thread
    // dispatcher with a MonotonicFrameClock backed by Choreographer. Required
    // for any Compose suspend function that calls `withFrameNanos` —
    // `animateScrollTo`, `animateTo`, etc. — because those look up
    // `coroutineContext[MonotonicFrameClock]` and throw without it.
    //
    // The C# binding exposes AndroidUiDispatcher.Companion as a nested type
    // with an internal constructor and no static accessor on the parent —
    // so we resolve Main via raw JNI and cache the result as a global ref.
    // Must be initialised on a Looper thread the first time, which is
    // naturally satisfied because Compose suspend calls originate from
    // button onClicks running on the main thread.
    //
    // Stays hand-written: this is a static-field getter walking a nested
    // Companion type, not a method call — doesn't fit any
    // `[ComposeBridge]` shape.
    internal static IntPtr AndroidUiDispatcherMain()
    {
        if (s_androidUiDispatcherMain_handle != IntPtr.Zero)
            return s_androidUiDispatcherMain_handle;

        var dispatcherClass = JNIEnv.FindClass("androidx/compose/ui/platform/AndroidUiDispatcher");
        var companionFid = JNIEnv.GetStaticFieldID(
            dispatcherClass, "Companion",
            "Landroidx/compose/ui/platform/AndroidUiDispatcher$Companion;");
        var companionLocal = JNIEnv.GetStaticObjectField(dispatcherClass, companionFid);
        try
        {
            var companionClass = JNIEnv.FindClass("androidx/compose/ui/platform/AndroidUiDispatcher$Companion");
            var getMainMid = JNIEnv.GetMethodID(
                companionClass, "getMain", "()Lkotlin/coroutines/CoroutineContext;");
            var ctxLocal = JNIEnv.CallObjectMethod(companionLocal, getMainMid);
            try
            {
                s_androidUiDispatcherMain_handle = JNIEnv.NewGlobalRef(ctxLocal);
            }
            finally
            {
                JNIEnv.DeleteLocalRef(ctxLocal);
            }
        }
        finally
        {
            JNIEnv.DeleteLocalRef(companionLocal);
        }

        return s_androidUiDispatcherMain_handle;
    }
}
