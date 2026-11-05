using Android.Runtime;
using AndroidX.Compose.Material3;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// <c>Function1&lt;SheetValue, Boolean&gt;</c> adapter used as the
/// <c>confirmValueChange</c> callback when constructing a
/// <c>SheetState</c> via <c>rememberModalBottomSheetState</c> /
/// <c>rememberStandardBottomSheetState</c>. The callback is part of
/// the <c>remember</c> cache key in Kotlin, so this type is allocated
/// <strong>once per <see cref="ComposableNode"/> instance</strong> and
/// reused across recompositions — a fresh adapter each pass would
/// invalidate the cached state holder and the sheet would forget its
/// position. <see cref="Callback"/> is read on every JNI invocation,
/// so callers may mutate it freely without re-allocating. When
/// <see cref="Callback"/> is <c>null</c> the adapter behaves as the
/// Kotlin default <c>{ true }</c> — every transition is allowed.
/// </summary>
/// <remarks>
/// The name follows the source generator's convention
/// <c>&lt;TName&gt;ConfirmStateChange</c> so a
/// <c>[ConfirmStateChange(typeof(SheetValue))]</c> attribute on a
/// Remember bridge parameter resolves to this adapter automatically.
/// Public so generated facade classes (which live alongside this
/// type in <c>AndroidX.Compose</c>) can declare it as a
/// <c>readonly</c> field; it is not part of the developer-facing API
/// and should not be constructed directly.
/// </remarks>
[Register("net/compose/SheetValueConfirmStateChange")]
public sealed class SheetValueConfirmStateChange : Java.Lang.Object, IFunction1
{
    /// <summary>
    /// Developer-supplied veto delegate, assigned by the
    /// generator-emitted facade in its <c>Render</c> preamble from
    /// the facade's <c>ConfirmValueChange</c> property. Treated as
    /// <c>{ true }</c> when <c>null</c>.
    /// </summary>
    public Func<SheetValue, bool>? Callback { get; set; }

    /// <summary>
    /// Kotlin <c>Function1.invoke</c> entry point. Marshals the JNI
    /// argument to <see cref="SheetValue"/>, invokes
    /// <see cref="Callback"/>, and returns a boxed
    /// <see cref="Java.Lang.Boolean"/>.
    /// </summary>
    public Java.Lang.Object? Invoke(Java.Lang.Object? p0)
    {
        var cb = Callback;
        if (cb is null)
            return Java.Lang.Boolean.True;
        var value = Android.Runtime.Extensions.JavaCast<SheetValue>(p0!);
        return cb(value) ? Java.Lang.Boolean.True : Java.Lang.Boolean.False;
    }
}
