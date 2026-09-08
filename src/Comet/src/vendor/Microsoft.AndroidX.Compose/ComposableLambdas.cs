using System.Runtime.CompilerServices;
using AndroidX.Compose.Runtime;
using AndroidX.Compose.Runtime.Internal;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

/// <summary>
/// Helpers that wrap <see cref="ComposableLambda2"/> /
/// <see cref="ComposableLambda3"/> / <see cref="ComposableLambda4"/>
/// @Composable content lambdas through one of Compose's two factory
/// functions so the runtime owns lambda identity across recompositions.
///
/// <para>Two factories exist and they are not interchangeable:</para>
/// <list type="bullet">
///   <item><description><c>ComposableLambdaKt.ComposableLambda(composer,
///     key, tracked, block)</c> — used by <see cref="Wrap2"/> /
///     <see cref="Wrap3(IComposer, Action{IComposer}, int, string)"/>.
///     Stores/retrieves the wrapper from the current composition's slot
///     table at the given key; requires an *active* composer. Use for
///     content slots that are evaluated synchronously during the outer
///     composition pass (e.g. <c>topBar</c>, <c>title</c>, button
///     content).</description></item>
///   <item><description><c>ComposableLambdaKt.ComposableLambdaInstance(
///     key, tracked, block)</c> — used by <see cref="Instantiate4"/> and
///     by <c>ComposeExtensions.SetContent</c>. Allocates a fresh wrapper
///     each call without touching any composer; safe to invoke from
///     outside composition. Use for content that runs at measure time
///     or in a subcomposition (e.g. <c>LazyListScope.items</c> /
///     <c>LazyGridScope.items</c> <c>itemContent</c>, which Compose
///     realizes inside the lazy list's
///     <c>rememberLazyListItemProviderLambda</c> long after the outer
///     <c>Render</c> has returned).</description></item>
/// </list>
///
/// <para><b>Convention:</b> never construct
/// <see cref="ComposableLambda2"/> / <see cref="ComposableLambda3"/> /
/// <see cref="ComposableLambda4"/> directly inside a <c>Render</c>
/// method — always route through <see cref="Wrap2"/> /
/// <see cref="Wrap3(IComposer, Action{IComposer}, int, string)"/> /
/// <see cref="Instantiate4"/>. The non-@Composable adapters
/// <see cref="ComposableLambda0"/> / <see cref="ComposableLambda1"/>
/// (onClick / onValueChange callbacks, plus the LazyListScope /
/// LazyGridScope DSL builders which are *not* @Composable) are NOT
/// wrapped — they run outside composition and would crash inside
/// Compose's restart-group machinery.</para>
/// </summary>
internal static class ComposableLambdas
{
    /// <summary>
    /// Wrap an <see cref="Action{IComposer}"/> as an identity-stable
    /// <see cref="IFunction2"/> (the Function2&lt;Composer, Int, Unit&gt;
    /// @Composable content-slot shape used by <c>topBar</c>,
    /// <c>bottomBar</c>, <c>title</c>, <c>icon</c>, etc.).
    /// </summary>
    public static IFunction2 Wrap2(
        IComposer composer,
        Action<IComposer> body,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => (IFunction2)ComposableLambdaKt.ComposableLambda(
            composer, SourceLocationKey.Compute(line, file), tracked: true,
            block: new ComposableLambda2(body));

    /// <summary>
    /// Wrap an <see cref="Action{IComposer}"/> as an identity-stable
    /// <see cref="IFunction3"/> (the Function3&lt;Scope, Composer, Int, Unit&gt;
    /// @Composable content-slot shape used by <c>Column</c> / <c>Row</c> /
    /// <c>Box</c> / <c>Button</c> bodies). The scope receiver is discarded.
    /// </summary>
    public static IFunction3 Wrap3(
        IComposer composer,
        Action<IComposer> body,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => (IFunction3)ComposableLambdaKt.ComposableLambda(
            composer, SourceLocationKey.Compute(line, file), tracked: true,
            block: new ComposableLambda3(body));

    /// <summary>
    /// Wrap an <see cref="Action{IntPtr, IComposer}"/> as an
    /// identity-stable <see cref="IFunction3"/> that receives the raw
    /// scope handle (RowScope, ColumnScope, SegmentedButtonRowScope,
    /// etc.) — for containers whose children need to forward the scope
    /// to a scope-extension Kotlin static (e.g.
    /// <c>RowScope.NavigationBarItem</c>).
    /// </summary>
    public static IFunction3 Wrap3(
        IComposer composer,
        Action<IntPtr, IComposer> body,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => (IFunction3)ComposableLambdaKt.ComposableLambda(
            composer, SourceLocationKey.Compute(line, file), tracked: true,
            block: new ComposableLambda3(body));

    /// <summary>
    /// Wrap an <see cref="Action{IComposer}"/> as an identity-stable
    /// <see cref="IFunction3"/> that receives the boxed <c>p0</c> as a
    /// <see cref="Java.Lang.Object"/> — the value-typed Function3 shape
    /// used by <c>Crossfade</c>'s
    /// <c>content: @Composable (T) -&gt; Unit</c>, where <c>p0</c> is
    /// the boxed targetState (not a scope receiver). The body unboxes
    /// it back to <c>T</c>.
    /// </summary>
    /// <remarks>
    /// Distinct method name (rather than a third <c>Wrap3</c>
    /// overload) so existing call sites that pass
    /// <c>(_, c) =&gt; ...</c> aren't ambiguous between the
    /// <c>Action&lt;IntPtr, IComposer&gt;</c> and
    /// <c>Action&lt;Java.Lang.Object?, IComposer&gt;</c> shapes.
    /// </remarks>
    public static IFunction3 Wrap3WithValue(
        IComposer composer,
        Action<Java.Lang.Object?, IComposer> body,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => (IFunction3)ComposableLambdaKt.ComposableLambda(
            composer, SourceLocationKey.Compute(line, file), tracked: true,
            block: new ComposableLambda3(body));

    /// <summary>
    /// Build an identity-stable <see cref="IFunction4"/> wrapper for the
    /// <c>Function4&lt;LazyItemScope, Int, Composer, Int, Unit&gt;</c>
    /// @Composable shape used by <c>LazyListScope.items</c> and
    /// <c>LazyGridScope.items</c> <c>itemContent</c>. <c>p0</c> is the
    /// lazy item scope handle, <c>p1</c> is the boxed item index,
    /// <c>p2</c> is the composer.
    ///
    /// <para>Uses <c>ComposableLambdaInstance</c> (not
    /// <c>ComposableLambda</c>) because the lazy list DSL builder runs
    /// at measure time inside the list's
    /// <c>rememberLazyListItemProviderLambda</c>, *not* inside the outer
    /// <c>Render</c>'s composition pass. There is no active composer at
    /// the call site, so we can't look up a slot-table entry — the
    /// <c>Instance</c> factory allocates a fresh wrapper without one,
    /// which is exactly what Kotlin's inline
    /// <c>LazyListScope.items(...)</c> extension expands to.</para>
    /// </summary>
    public static IFunction4 Instantiate4(
        Action<IntPtr, Java.Lang.Object?, IComposer> body,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => (IFunction4)ComposableLambdaKt.ComposableLambdaInstance(
            key: SourceLocationKey.Compute(line, file), tracked: true,
            block: new ComposableLambda4(body));

    /// <summary>
    /// Build an identity-stable <see cref="IFunction3"/> wrapper for the
    /// <c>Function3&lt;NavBackStackEntry, Composer, Int, Unit&gt;</c>
    /// @Composable shape used by <c>NavHost</c>'s per-route
    /// <c>composable("route") { ... }</c> destination content. <c>p0</c>
    /// is the current <see cref="AndroidX.Navigation.NavBackStackEntry"/>
    /// handle, <c>p1</c> is the destination's composer, <c>p2</c> is
    /// <c>$changed</c>.
    ///
    /// <para>Uses <c>ComposableLambdaInstance</c> (not
    /// <c>ComposableLambda</c>) because the navigation builder DSL runs
    /// once at NavHost graph construction, but the destination's
    /// content lambda is invoked LATER inside the route's own
    /// subcomposition (every time the user navigates to the route, or
    /// the entry recomposes). The outer NavHost composer captured at
    /// graph-build time is no longer active by then, so we can't look
    /// up a slot-table entry — the <c>Instance</c> factory allocates a
    /// fresh wrapper without one, exactly mirroring what Kotlin's
    /// <c>composable("route") { ... }</c> expands to.</para>
    /// </summary>
    public static IFunction3 InstantiateNavComposable(
        Action<IntPtr, IComposer> body,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => (IFunction3)ComposableLambdaKt.ComposableLambdaInstance(
            key: HashCode.Combine(line, file), tracked: true,
            block: new ComposableLambda3(body));

    /// <summary>
    /// Wrap an <c>Action&lt;IntPtr, Java.Lang.Object?, IComposer&gt;</c>
    /// as an identity-stable <see cref="IFunction4"/> for the
    /// <c>Function4&lt;Scope, Int, Composer, Int, Unit&gt;</c> @Composable
    /// shape used by Material 3 carousel <c>content</c> slots
    /// (<c>HorizontalUncontainedCarousel</c>, etc.).
    /// <c>scope</c> is the raw scope receiver handle (e.g.
    /// <c>CarouselItemScope</c>), <c>indexBoxed</c> is the boxed
    /// <see cref="Java.Lang.Integer"/> item index, <c>composer</c> is
    /// the active composer for the item's composition.
    ///
    /// <para>Uses <c>ComposableLambda</c> (not
    /// <c>ComposableLambdaInstance</c>) because the carousel's
    /// <c>content</c> parameter is a direct @Composable lambda on the
    /// outer carousel call — the Kotlin compiler would wrap it via
    /// <c>composableLambda(composer, ...)</c> in the OUTER composition's
    /// slot table even though the lambda body itself eventually runs
    /// inside the carousel's pager subcomposition. Mirrors
    /// <see cref="Wrap2"/>/<see cref="Wrap3(IComposer, Action{IComposer}, int, string)"/>
    /// for the 4-arg shape.</para>
    /// </summary>
    public static IFunction4 Wrap4(
        IComposer composer,
        Action<IntPtr, Java.Lang.Object?, IComposer> body,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => (IFunction4)ComposableLambdaKt.ComposableLambda(
            composer, SourceLocationKey.Compute(line, file), tracked: true,
            block: new ComposableLambda4(body));
}
