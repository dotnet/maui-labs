using Android.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// JCW that implements Kotlin's <c>Function0&lt;Object&gt;</c> — a
/// zero-argument lambda returning a boxed managed object. Unlike
/// <see cref="ComposableLambda0"/>, which returns <c>Unit.INSTANCE</c>
/// (void in Kotlin terms), this one returns a typed value Compose /
/// the binding can consume.
///
/// Used wherever a Kotlin API takes a <c>() -&gt; T</c> producer of
/// a non-<c>Unit</c> value, e.g. <see cref="ComposeExtensions.RememberSaveable{T}(Func{T}, int, string)"/>
/// for the value the saveable registry should cache.
/// </summary>
[Register("net/compose/ObjectFunction0")]
internal sealed class ObjectFunction0 : Java.Lang.Object, IFunction0
{
    readonly Func<Java.Lang.Object?> _factory;

    public ObjectFunction0(Func<Java.Lang.Object?> factory) =>
        _factory = factory;

    public Java.Lang.Object Invoke() => _factory()!;
}
