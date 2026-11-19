using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Type-inferred entry point for creating <see cref="CompositionLocal{T}"/>
/// instances. Mirrors Kotlin's top-level <c>compositionLocalOf</c> and
/// <c>staticCompositionLocalOf</c> functions:
/// <code>
/// public static readonly CompositionLocal&lt;MyTheme&gt; LocalMyTheme =
///     CompositionLocal.StaticOf(() =&gt; MyTheme.Default);
/// </code>
/// </summary>
public static class CompositionLocal
{
    /// <summary>
    /// Create a <i>static</i> composition local. Readers do <b>not</b>
    /// recompose when the provided value changes — instead, the entire
    /// content lambda of the enclosing
    /// <see cref="CompositionLocalProvider"/> is recomposed. Use this
    /// for values that rarely change (theme tokens, density-like
    /// configuration) since it avoids the per-reader bookkeeping cost
    /// of the dynamic variant.
    /// </summary>
    public static CompositionLocal<T> StaticOf<T>(Func<T> defaultFactory) =>
        CompositionLocal<T>.StaticOf(defaultFactory);

    /// <summary>
    /// Create a <i>dynamic</i> composition local backed by
    /// <c>structuralEqualityPolicy</c>. Only the composables that
    /// actually read <see cref="CompositionLocal{T}.Current"/> are
    /// invalidated when the provided value changes (by
    /// <see cref="object.Equals(object?)"/>) — the rest of the
    /// provider's subtree is skipped.
    /// </summary>
    public static CompositionLocal<T> Of<T>(Func<T> defaultFactory) =>
        CompositionLocal<T>.Of(defaultFactory);
}

/// <summary>
/// Typed wrapper around Kotlin's
/// <c>androidx.compose.runtime.ProvidableCompositionLocal</c>. A
/// composition local is a slot in the composition that propagates a
/// value down the tree implicitly — readers pick up the nearest
/// enclosing <see cref="CompositionLocalProvider"/>'s value, or the
/// default if none is in scope.
///
/// <para>Instances are usually created once (a <c>static readonly</c>
/// field) via <see cref="CompositionLocal.StaticOf{T}"/> or
/// <see cref="CompositionLocal.Of{T}"/>, then provided per-subtree
/// with <see cref="Provides(T)"/> and read inside a composable's
/// <c>Render</c> method via
/// <see cref="Current(IComposer)"/>.</para>
///
/// <para><b>Boxing.</b> Reference values that already derive from
/// <see cref="Java.Lang.Object"/> are passed through unchanged; the
/// built-in primitive / <c>string</c> / numeric types are forwarded
/// to the corresponding <c>java.lang.*</c> box. Any other managed
/// type is wrapped in an internal <c>ManagedBox</c> JCW so pure-C#
/// POCOs (records, classes, structs) work without inheriting from
/// <see cref="Java.Lang.Object"/>.</para>
/// </summary>
public sealed class CompositionLocal<T>
{
    readonly ProvidableCompositionLocal _peer;

    internal CompositionLocal(ProvidableCompositionLocal peer) =>
        _peer = peer;

    /// <summary>
    /// Create a <i>static</i> composition local. Prefer the
    /// type-inferred <see cref="CompositionLocal.StaticOf{T}"/>
    /// entry point — this overload only exists for callers that want
    /// to spell the type argument explicitly.
    /// </summary>
    public static CompositionLocal<T> StaticOf(Func<T> defaultFactory)
    {
        ArgumentNullException.ThrowIfNull(defaultFactory);
        var peer = CompositionLocalKt.StaticCompositionLocalOf(
            new ObjectFunction0(() => ToJava(defaultFactory())));
        return new CompositionLocal<T>(peer);
    }

    /// <summary>
    /// Create a <i>dynamic</i> composition local backed by
    /// <c>structuralEqualityPolicy</c>. Prefer the type-inferred
    /// <see cref="CompositionLocal.Of{T}"/> entry point.
    /// </summary>
    public static CompositionLocal<T> Of(Func<T> defaultFactory)
    {
        ArgumentNullException.ThrowIfNull(defaultFactory);
        var peer = CompositionLocalKt.CompositionLocalOf(
            SnapshotStateKt.StructuralEqualityPolicy(),
            new ObjectFunction0(() => ToJava(defaultFactory())));
        return new CompositionLocal<T>(peer);
    }

    /// <summary>
    /// Read the local's value for the current composition position,
    /// equivalent to Kotlin's <c>MyLocal.current</c>. Pass the
    /// <see cref="IComposer"/> handed to the enclosing
    /// <c>Render</c> method.
    /// </summary>
    public T Current(IComposer composer)
    {
        ArgumentNullException.ThrowIfNull(composer);
        return FromJava(_peer.GetCurrent(composer, 0));
    }

    /// <summary>
    /// Pair this local with <paramref name="value"/> for installation
    /// by a <see cref="CompositionLocalProvider"/>. Equivalent to
    /// Kotlin's infix <c>local provides value</c> syntax.
    /// </summary>
    public ProvidedValue Provides(T value) =>
        new(_peer.Provides(ToJava(value)!));

    // Cached per-T: when T is Nullable<U> (e.g. long?, int?, bool?), this
    // is typeof(U) so the primitive dispatch in FromJava/ToJava treats
    // `long?` the same as `long`. For all other T, it's just typeof(T).
    static readonly Type s_effectiveType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

    static Java.Lang.Object? ToJava(T value)
    {
        if (value is null) return null;
        // Boxing a non-null Nullable<U> to object yields a boxed U, not a
        // boxed Nullable<U>, so one switch handles both T == long and
        // T == long?.
        object boxed = value!;
        return boxed switch
        {
            Java.Lang.Object o => o,
            string s           => new Java.Lang.String(s),
            bool b             => Java.Lang.Boolean.ValueOf(b),
            char c             => Java.Lang.Character.ValueOf(c),
            sbyte sb           => Java.Lang.Byte.ValueOf(sb),
            byte by            => Java.Lang.Short.ValueOf((short)by),
            short sh           => Java.Lang.Short.ValueOf(sh),
            ushort us          => Java.Lang.Integer.ValueOf(us),
            int i              => Java.Lang.Integer.ValueOf(i),
            uint ui            => Java.Lang.Long.ValueOf(ui),
            long l             => Java.Lang.Long.ValueOf(l),
            ulong ul           => Java.Lang.Long.ValueOf(unchecked((long)ul)),
            float f            => Java.Lang.Float.ValueOf(f),
            double d           => Java.Lang.Double.ValueOf(d),
            _                  => new ManagedBox(boxed),
        };
    }

    static T FromJava(Java.Lang.Object? value)
    {
        if (value is null) return default!;
        if (value is ManagedBox box) return (T)box.Value!;
        if (value is T t) return t;
        // Use the underlying type when T is Nullable<U> so `long?`
        // dispatches into the same branch as `long`. `(T)(object)5L`
        // unboxes directly into Nullable<long> for T == long?.
        var type = s_effectiveType;
        if (type == typeof(string))  return (T)(object)value.ToString()!;
        if (type == typeof(bool))    return (T)(object)((Java.Lang.Boolean)value).BooleanValue();
        if (type == typeof(char))    return (T)(object)((Java.Lang.Character)value).CharValue();
        if (type == typeof(sbyte))   return (T)(object)((Java.Lang.Byte)value).ByteValue();
        if (type == typeof(byte))    return (T)(object)(byte)((Java.Lang.Short)value).ShortValue();
        if (type == typeof(short))   return (T)(object)((Java.Lang.Short)value).ShortValue();
        if (type == typeof(ushort))  return (T)(object)(ushort)((Java.Lang.Integer)value).IntValue();
        if (type == typeof(int))     return (T)(object)((Java.Lang.Integer)value).IntValue();
        if (type == typeof(uint))    return (T)(object)(uint)((Java.Lang.Long)value).LongValue();
        if (type == typeof(long))    return (T)(object)((Java.Lang.Long)value).LongValue();
        if (type == typeof(ulong))   return (T)(object)unchecked((ulong)((Java.Lang.Long)value).LongValue());
        if (type == typeof(float))   return (T)(object)((Java.Lang.Float)value).FloatValue();
        if (type == typeof(double))  return (T)(object)((Java.Lang.Double)value).DoubleValue();
        throw new InvalidCastException(
            $"CompositionLocal<{typeof(T).Name}>: don't know how to unwrap a "
            + $"{value.GetType().Name}. Built-in composition locals always return "
            + "their own bound types; for user-defined locals the value must be a "
            + "Java.Lang.Object subclass, a built-in primitive/string, or wrapped in "
            + "ManagedBox (handled automatically by Provides).");
    }
}
