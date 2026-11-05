using Android.Runtime;
using AndroidX.Navigation;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// Function1&lt;NavGraphBuilder, Unit&gt; — the <c>builder</c> argument
/// to <c>NavHostKt.NavHost(navController, startDestination, modifier,
/// route, builder, ...)</c>. NOT a @Composable lambda: NavHost invokes
/// it synchronously during graph construction, before any destination
/// is composed, so it must NOT be wrapped via <c>ComposableLambda</c>
/// or <c>ComposableLambdaInstance</c> — those inject the
/// <c>startRestartGroup</c>/<c>endRestartGroup</c> machinery used by
/// real composables and would corrupt the surrounding composition.
///
/// <para>The body is a plain <see cref="Action{NavGraphBuilder}"/>
/// that registers each route via Kotlin's <c>composable()</c> extension
/// (routed through <c>ComposeBridges.NavGraphBuilderComposable</c>).
/// </para>
/// </summary>
[Register("net/compose/NavGraphBuilderLambda")]
internal sealed class NavGraphBuilderLambda : Java.Lang.Object, IFunction1
{
    readonly Action<NavGraphBuilder> _body;

    public NavGraphBuilderLambda(Action<NavGraphBuilder> body) => _body = body;

    // Kotlin Function1<NavGraphBuilder, Unit> contractually returns
    // Unit.INSTANCE — see ComposableLambda0/1 for the rationale.
    public Java.Lang.Object Invoke(Java.Lang.Object? p0)
    {
        ArgumentNullException.ThrowIfNull(p0);
        var graphBuilder = Android.Runtime.Extensions.JavaCast<NavGraphBuilder>(p0);
        _body(graphBuilder);
        return Kotlin.Unit.Instance!;
    }
}
