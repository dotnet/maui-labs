using Android.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// JCW adapter implementing <c>Function0&lt;Unit&gt;</c> for gesture
/// callbacks that take no arguments — Compose's
/// <c>onDragEnd</c> / <c>onDragCancel</c> on
/// <c>detectDragGestures</c>.
/// </summary>
/// <remarks>
/// The wrapped <see cref="Action"/> is invoked synchronously on the
/// Compose UI thread; the return value is the canonical
/// <see cref="Kotlin.Unit.Instance"/> singleton so Kotlin sees a
/// well-formed <c>Unit</c> result. The body is captured at
/// construction time — see <see cref="OffsetCallback"/> for the same
/// callback-freshness caveat (the wrapping <c>pointerInput</c>
/// modifier only restarts on key change, not on handler instance
/// change).
/// </remarks>
[Register("net/compose/UnitCallback")]
internal sealed class UnitCallback : Java.Lang.Object, IFunction0
{
    readonly Action _body;

    public UnitCallback(Action body) => _body = body;

    public Java.Lang.Object Invoke()
    {
        _body();
        return Kotlin.Unit.Instance!;
    }
}
