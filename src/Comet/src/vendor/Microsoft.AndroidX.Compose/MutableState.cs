using System.Runtime.CompilerServices;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Generic typed wrapper around <see cref="IMutableState"/>. Reads/writes
/// values through the JVM state container so changes are observed by the
/// Compose runtime and trigger recomposition.
/// </summary>
/// <remarks>
/// <para>
/// For <c>int</c>, <c>long</c>, and <c>float</c>, the ctor picks a
/// primitive-specialized Compose state
/// (<see cref="IMutableIntState"/>, <see cref="IMutableLongState"/>,
/// <see cref="IMutableFloatState"/>) so reads/writes avoid the
/// <c>Java.Lang.Integer</c>/<c>Long</c>/<c>Float</c> allocation that the
/// generic boxed path pays on every access. All other primitives that
/// <see cref="MutableState{T}"/> can box (<c>sbyte</c>, <c>byte</c>,
/// <c>short</c>, <c>ushort</c>, <c>uint</c>, <c>ulong</c>, <c>double</c>,
/// <c>bool</c>, <c>char</c>, <c>string</c>, <c>Nullable&lt;T&gt;</c> of any
/// of those) fall through to the boxed path. Reference types deriving from
/// <see cref="Java.Lang.Object"/> are stored as-is.
/// </para>
/// <para>
/// The primitive-specialized fast path requires the underlying
/// <see cref="IMutableState"/> to actually be a primitive subtype. That's
/// true when constructed via <c>new MutableState&lt;int&gt;(0)</c> — the
/// ctor picks the right Kotlin factory. It's <i>not</i> guaranteed when
/// the wrapper is built around a state produced elsewhere, such as the
/// boxed <c>mutableStateOf(restoredValue, policy)</c> that
/// <see cref="ComposeExtensions.RememberSaveable{T}(System.Func{T}, int, string)"/>
/// returns after a process-death restore. The instance <c>_kind</c> field
/// is probed from the supplied state once at construction (and re-probed
/// on every <see cref="SetUnderlyingState"/> swap) so the <see cref="Value"/>
/// getter/setter never attempts an invalid cast.
/// </para>
/// </remarks>
public class MutableState<T> : IMutableStateWrapper, IState<T>
{
    enum Kind { Other, Int, Long, Float }

    internal IMutableState _state;
    Kind _kind;

    /// <summary>
    /// Wrap a fresh Compose state initialized with <paramref name="initial"/>.
    /// Picks the primitive-specialized
    /// <see cref="IMutableIntState"/>/<see cref="IMutableLongState"/>/<see cref="IMutableFloatState"/>
    /// when <typeparamref name="T"/> is <c>int</c>/<c>long</c>/<c>float</c>;
    /// otherwise creates a boxed <c>mutableStateOf</c> with structural
    /// equality.
    /// </summary>
    public MutableState(T initial)
    {
        _state = CreateState(initial);
        _kind = ProbeKind(_state);
    }

    // Subclass hook: lets a wrapper be built around a pre-existing
    // IMutableState (e.g. one Compose handed back after RememberSaveable
    // restoration). Probes _kind so the fast-path dispatch in Value stays
    // correct regardless of who built the state.
    internal MutableState(IMutableState state)
    {
        _state = state;
        _kind = ProbeKind(state);
    }

    /// <summary>
    /// Subclass hook for <see cref="IMutableStateWrapper.State"/>'s setter.
    /// Swaps the underlying <see cref="IMutableState"/> and re-probes the
    /// primitive-fast-path <c>_kind</c> field so subsequent
    /// <see cref="Value"/> reads/writes target the right specialised getter.
    /// Compose calls this through the <see cref="IMutableStateWrapper.State"/>
    /// setter after <c>rememberSaveable</c> hands back a (potentially boxed)
    /// restored state on first composition.
    /// </summary>
    internal virtual void SetUnderlyingState(IMutableState state)
    {
        _state = state;
        _kind = ProbeKind(state);
    }

    IMutableState IMutableStateWrapper.State
    {
        get => _state;
        set => SetUnderlyingState(value);
    }

    /// <summary>
    /// The current value. Reading inside a composition subscribes the
    /// surrounding scope to changes; writing triggers recomposition of any
    /// scope that previously read the value. For
    /// <c>int</c>/<c>long</c>/<c>float</c>-typed instances, reads/writes
    /// route through the primitive Compose state without any boxing.
    /// </summary>
    public virtual T Value
    {
        get
        {
            switch (_kind)
            {
                case Kind.Int:
                    int i = ((IMutableIntState)_state).IntValue;
                    return Unsafe.As<int, T>(ref i);
                case Kind.Long:
                    long l = ((IMutableLongState)_state).LongValue;
                    return Unsafe.As<long, T>(ref l);
                case Kind.Float:
                    float f = ((IMutableFloatState)_state).FloatValue;
                    return Unsafe.As<float, T>(ref f);
                default:
                    return FromJava(_state.Value);
            }
        }
        set
        {
            switch (_kind)
            {
                case Kind.Int:
                    ((IMutableIntState)_state).IntValue = Unsafe.As<T, int>(ref value);
                    break;
                case Kind.Long:
                    ((IMutableLongState)_state).LongValue = Unsafe.As<T, long>(ref value);
                    break;
                case Kind.Float:
                    ((IMutableFloatState)_state).FloatValue = Unsafe.As<T, float>(ref value);
                    break;
                default:
                    _state.Value = ToJava(value);
                    break;
            }
        }
    }

    /// <summary>
    /// Implicit conversion to the wrapped value so call sites read
    /// idiomatically — e.g. <c>if (count &gt; 0)</c>, <c>int total = count;</c>,
    /// <c>(int)Math.Sqrt(count)</c> — without sprinkling <c>.Value</c>
    /// everywhere. Mirrors Kotlin's <c>by remember { mutableStateOf(...) }</c>
    /// property-delegate ergonomics.
    /// </summary>
    /// <remarks>
    /// String interpolation (<c>$"...{state}..."</c>) keeps calling
    /// <see cref="ToString"/> — interpolation boxes its arguments as
    /// <c>object</c>, and the C# compiler does <i>not</i> apply user-defined
    /// conversions when boxing — so this operator does not change
    /// interpolation behavior.
    /// </remarks>
    public static implicit operator T(MutableState<T> state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Value;
    }

    /// <summary>
    /// Returns the underlying value's string representation so
    /// <c>$"...{state}..."</c> interpolation reads as Kotlin would
    /// (a null value renders as <c>"null"</c>).
    /// </summary>
    public override string ToString() => Value?.ToString() ?? "null";

    static IMutableState CreateState(T initial)
    {
        if (typeof(T) == typeof(int))
            return SnapshotIntStateKt.MutableIntStateOf(Unsafe.As<T, int>(ref initial));
        if (typeof(T) == typeof(long))
            return SnapshotLongStateKt.MutableLongStateOf(Unsafe.As<T, long>(ref initial));
        if (typeof(T) == typeof(float))
            return PrimitiveSnapshotStateKt.MutableFloatStateOf(Unsafe.As<T, float>(ref initial));
        return SnapshotStateKt.MutableStateOf(
            ToJava(initial),
            SnapshotStateKt.StructuralEqualityPolicy());
    }

    static Kind ProbeKind(IMutableState state) => state switch
    {
        IMutableIntState   => Kind.Int,
        IMutableLongState  => Kind.Long,
        IMutableFloatState => Kind.Float,
        _                  => Kind.Other,
    };

    // Cached per-T: when T is Nullable<U> (e.g. long?, int?, bool?), this
    // is typeof(U) so the primitive dispatch in FromJava/ToJava treats
    // `long?` the same as `long`. For all other T, it's just typeof(T).
    static readonly Type s_effectiveType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

    internal static Java.Lang.Object? ToJava(T value)
    {
        if (value is null) return null;
        // Boxing a non-null Nullable<U> to object yields a boxed U, not a
        // boxed Nullable<U>. That lets one switch handle both T == long and
        // T == long? without a separate nullable branch.
        object boxed = value!;
        return boxed switch
        {
            Java.Lang.Object o    => o,
            string s              => new Java.Lang.String(s),
            bool b                => Java.Lang.Boolean.ValueOf(b),
            char c                => Java.Lang.Character.ValueOf(c),
            sbyte sb              => Java.Lang.Byte.ValueOf(sb),
            byte by               => Java.Lang.Short.ValueOf((short)by),                  // widen 8u -> 16s
            short sh              => Java.Lang.Short.ValueOf(sh),
            ushort us             => Java.Lang.Integer.ValueOf(us),                       // widen 16u -> 32s
            int i                 => Java.Lang.Integer.ValueOf(i),
            uint ui               => Java.Lang.Long.ValueOf(ui),                          // widen 32u -> 64s
            long l                => Java.Lang.Long.ValueOf(l),
            ulong ul              => Java.Lang.Long.ValueOf(unchecked((long)ul)),         // bit-exact round-trip
            float f               => Java.Lang.Float.ValueOf(f),
            double d              => Java.Lang.Double.ValueOf(d),
            _                     => throw new NotSupportedException(
                                        $"MutableState<{typeof(T).Name}>: T must be a Java.Lang.Object subclass, string, bool, char, or a built-in numeric primitive (sbyte/byte/short/ushort/int/uint/long/ulong/float/double), or a Nullable<T> of any of those primitives. Types like decimal, Half, BigInteger, and IntPtr have no clean Java box and are not supported.")
        };
    }

    internal static T FromJava(Java.Lang.Object? value)
    {
        // For T == Nullable<U> this returns the empty Nullable (HasValue=false);
        // for reference T it returns null; for non-nullable struct T it's
        // unreachable in practice because Compose wouldn't store a Java null
        // there, but `default!` is still the only sane fallback.
        if (value is null) return default!;
        if (value is T t) return t;
        // Use the underlying type when T is Nullable<U>, so e.g. `long?`
        // dispatches into the same branch as `long`. The unbox cast
        // `(T)(object)5L` works for T == long? because boxed-primitive
        // unboxes directly into Nullable<long>.
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
        throw new NotSupportedException(
            $"MutableState<{typeof(T).Name}>: don't know how to unwrap {value.GetType().Name}");
    }
}

