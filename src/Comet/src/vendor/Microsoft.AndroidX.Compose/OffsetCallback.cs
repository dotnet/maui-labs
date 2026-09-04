using Android.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// JCW adapter implementing <c>Function1&lt;Offset, Unit&gt;</c> for
/// gesture-detector callbacks like <c>onTap</c> / <c>onLongPress</c> /
/// <c>onDoubleTap</c>. Because Kotlin's <c>Offset</c> is a
/// <c>@JvmInline value class</c>, the boxed argument <c>p0</c> arrives
/// as an <c>androidx.compose.ui.geometry.Offset</c> wrapper, not a
/// <c>java.lang.Long</c>. We unbox via the wrapper's
/// <c>unbox-impl()J</c> instance method.
/// </summary>
[Register("net/compose/OffsetCallback")]
internal sealed class OffsetCallback : Java.Lang.Object, IFunction1
{
    static IntPtr s_unbox;

    readonly Action<Offset> _body;

    public OffsetCallback(Action<Offset> body) => _body = body;

    public Java.Lang.Object Invoke(Java.Lang.Object? p0)
    {
        long packed = 0L;
        if (p0 is not null)
        {
            EnsureUnboxMethod();
            packed = JNIEnv.CallLongMethod(p0.Handle, s_unbox);
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
