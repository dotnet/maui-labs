using Android.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// Function0&lt;Integer&gt; — a no-arg Kotlin lambda returning a boxed
/// <see cref="Java.Lang.Integer"/>. Used for state-holder factories
/// like <c>rememberCarouselState(itemCount: () -&gt; Int)</c> /
/// <c>rememberLazyListState(initialFirstVisibleItemIndex: () -&gt; Int)</c>
/// that re-read the count on every measure pass.
///
/// Separate from <see cref="ComposableLambda0"/>, which returns
/// <c>Kotlin.Unit</c> (the JNI shape for onClick-style callbacks).
/// </summary>
[Register("net/compose/ComposableLambda0Int")]
internal sealed class ComposableLambda0Int : Java.Lang.Object, IFunction0
{
    readonly Func<int> _body;
    public ComposableLambda0Int(Func<int> body) => _body = body;

    public Java.Lang.Object Invoke() => Java.Lang.Integer.ValueOf(_body());
}
