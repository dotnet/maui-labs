using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Single-field <see cref="Java.Lang.Object"/> wrapper that lets
/// <see cref="ComposeExtensions.Remember{T}(Func{T}, int, string)"/> store an arbitrary managed
/// <c>T</c> (often a <see cref="MutableState{T}"/>) in Compose's slot
/// table, which only holds <see cref="Java.Lang.Object"/> references.
///
/// Registered with the .NET-for-Android peer cache (<c>[Register]</c>
/// + the IntPtr/JniHandleOwnership ctor) so that when Compose hands
/// the slot value back through <see cref="AndroidX.Compose.Runtime.IComposer.RememberedValue"/>
/// on a recomposition, the runtime rehydrates the same managed
/// <c>RememberHolder</c> peer rather than a plain
/// <see cref="Java.Lang.Object"/> shell — which would lose the
/// <see cref="Value"/> field and force the factory to re-run.
///
/// When a keyed <see cref="ComposeExtensions.Remember{T}(Func{T}, object?, int, string)"/> overload is used,
/// the supplied keys are cloned into <see cref="Keys"/> so a later
/// recomposition can compare them via <see cref="KeysEqual"/>; mismatch
/// invalidates the slot and forces the factory to re-run.
/// </summary>
[Register("net/compose/RememberHolder")]
internal sealed class RememberHolder : Java.Lang.Object
{
    /// <summary>The remembered managed value (boxed if a value type).</summary>
    public object? Value;

    /// <summary>
    /// Defensive snapshot of the caller-supplied keys, or <c>null</c>
    /// for the keyless overload. Cloned at construction so later
    /// caller mutation of the original array doesn't corrupt the
    /// "previous keys" comparison.
    /// </summary>
    public object?[]? Keys;

    public RememberHolder() { }

    public RememberHolder(object? value) => Value = value;

    public RememberHolder(object? value, object?[]? keys)
    {
        Value = value;
        Keys = keys is null ? null : (object?[])keys.Clone();
    }

    // Peer-rehydration ctor: called by the .NET-for-Android runtime when
    // a JNI handle for our [Register]'d class crosses back into managed
    // code and its managed peer needs to be (re)created. We do NOT touch
    // Value here — the original instance's Value field stays attached to
    // whichever peer the runtime returns from its peer cache.
    internal RememberHolder(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer) { }

    /// <summary>
    /// Structural-equality comparison of two key arrays, matching
    /// Kotlin's <c>remember(k1, k2, …) { … }</c> invalidation rules:
    /// two <c>null</c> arrays are equal, length mismatch is unequal,
    /// elements are compared with <see cref="object.Equals(object?, object?)"/>.
    /// </summary>
    internal static bool KeysEqual(object?[]? a, object?[]? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!object.Equals(a[i], b[i])) return false;
        }
        return true;
    }
}
