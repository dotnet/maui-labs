using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>Scaffold</c>. Hosts a primary <see cref="Body"/> with
/// optional Material chrome — top app bar, bottom bar, snackbar host,
/// and floating action button — pinned to their conventional edges of
/// the screen.
///
/// The Kotlin overload is stripped from the binding because
/// <c>floatingActionButtonPosition</c> (<c>FabPosition</c>),
/// <c>containerColor</c> and <c>contentColor</c> (<c>Color</c>) are
/// <c>@JvmInline value class</c> parameters; we call it through a JNI
/// bridge in <see cref="ComposeBridges"/> against the mangled name
/// <c>Scaffold-TvnljyQ</c>.
///
/// <code>
/// new Scaffold
/// {
///     TopBar    = new Text("My App"),
///     BottomBar = new NavigationBar { ... },
///     Body      = tabContent,
/// }
/// </code>
/// </summary>
public sealed class Scaffold : ComposableNode
{
    /// <summary>Optional: persistent top app bar slot.</summary>
    public ComposableNode? TopBar { get; set; }

    /// <summary>Optional: persistent bottom bar slot (e.g. <see cref="NavigationBar"/>).</summary>
    public ComposableNode? BottomBar { get; set; }

    /// <summary>Optional: snackbar host slot, typically anchored above <see cref="BottomBar"/>.</summary>
    public ComposableNode? SnackbarHost { get; set; }

    /// <summary>Optional: floating action button slot.</summary>
    public ComposableNode? FloatingActionButton { get; set; }

    /// <summary>Required: the main body, laid out under the top bar and above the bottom bar.</summary>
    /// <remarks>
    /// The scaffold-supplied <c>PaddingValues</c> is forwarded to this
    /// node via the internal
    /// <see cref="ComposableNode.Render(IComposer, IntPtr)"/> overload —
    /// the default impl on <see cref="ComposableNode"/> applies it as
    /// a <c>Modifier.padding</c> on the body's frame; <see cref="LazyColumn{T}"/>
    /// (and the other lazy containers) override it to route the handle
    /// into their Kotlin <c>contentPadding:</c> argument so items scroll
    /// under the bars. When the body sits behind a transparent wrapper
    /// (e.g. <see cref="PullToRefreshBox"/>) and you need the inner
    /// scrollable to receive the padding directly, prefer
    /// <see cref="BodyContent"/> instead — it hands you the
    /// <see cref="PaddingValues"/> explicitly the way Kotlin's
    /// <c>content: @Composable (PaddingValues) -&gt; Unit</c> does.
    /// </remarks>
    public ComposableNode? Body { get; set; }

    /// <summary>
    /// Explicit body lambda that receives the scaffold-supplied
    /// <see cref="PaddingValues"/> so the caller can route it into the
    /// scrollable of their choice. Mirrors Kotlin's
    /// <c>content: @Composable (PaddingValues) -&gt; Unit</c> slot
    /// exactly. Use this when the body is a transparent wrapper around
    /// a scrollable (e.g. <see cref="PullToRefreshBox"/> wrapping a
    /// <see cref="LazyColumn{T}"/>) and you want the items to scroll
    /// under the top / bottom bars instead of stopping at them.
    /// </summary>
    /// <remarks>
    /// <code>
    /// new Scaffold
    /// {
    ///     TopBar      = ...,
    ///     BottomBar   = ...,
    ///     BodyContent = padding =&gt; new PullToRefreshBox(...)
    ///     {
    ///         new LazyColumn&lt;Row&gt;(items, itemContent: r =&gt; ...)
    ///         {
    ///             ContentPadding = padding,
    ///             Modifier       = Modifier.FillMaxSize(),
    ///         },
    ///     },
    /// }
    /// </code>
    /// The <see cref="PaddingValues"/> passed to the lambda is only
    /// valid for the synchronous duration of the call — assign it to
    /// <see cref="LazyColumn{T}.ContentPadding"/> on a node rendered
    /// inside the same lambda; don't capture it into long-lived state.
    /// When both <see cref="Body"/> and <see cref="BodyContent"/> are
    /// set, <see cref="BodyContent"/> wins and <see cref="Body"/> is
    /// ignored.
    /// </remarks>
    public Func<PaddingValues, ComposableNode>? BodyContent { get; set; }

    public override void Render(IComposer composer)
    {
        if (Body is null && BodyContent is null)
            throw new InvalidOperationException(
                "Scaffold.Body or Scaffold.BodyContent is required (the Kotlin parameter has no default).");

        // Material 3's Scaffold passes PaddingValues as the first arg of
        // its content lambda. Two paths:
        //
        //  * BodyContent (preferred when the inner scrollable wants
        //    explicit control — e.g. PullToRefreshBox around a
        //    LazyColumn): mirror Kotlin 1:1, hand the caller a managed
        //    PaddingValues wrapper to plumb wherever they want.
        //  * Body (the simple case): forward the raw handle directly
        //    through ComposableNode.Render(IComposer, IntPtr) so body's
        //    BuildModifier can prepend a `Modifier.padding(values)` op
        //    via JNI without allocating a managed Modifier wrapper per
        //    measure pass (issue #46). ScaffoldLayout is a
        //    SubcomposeLayout — this lambda runs on every measure pass,
        //    so the saved allocation adds up quickly during scrolls,
        //    rotations, and IME animations.
        Kotlin.Jvm.Functions.IFunction3 content;
        if (BodyContent is not null)
        {
            var builder = BodyContent;
            content = ComposableLambdas.Wrap3(composer, (paddingHandle, c) =>
            {
                if (paddingHandle == IntPtr.Zero)
                {
                    builder(null!)?.Render(c);
                    return;
                }
                // PaddingValues.Wrap with DoNotTransfer — the handle is
                // owned by the Kotlin Scaffold for the duration of this
                // lambda; we just borrow it.
                var managed = PaddingValues.Wrap(paddingHandle);
                builder(managed)?.Render(c);
            });
        }
        else
        {
            var body = Body!;
            content = ComposableLambdas.Wrap3(composer, (paddingHandle, c) =>
                body.Render(c, paddingHandle));
        }

        // Always pass non-null slot lambdas. Toggling between Compose's
        // default `{}` (a synthetic Java SAM lambda) and our IFunction2
        // identity confuses M3's internal `rememberComposableLambda`,
        // which casts the slot value to ComposableLambdaImpl on every
        // recomposition — a flip in slot type triggers a runtime
        // ClassCastException. Emit nothing when the user didn't supply
        // a slot; functionally identical to M3's `{}` default.
        var topBar = ComposableLambdas.Wrap2(composer, c => TopBar?.Render(c));
        var bottomBar = ComposableLambdas.Wrap2(composer, c => BottomBar?.Render(c));
        var snackbarHost = ComposableLambdas.Wrap2(composer, c => SnackbarHost?.Render(c));
        var fab = ComposableLambdas.Wrap2(composer, c => FloatingActionButton?.Render(c));

        int defaults = (int)ScaffoldDefault.All;
        var modifier = BuildModifier();
        if (modifier is not null) defaults &= ~(int)ScaffoldDefault.Modifier;
        defaults &= ~(int)ScaffoldDefault.TopBar;
        defaults &= ~(int)ScaffoldDefault.BottomBar;
        defaults &= ~(int)ScaffoldDefault.SnackbarHost;
        defaults &= ~(int)ScaffoldDefault.FloatingActionButton;

        ComposeBridges.Scaffold(
            modifier:             modifier,
            topBar:               topBar,
            bottomBar:            bottomBar,
            snackbarHost:         snackbarHost,
            floatingActionButton: fab,
            content:              content,
            defaults:             defaults,
            composer:             composer);
    }
}
