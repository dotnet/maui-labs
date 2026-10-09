using Android.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// JCW adapter implementing <c>Function3&lt;PressGestureScope, Offset,
/// Continuation, Object&gt;</c> for the <c>onPress</c> callback of
/// <c>detectTapGestures</c>. Kotlin lowers the original
/// <c>suspend PressGestureScope.(Offset) -&gt; Unit</c> shape to a
/// Function3 with the receiver as <c>p0</c>, the offset as <c>p1</c>,
/// and the suspend continuation as <c>p2</c>.
/// </summary>
/// <remarks>
/// <para>
/// v1 invokes the user's <see cref="Action{Offset}"/> body
/// synchronously and immediately returns <c>Kotlin.Unit.Instance</c>
/// (i.e. the suspend body completes synchronously without ever
/// suspending). The <c>PressGestureScope</c> receiver (which Kotlin
/// uses to wait for release / cancel via <c>awaitRelease</c> /
/// <c>tryAwaitRelease</c>) is intentionally NOT surfaced — that
/// requires bridging suspend-from-suspend which we don't have plumbing
/// for yet, and the resulting API would be misleadingly async.
/// </para>
/// <para>
/// The boxed <c>Offset</c> argument (<c>p1</c>) is unboxed via the
/// shared <see cref="OffsetCallback"/> helper's
/// <c>unbox-impl()J</c> lookup pattern.
/// </para>
/// </remarks>
[Register("net/compose/OffsetPressCallback")]
internal sealed class OffsetPressCallback : Java.Lang.Object, IFunction3
{
    static IntPtr s_unbox;

    readonly Action<Offset> _body;

    public OffsetPressCallback(Action<Offset> body) => _body = body;

    public Java.Lang.Object Invoke(Java.Lang.Object? p0, Java.Lang.Object? p1, Java.Lang.Object? p2)
    {
        long packed = 0L;
        if (p1 is not null)
        {
            EnsureUnboxMethod();
            packed = JNIEnv.CallLongMethod(p1.Handle, s_unbox);
        }
        _body(Offset.FromPacked(packed));
        return Kotlin.Unit.Instance!;
    }

    static void EnsureUnboxMethod()
    {
        if (s_unbox != IntPtr.Zero) return;
        var cls = JNIEnv.FindClass("androidx/compose/ui/geometry/Offset");
        s_unbox = JNIEnv.GetMethodID(cls, "unbox-impl", "()J");
    }
}
