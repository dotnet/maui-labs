using Android.Runtime;
using AndroidX.Lifecycle;

namespace AndroidX.Compose;

/// <summary>
/// Internal JCW adapter that exposes a <see cref="Func{T}"/> as an
/// <see cref="ViewModelProvider.IFactory"/> so
/// <see cref="ViewModelProvider"/> can construct
/// the view model the first time the host's
/// <see cref="ViewModelStore"/> doesn't already
/// contain the requested key.
/// </summary>
/// <remarks>
/// <para>
/// One factory instance is allocated per
/// <see cref="ComposeExtensions.ViewModel{T}(Func{T}, int, string)"/>
/// call. The factory is short-lived — Kotlin only retains a
/// reference to it for the duration of <c>get(key, modelClass)</c>,
/// after which the constructed VM is cached in the store and the
/// factory can be released.
/// </para>
/// <para>
/// <see cref="Create(Java.Lang.Class)"/> is the only callback Kotlin
/// invokes when no <c>CreationExtras</c>-aware overload is required.
/// Both shapes route to the wrapped <see cref="Func{T}"/>; the
/// supplied <see cref="Java.Lang.Class"/> is intentionally ignored
/// — the caller already committed to the C# generic <c>T</c> when
/// they called <see cref="ComposeExtensions.ViewModel{T}(Func{T}, int, string)"/>.
/// </para>
/// </remarks>
[Register("net/compose/LambdaViewModelFactory")]
sealed class LambdaViewModelFactory : Java.Lang.Object, ViewModelProvider.IFactory
{
    readonly Func<AndroidX.Lifecycle.ViewModel> _factory;

    public LambdaViewModelFactory(Func<AndroidX.Lifecycle.ViewModel> factory)
        => _factory = factory;

    /// <summary>
    /// Kotlin <c>fun &lt;T : ViewModel&gt; create(modelClass: Class&lt;T&gt;): T</c>
    /// — invokes the wrapped delegate and returns the new instance.
    /// </summary>
    public Java.Lang.Object Create(Java.Lang.Class modelClass)
        => _factory();
}
