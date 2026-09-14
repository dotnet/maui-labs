using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Wraps an arbitrary managed value as a <see cref="Java.Lang.Object"/>
/// so it can cross the JNI boundary.
/// Used by <see cref="CompositionLocal{T}"/> to hand pure-C# POCO
/// values (themes, settings, custom contexts) to Kotlin's
/// <c>compositionLocalOf</c> / <c>staticCompositionLocalOf</c>
/// machinery without forcing users to make those types derive
/// from <see cref="Java.Lang.Object"/>.
///
/// <para>Non-generic on purpose: a generic JCW would require a
/// per-closed-type Java Callable Wrapper generated at facade
/// compile time, which is impossible for sample-side or any other
/// consumer-defined <c>T</c>. The single <c>net/compose/ManagedBox</c>
/// peer covers every payload; the typed unbox happens in C# inside
/// <see cref="CompositionLocal{T}.FromJava"/>.</para>
///
/// <para>Equality is forwarded to the boxed value's own
/// <see cref="object.Equals(object)"/>/<see cref="object.GetHashCode"/>
/// so structural-equality <c>CompositionLocalOf</c> readers
/// recompose only when the underlying value actually changes — two
/// distinct <see cref="ManagedBox"/> instances wrapping equal
/// values compare equal.</para>
///
/// <para>Held by the Java-side provider machinery while a
/// <see cref="CompositionLocalProvider"/> is on the composition's
/// stack; collected once Compose drops its reference. The JCW base
/// class keeps the managed peer alive for the JVM peer's lifetime.</para>
/// </summary>
[Register("net/compose/ManagedBox")]
internal sealed class ManagedBox : Java.Lang.Object
{
    public object? Value { get; }

    public ManagedBox(object? value) => Value = value;

    public override bool Equals(Java.Lang.Object? obj) =>
        obj is ManagedBox other &&
        EqualityComparer<object?>.Default.Equals(Value, other.Value);

    public override int GetHashCode() =>
        Value is null ? 0 : Value.GetHashCode();

    public override string ToString() =>
        Value?.ToString() ?? "null";
}
