using Android.Content;
using Android.Runtime;
using Kotlin.Jvm.Functions;
using AView = Android.Views.View;

namespace AndroidX.Compose;

/// <summary>
/// <c>Function1&lt;Context, View&gt;</c> JCW used by
/// <see cref="AndroidView"/> as Compose's <c>factory</c> argument.
/// </summary>
[Register("net/compose/AndroidViewFactoryAdapter")]
internal sealed class AndroidViewFactoryAdapter : Java.Lang.Object, IFunction1
{
    readonly Func<Context, AView> _factory;

    public AndroidViewFactoryAdapter(Func<Context, AView> factory) => _factory = factory;

    public Java.Lang.Object Invoke(Java.Lang.Object? p0) =>
        _factory((Context)p0!);
}
