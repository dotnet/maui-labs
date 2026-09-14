using Android.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// JCW adapter implementing <c>Function1&lt;Integer, Integer&gt;</c>, used
/// by the <c>slideIn*</c> / <c>slideOut*</c> transition factories whose
/// offset parameter is a Kotlin <c>(Int) -&gt; Int</c> lambda mapping the
/// container's measured width/height to the slide's initial/target
/// offset in pixels. Boxed via <c>java.lang.Integer</c> at the JNI
/// boundary.
/// </summary>
[Register("net/compose/IntCallback")]
internal sealed class IntCallback : Java.Lang.Object, IFunction1
{
    readonly Func<int, int> _body;

    public IntCallback(Func<int, int> body) => _body = body;

    public Java.Lang.Object Invoke(Java.Lang.Object? p0)
    {
        int size = p0 is Java.Lang.Integer i ? i.IntValue() : 0;
        return Java.Lang.Integer.ValueOf(_body(size))!;
    }
}
