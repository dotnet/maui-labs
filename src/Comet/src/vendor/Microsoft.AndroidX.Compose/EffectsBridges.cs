namespace AndroidX.Compose;

// Raw-JNI helpers for the Compose effect APIs in
// `androidx.compose.runtime.EffectsKt`. The entry points themselves
// (`SideEffect`, `DisposableEffect`, `LaunchedEffect`,
// `RememberCoroutineScope`) are bound directly by the
// `Xamarin.AndroidX.Compose.Runtime.Android` NuGet, so we don't need
// `[ComposeBridge]` shims for them. What we *do* need is:
//
//  - `BoxKey(object?)` — non-generic equivalent of
//    `MutableState<T>.ToJava`, so the public C# effect APIs can take
//    `object?` keys and box primitives once per render.
//
// `kotlin.Result` plumbing (constructing/reading `Result.Failure`)
// lives in <see cref="KotlinResult"/>, shared with `SuspendBridge`.
internal static partial class ComposeBridges
{
    /// <summary>
    /// Non-generic mirror of <see cref="MutableState{T}.ToJava"/> for
    /// boxing an opaque <see cref="object"/> key into the
    /// <see cref="Java.Lang.Object"/> the EffectsKt entry points expect.
    /// Supports <c>null</c>, any <see cref="Java.Lang.Object"/> peer,
    /// <see cref="string"/>, and every common .NET primitive.
    /// </summary>
    internal static Java.Lang.Object? BoxKey(object? key) => key switch
    {
        null                  => null,
        Java.Lang.Object o    => o,
        string s              => new Java.Lang.String(s),
        bool b                => Java.Lang.Boolean.ValueOf(b),
        char c                => Java.Lang.Character.ValueOf(c),
        sbyte sb              => Java.Lang.Byte.ValueOf(sb),
        byte by               => Java.Lang.Short.ValueOf((short)by),
        short sh              => Java.Lang.Short.ValueOf(sh),
        ushort us             => Java.Lang.Integer.ValueOf(us),
        int i                 => Java.Lang.Integer.ValueOf(i),
        uint ui               => Java.Lang.Long.ValueOf(ui),
        long l                => Java.Lang.Long.ValueOf(l),
        ulong ul              => Java.Lang.Long.ValueOf(unchecked((long)ul)),
        float f               => Java.Lang.Float.ValueOf(f),
        double d              => Java.Lang.Double.ValueOf(d),
        // Anything else has no obvious mapping to Java's `Object.equals`
        // semantics that Compose uses for key comparison. Boxed value
        // types (Guid, DateTime, enums, records) can produce a fresh
        // identity hash every render — silently triggering an effect
        // restart on every recomposition. Refuse and let the caller
        // pick a stable representation (a string, a primitive, or a
        // pre-allocated Java.Lang.Object peer).
        _                     => throw new NotSupportedException(
            $"Compose effect key type '{key.GetType().FullName}' is not supported. "
            + "Use a Java.Lang.Object, string, bool, char, sbyte/byte/short/ushort/int/uint/long/ulong/float/double, "
            + "or convert your key to a stable form (e.g. ToString() or a stable hash).")
    };

    /// <summary>
    /// Box an array of <see cref="object"/>? keys into a Java
    /// <c>Object[]</c> for the <c>vararg keys</c> overload of
    /// <see cref="AndroidX.Compose.Runtime.EffectsKt.LaunchedEffect(Java.Lang.Object[], Kotlin.Jvm.Functions.IFunction2, AndroidX.Compose.Runtime.IComposer, int)"/>
    /// /
    /// <see cref="AndroidX.Compose.Runtime.EffectsKt.DisposableEffect(Java.Lang.Object[], Kotlin.Jvm.Functions.IFunction1, AndroidX.Compose.Runtime.IComposer, int)"/>.
    /// Currently unused — the 1/2/3-key overloads cover the common
    /// case and avoid the per-render array allocation. Kept here for
    /// the future variadic public overload.
    /// </summary>
    internal static Java.Lang.Object?[] BoxKeyArray(object?[] keys)
    {
        var boxed = new Java.Lang.Object?[keys.Length];
        for (int i = 0; i < keys.Length; i++)
            boxed[i] = BoxKey(keys[i]);
        return boxed;
    }
}
