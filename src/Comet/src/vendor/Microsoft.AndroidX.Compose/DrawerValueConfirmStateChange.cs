using Android.Runtime;
using AndroidX.Compose.Material3;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// <c>Function1&lt;DrawerValue, Boolean&gt;</c> adapter used as the
/// <c>confirmStateChange</c> callback when constructing a
/// <c>DrawerState</c> via <c>rememberDrawerState</c>. The callback is
/// part of the <c>remember</c> cache key in Kotlin, so this type is
/// allocated <strong>once per <see cref="ComposableNode"/> instance</strong>
/// and reused across recompositions — a fresh adapter each pass would
/// invalidate the cached state holder and the drawer would forget
/// whether it's open. <see cref="Callback"/> is read on every JNI
/// invocation, so callers may mutate it freely without re-allocating.
/// When <see cref="Callback"/> is <c>null</c> the adapter behaves as
/// the Kotlin default <c>{ true }</c> — every transition is allowed.
/// </summary>
/// <remarks>
/// The name follows the source generator's convention
/// <c>&lt;TName&gt;ConfirmStateChange</c> so a
/// <c>[ConfirmStateChange(typeof(DrawerValue))]</c> attribute on a
/// Remember bridge parameter resolves to this adapter automatically.
/// Public so generated facade classes (which live alongside this type
/// in <c>AndroidX.Compose</c>) can declare it as a <c>readonly</c> field;
/// it is not part of the developer-facing API and should not be
/// constructed directly.
/// </remarks>
[Register("net/compose/DrawerValueConfirmStateChange")]
public sealed class DrawerValueConfirmStateChange : Java.Lang.Object, IFunction1
{
    /// <summary>
    /// Developer-supplied veto delegate, assigned by the generator-
    /// emitted facade in its <c>Render</c> preamble from the facade's
    /// <c>ConfirmStateChange</c> property. Treated as
    /// <c>{ true }</c> when <c>null</c>.
    /// </summary>
    public Func<DrawerValue, bool>? Callback { get; set; }

    /// <summary>
    /// Kotlin <c>Function1.invoke</c> entry point. Marshals the JNI
    /// argument to <see cref="DrawerValue"/>, invokes
    /// <see cref="Callback"/>, and returns a boxed
    /// <see cref="Java.Lang.Boolean"/>.
    /// </summary>
    public Java.Lang.Object? Invoke(Java.Lang.Object? p0)
    {
        var cb = Callback;
        if (cb is null)
            return Java.Lang.Boolean.True;
        var value = Android.Runtime.Extensions.JavaCast<DrawerValue>(p0!);
        return cb(value) ? Java.Lang.Boolean.True : Java.Lang.Boolean.False;
    }
}
