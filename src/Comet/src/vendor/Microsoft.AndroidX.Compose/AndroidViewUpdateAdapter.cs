using Android.Runtime;
using Kotlin.Jvm.Functions;
using AView = Android.Views.View;

namespace AndroidX.Compose;

/// <summary>
/// <c>Function1&lt;View, Unit&gt;</c> JCW used by
/// <see cref="AndroidView"/> as Compose's optional <c>update</c>
/// argument.
/// </summary>
[Register("net/compose/AndroidViewUpdateAdapter")]
internal sealed class AndroidViewUpdateAdapter : Java.Lang.Object, IFunction1
{
    static Java.Lang.Object? s_unit;

    readonly Action<AView> _update;

    public AndroidViewUpdateAdapter(Action<AView> update) => _update = update;

    public Java.Lang.Object Invoke(Java.Lang.Object? p0)
    {
        _update((AView)p0!);
        // Kotlin `Unit`. Returning null would fault inside Compose's
        // adapter; a singleton wrapper keeps allocations off the hot
        // recomposition path.
        return s_unit ??= Kotlin.Unit.Instance!;
    }
}
