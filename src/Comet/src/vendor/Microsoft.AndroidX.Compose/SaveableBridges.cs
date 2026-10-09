using Android.Runtime;

namespace AndroidX.Compose;

// Hand-written raw-JNI bridges for the
// `androidx.compose.runtime.saveable` package. The Xamarin
// NuGet (`Xamarin.AndroidX.Compose.Runtime.Saveable.Android`) ships
// the Java library but does not generate C# bindings, so every
// Saveable entry point we use here goes through `JNIEnv.*` calls.
// Same model as `SuspendBridges.cs`.
//
// When/if `dotnet/android-libraries` adds bindings for this package,
// these methods can be replaced with direct binding calls and this
// file can shrink to whatever is still mangled or unbound.
internal static partial class ComposeBridges
{
    static IntPtr s_autoSaver_class;
    static IntPtr s_autoSaver_method;
    static IntPtr s_autoSaver_handle;

    static IntPtr s_rememberSaveableSimple_class;
    static IntPtr s_rememberSaveableSimple_method;

    static IntPtr s_rememberSaveableState_class;
    static IntPtr s_rememberSaveableState_method;

    static IntPtr s_emptyObjectArray;

    // androidx.compose.runtime.saveable.SaverKt.autoSaver(): Saver<T, Object>
    //
    // The default `Saver` Compose uses for any `Bundle`-saveable value
    // (primitives, strings, parcelables, …). Cached as a global ref —
    // returns the same singleton on every call from Kotlin, and we
    // need it as the `stateSaver` argument for the MutableState-overload
    // of `rememberSaveable`.
    internal static IntPtr SaverAutoSaver()
    {
        if (s_autoSaver_handle != IntPtr.Zero)
            return s_autoSaver_handle;

        if (s_autoSaver_method == IntPtr.Zero)
        {
            s_autoSaver_class = JNIEnv.FindClass("androidx/compose/runtime/saveable/SaverKt");
            s_autoSaver_method = JNIEnv.GetStaticMethodID(
                s_autoSaver_class,
                "autoSaver",
                "()Landroidx/compose/runtime/saveable/Saver;");
        }

        var local = JNIEnv.CallStaticObjectMethod(s_autoSaver_class, s_autoSaver_method);
        try
        {
            s_autoSaver_handle = JNIEnv.NewGlobalRef(local);
        }
        finally
        {
            JNIEnv.DeleteLocalRef(local);
        }
        return s_autoSaver_handle;
    }

    // Lazily-cached empty `Object[]` for the `vararg inputs` parameter
    // of `rememberSaveable`. Compose treats inputs as "dependency
    // tracking" — if any element changes, the cached factory result
    // is discarded. We always pass an empty array (= "never invalidate
    // on input change"); future overloads with `params object?[]`
    // can build the array on demand.
    internal static IntPtr EmptyObjectArray()
    {
        if (s_emptyObjectArray != IntPtr.Zero)
            return s_emptyObjectArray;

        var objectClass = Java.Lang.Class.FromType(typeof(Java.Lang.Object));
        var local = JNIEnv.NewObjectArray(0, objectClass.Handle, IntPtr.Zero);
        try
        {
            s_emptyObjectArray = JNIEnv.NewGlobalRef(local);
        }
        finally
        {
            JNIEnv.DeleteLocalRef(local);
        }
        return s_emptyObjectArray;
    }

    // androidx.compose.runtime.saveable.RememberSaveableKt.rememberSaveable(
    //     Object[] inputs,
    //     Function0<? extends T> init,
    //     Composer composer,
    //     int $changed): Object
    //
    // The no-saver overload — Compose internally uses `autoSaver()`,
    // which handles every Bundle-saveable value (primitives, strings,
    // parcelables). Returns a JNI local ref to the boxed result.
    internal static unsafe IntPtr RememberSaveableSimple(
        IntPtr inputs,
        ObjectFunction0 init,
        AndroidX.Compose.Runtime.IComposer composer,
        int changed)
    {
        if (s_rememberSaveableSimple_method == IntPtr.Zero)
        {
            s_rememberSaveableSimple_class = JNIEnv.FindClass(
                "androidx/compose/runtime/saveable/RememberSaveableKt");
            s_rememberSaveableSimple_method = JNIEnv.GetStaticMethodID(
                s_rememberSaveableSimple_class,
                "rememberSaveable",
                "([Ljava/lang/Object;Lkotlin/jvm/functions/Function0;Landroidx/compose/runtime/Composer;I)Ljava/lang/Object;");
        }

        try
        {
            JValue* args = stackalloc JValue[4];
            args[0] = new JValue(inputs);
            args[1] = new JValue(init.Handle);
            args[2] = new JValue(((Java.Lang.Object)composer).Handle);
            args[3] = new JValue(changed);
            return JNIEnv.CallStaticObjectMethod(
                s_rememberSaveableSimple_class,
                s_rememberSaveableSimple_method,
                args);
        }
        finally
        {
            GC.KeepAlive(init);
            GC.KeepAlive(composer);
        }
    }

    // androidx.compose.runtime.saveable.RememberSaveableKt.rememberSaveable(
    //     Object[] inputs,
    //     Saver<T, ?> stateSaver,
    //     Function0<? extends MutableState<T>> init,
    //     Composer composer,
    //     int $changed): MutableState<T>
    //
    // The MutableState overload — Compose internally wraps
    // `stateSaver` with `mutableStateSaver(stateSaver)` and persists
    // only the inner value. On restore Compose calls
    // `mutableStateOf(restoredValue, policy)` to rebuild the state
    // wrapper, so the returned `IMutableState` is *not* guaranteed
    // to be a primitive-specialized `IMutableIntState`/etc. — it's
    // the boxed default unless the caller supplied a custom saver
    // that re-creates the specialized type.
    internal static unsafe IntPtr RememberSaveableMutableState(
        IntPtr inputs,
        IntPtr stateSaver,
        ObjectFunction0 init,
        AndroidX.Compose.Runtime.IComposer composer,
        int changed)
    {
        if (s_rememberSaveableState_method == IntPtr.Zero)
        {
            s_rememberSaveableState_class = JNIEnv.FindClass(
                "androidx/compose/runtime/saveable/RememberSaveableKt");
            s_rememberSaveableState_method = JNIEnv.GetStaticMethodID(
                s_rememberSaveableState_class,
                "rememberSaveable",
                "([Ljava/lang/Object;Landroidx/compose/runtime/saveable/Saver;Lkotlin/jvm/functions/Function0;Landroidx/compose/runtime/Composer;I)Landroidx/compose/runtime/MutableState;");
        }

        try
        {
            JValue* args = stackalloc JValue[5];
            args[0] = new JValue(inputs);
            args[1] = new JValue(stateSaver);
            args[2] = new JValue(init.Handle);
            args[3] = new JValue(((Java.Lang.Object)composer).Handle);
            args[4] = new JValue(changed);
            return JNIEnv.CallStaticObjectMethod(
                s_rememberSaveableState_class,
                s_rememberSaveableState_method,
                args);
        }
        finally
        {
            GC.KeepAlive(init);
            GC.KeepAlive(composer);
        }
    }

    // Builds a Kotlin-compatible `Object[]` from a managed `object?[]`
    // for the `inputs` (vararg) parameter of `rememberSaveable`. Each
    // element is boxed via the same primitive→Java.Lang.Object switch
    // MutableState<T> uses, so primitives compare structurally rather
    // than by reference. Returns a JNI local ref the caller MUST free
    // (call DeleteLocalRef inside a finally). Also creates local refs
    // for each boxed element; we delete each one immediately after
    // setting it into the array (the array owns its own ref).
    //
    // Null/empty keys array → `EmptyObjectArray()`, a cached global
    // ref — `ownsHandle` is false, signalling the caller MUST NOT
    // delete the returned handle.
    internal static IntPtr BuildKeysArray(object?[]? keys, out bool ownsHandle)
    {
        if (keys is null || keys.Length == 0)
        {
            ownsHandle = false;
            return EmptyObjectArray();
        }

        ownsHandle = true;
        var objectClass = Java.Lang.Class.FromType(typeof(Java.Lang.Object));
        var array = JNIEnv.NewObjectArray(keys.Length, objectClass.Handle, IntPtr.Zero);
        for (int i = 0; i < keys.Length; i++)
        {
            var boxed = BoxKey(keys[i], out var ownsBoxed);
            if (boxed is null)
            {
                JNIEnv.SetObjectArrayElement(array, i, IntPtr.Zero);
                continue;
            }
            JNIEnv.SetObjectArrayElement(array, i, boxed.Handle);
            // Only dispose freshly created boxing wrappers — disposing
            // a pass-through caller-owned Java.Lang.Object would
            // invalidate their managed peer.
            if (ownsBoxed) boxed.Dispose();
        }
        return array;
    }

    static Java.Lang.Object? BoxKey(object? key, out bool owns)
    {
        switch (key)
        {
            case null:
                owns = false;
                return null;
            case Java.Lang.Object o:
                // Caller-owned — do NOT dispose; the array still
                // takes its own JNI ref via SetObjectArrayElement.
                owns = false;
                return o;
            case string s:           owns = true; return new Java.Lang.String(s);
            case bool b:             owns = true; return Java.Lang.Boolean.ValueOf(b);
            case char c:             owns = true; return Java.Lang.Character.ValueOf(c);
            case sbyte sb:           owns = true; return Java.Lang.Byte.ValueOf(sb);
            case byte by:            owns = true; return Java.Lang.Short.ValueOf((short)by);
            case short sh:           owns = true; return Java.Lang.Short.ValueOf(sh);
            case ushort us:          owns = true; return Java.Lang.Integer.ValueOf(us);
            case int i:              owns = true; return Java.Lang.Integer.ValueOf(i);
            case uint ui:            owns = true; return Java.Lang.Long.ValueOf(ui);
            case long l:             owns = true; return Java.Lang.Long.ValueOf(l);
            case ulong ul:           owns = true; return Java.Lang.Long.ValueOf(unchecked((long)ul));
            case float f:            owns = true; return Java.Lang.Float.ValueOf(f);
            case double d:           owns = true; return Java.Lang.Double.ValueOf(d);
            default:                 owns = true; return new Java.Lang.String(key.ToString() ?? string.Empty);
        }
    }
}
