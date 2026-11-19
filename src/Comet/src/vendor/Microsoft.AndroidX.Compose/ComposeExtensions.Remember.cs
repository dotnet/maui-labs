using System.Runtime.CompilerServices;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

public static partial class ComposeExtensions
{
    /// <summary>
    /// Compose's <c>remember { factory() }</c>, backed by the active
    /// composer's slot table. Returns the value of <paramref name="factory"/>
    /// the first time this call site is reached, then the cached value on
    /// subsequent recompositions.
    ///
    /// The slot is keyed by <see cref="SourceLocationKey"/>'s FNV-1a hash
    /// of the file path mixed with the line number — a stable identifier
    /// derived from <see cref="CallerLineNumberAttribute"/> /
    /// <see cref="CallerFilePathAttribute"/> fill-ins, deterministic
    /// across process restarts (unlike <see cref="HashCode"/>,
    /// which is per-process randomized) so the saveable-state registry
    /// can match the recomputed key to its stored value on restore.
    ///
    /// Wrapped in <c>StartReplaceableGroup</c> / <c>EndReplaceableGroup</c>
    /// so the slot belongs to its own group; sibling positional grouping
    /// (see <see cref="ComposableContainer.RenderChildren"/>) then keeps
    /// repeated helper calls in a parent container from sharing slots.
    /// </summary>
    public static T Remember<T>(
        this IComposer composer,
        Func<T> factory,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => RememberCore(composer, factory, keys: null, line, file);

    /// <summary>
    /// Keyed <c>remember(key1) { factory() }</c>: the cached value is
    /// re-created whenever <paramref name="key1"/> changes (structural
    /// equality via <see cref="object.Equals(object?, object?)"/>).
    /// </summary>
    public static T Remember<T>(
        this IComposer composer,
        Func<T> factory,
        object? key1,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => RememberCore(composer, factory, new[] { key1 }, line, file);

    /// <summary>Keyed <c>remember(key1, key2) { factory() }</c>.</summary>
    public static T Remember<T>(
        this IComposer composer,
        Func<T> factory,
        object? key1,
        object? key2,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => RememberCore(composer, factory, new[] { key1, key2 }, line, file);

    /// <summary>Keyed <c>remember(key1, key2, key3) { factory() }</c>.</summary>
    public static T Remember<T>(
        this IComposer composer,
        Func<T> factory,
        object? key1,
        object? key2,
        object? key3,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => RememberCore(composer, factory, new[] { key1, key2, key3 }, line, file);

    /// <summary>
    /// Array-form keyed <c>remember(vararg keys) { factory() }</c>.
    /// Use when the caller has more than three keys or already has the
    /// keys in an array.
    /// </summary>
    public static T RememberKeyed<T>(
        this IComposer composer,
        Func<T> factory,
        object?[] keys,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => RememberCore(composer, factory, keys ?? throw new ArgumentNullException(nameof(keys)), line, file);

    static T RememberCore<T>(IComposer composer, Func<T> factory, object?[]? keys, int line, string file)
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(factory);

        composer.StartReplaceableGroup(SourceLocationKey.Compute(line, file));
        try
        {
            if (composer.RememberedValue() is RememberHolder existing
                && RememberHolder.KeysEqual(existing.Keys, keys))
            {
                return (T)existing.Value!;
            }
            var value = factory();
            composer.UpdateRememberedValue(new RememberHolder(value, keys));
            return value;
        }
        finally
        {
            composer.EndReplaceableGroup();
        }
    }

    /// <summary>
    /// Compose's <c>rememberSaveable { factory() }</c>: like
    /// <see cref="Remember{T}(IComposer, Func{T}, int, string)"/>, but the
    /// cached value also survives <b>process death and activity
    /// recreation</b> (e.g. rotation when the activity doesn't override
    /// <c>android:configChanges</c>) via Compose's
    /// <c>SaveableStateRegistry</c>, which serialises into the
    /// activity's saved-instance <see cref="Bundle"/>.
    ///
    /// Mirrors Kotlin's single <c>rememberSaveable&lt;T&gt;</c> entry
    /// point — the same call works for scalar values
    /// (<c>int</c>, <c>long</c>, <c>float</c>, <c>double</c>,
    /// <c>bool</c>, <c>string</c>, plus any other type
    /// <see cref="MutableState{T}"/> can box / unbox) and for
    /// state-holder wrappers (<see cref="MutableState{U}"/>,
    /// <see cref="MutableNumberState{U}"/>).
    /// </summary>
    public static T RememberSaveable<T>(
        this IComposer composer,
        Func<T> factory,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => RememberSaveableCore(composer, factory, keys: null, line, file);

    /// <summary>
    /// Keyed <c>rememberSaveable(key1) { factory() }</c>: keys flow into
    /// Kotlin's <c>inputs</c> array so Compose's saveable registry
    /// invalidates the cached value when any key changes.
    /// </summary>
    public static T RememberSaveable<T>(
        this IComposer composer,
        Func<T> factory,
        object? key1,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => RememberSaveableCore(composer, factory, new[] { key1 }, line, file);

    /// <summary>Keyed <c>rememberSaveable(key1, key2) { factory() }</c>.</summary>
    public static T RememberSaveable<T>(
        this IComposer composer,
        Func<T> factory,
        object? key1,
        object? key2,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => RememberSaveableCore(composer, factory, new[] { key1, key2 }, line, file);

    /// <summary>Keyed <c>rememberSaveable(key1, key2, key3) { factory() }</c>.</summary>
    public static T RememberSaveable<T>(
        this IComposer composer,
        Func<T> factory,
        object? key1,
        object? key2,
        object? key3,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => RememberSaveableCore(composer, factory, new[] { key1, key2, key3 }, line, file);

    /// <summary>
    /// Array-form keyed <c>rememberSaveable(vararg inputs) { factory() }</c>.
    /// </summary>
    public static T RememberSaveableKeyed<T>(
        this IComposer composer,
        Func<T> factory,
        object?[] keys,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => RememberSaveableCore(composer, factory, keys ?? throw new ArgumentNullException(nameof(keys)), line, file);

    static T RememberSaveableCore<T>(IComposer composer, Func<T> factory, object?[]? keys, int line, string file)
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(factory);

        composer.StartReplaceableGroup(SourceLocationKey.Compute(line, file));
        try
        {
            if (typeof(IMutableStateWrapper).IsAssignableFrom(typeof(T)))
                return RememberSaveableWrapper(composer, factory, keys);

            return RememberSaveableScalar(composer, factory, keys);
        }
        finally
        {
            composer.EndReplaceableGroup();
        }
    }

    static T RememberSaveableScalar<T>(IComposer composer, Func<T> factory, object?[]? keys)
    {
        var inputs = ComposeBridges.BuildKeysArray(keys, out var ownsInputs);
        var jcw = new ObjectFunction0(() => MutableState<T>.ToJava(factory()));
        var handle = ComposeBridges.RememberSaveableSimple(
            inputs,
            jcw,
            composer,
            changed: 0);
        try
        {
            if (handle == IntPtr.Zero)
                return default!;
            var boxed = Java.Lang.Object.GetObject<Java.Lang.Object>(
                handle, Android.Runtime.JniHandleOwnership.DoNotTransfer);
            return MutableState<T>.FromJava(boxed);
        }
        finally
        {
            if (handle != IntPtr.Zero)
                Android.Runtime.JNIEnv.DeleteLocalRef(handle);
            if (ownsInputs && inputs != IntPtr.Zero)
                Android.Runtime.JNIEnv.DeleteLocalRef(inputs);
        }
    }

    static T RememberSaveableWrapper<T>(IComposer composer, Func<T> factory, object?[]? keys)
    {
        // Cache the C# wrapper across recompositions so we don't
        // allocate a fresh facade + Kotlin IMutableState on every
        // render. Nested Remember opens its own replaceable group;
        // nesting is fine — Compose's slot table handles it.
        var wrapper = composer.Remember(factory)
            ?? throw new InvalidOperationException(
                $"RememberSaveable<{typeof(T).Name}>: factory returned null.");
        var iwrap = (IMutableStateWrapper)wrapper;

        var inputs = ComposeBridges.BuildKeysArray(keys, out var ownsInputs);
        var jcw = new ObjectFunction0(() => (Java.Lang.Object)iwrap.State);
        var handle = ComposeBridges.RememberSaveableMutableState(
            inputs,
            ComposeBridges.SaverAutoSaver(),
            jcw,
            composer,
            changed: 0);
        try
        {
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"RememberSaveable<{typeof(T).Name}>: rememberSaveable returned null.");
            iwrap.State = Java.Lang.Object.GetObject<IMutableState>(
                handle, Android.Runtime.JniHandleOwnership.DoNotTransfer)!;
            return wrapper;
        }
        finally
        {
            if (handle != IntPtr.Zero)
                Android.Runtime.JNIEnv.DeleteLocalRef(handle);
            if (ownsInputs && inputs != IntPtr.Zero)
                Android.Runtime.JNIEnv.DeleteLocalRef(inputs);
        }
    }
}
