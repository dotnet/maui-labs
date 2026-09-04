using Android.Runtime;
using AndroidX.Compose.Foundation.Lazy;
using AndroidX.Compose.Foundation.Layout;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>LazyColumn</c> — the lazy equivalent of <see cref="Column"/>.
/// Only the items currently visible (plus a small lookahead) are
/// composed and laid out, so it's the right choice any time the child
/// count exceeds what fits on screen.
///
/// The facade is data-driven instead of collection-init: you pass the
/// items list and a per-item content callback at construction time.
/// Compose handles scrolling automatically — no scroll-state plumbing
/// required for the common case.
///
/// <code>
/// new LazyColumn&lt;int&gt;(
///     items: Enumerable.Range(0, 1000).ToList(),
///     itemContent: i =&gt; new Text($"Row {i}"))
/// </code>
///
/// Set <see cref="State"/> (typically the result of a remembered
/// <see cref="LazyListState"/>) to read scroll position or drive
/// programmatic scrolling. When left null Compose creates its own
/// internal state via the Kotlin default <c>rememberLazyListState()</c>.
///
/// When placed as the body of a <see cref="Scaffold"/>, the
/// scaffold-supplied <c>PaddingValues</c> is forwarded into Kotlin's
/// own <c>contentPadding</c> parameter (NOT applied as
/// <c>Modifier.padding</c>) so the list itself fills the scaffold body
/// — items scroll under top app bars, bottom app bars, and system
/// chrome (gesture nav) like upstream M3 templates, and the first /
/// last items remain reachable above and below them.
/// </summary>
public sealed class LazyColumn<T> : ComposableNode
{
    readonly IReadOnlyList<T> _items;
    readonly Func<T, ComposableNode> _itemContent;
    IPaddingValues? _runtimeContentPadding;

    public LazyColumn(IReadOnlyList<T> items, Func<T, ComposableNode> itemContent)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items       = items;
        ArgumentNullException.ThrowIfNull(itemContent);
        _itemContent = itemContent;
    }

    /// <summary>
    /// Optional scroll state. Leave null to let Compose substitute its
    /// own remembered state.
    /// </summary>
    public LazyListState? State { get; set; }

    /// <summary>
    /// When <see langword="true"/>, items are stacked from the bottom up
    /// — the first item rendered sits at the bottom of the viewport and
    /// scroll offset 0 corresponds to the end of the list. Mirrors
    /// Kotlin's <c>reverseLayout</c> parameter. Defaults to
    /// <see langword="false"/> (normal top-down layout).
    /// </summary>
    /// <remarks>
    /// Reverse layout is the canonical pattern for chat / message
    /// timelines where newly appended items should appear at the bottom
    /// and the scroll position should pin to the latest message. Pair
    /// with <c>items.Reverse()</c> at the data layer if your underlying
    /// collection is in chronological order — Compose lays items out
    /// bottom-first when this flag is set.
    /// </remarks>
    public bool ReverseLayout { get; set; }

    /// <summary>
    /// Optional fixed content padding applied inside the list (not as a
    /// modifier on the list frame). Items can scroll behind this area
    /// but the first/last items stay fully reachable. Takes precedence
    /// over a <see cref="Scaffold"/>-supplied <c>PaddingValues</c> when
    /// both are present.
    /// </summary>
    public PaddingValues? ContentPadding { get; set; }

    /// <summary>
    /// When a parent layout (typically <see cref="Scaffold"/>) hands us
    /// a runtime <c>PaddingValues</c>, route it into LazyColumn's own
    /// <c>contentPadding:</c> parameter instead of letting the base
    /// implementation prepend a <c>Modifier.padding</c> op. This gives
    /// the "items scroll under the top/bottom bars" behavior Material's
    /// templates rely on while keeping the first and last item
    /// reachable above and below the chrome.
    /// </summary>
    internal override void Render(IComposer composer, IntPtr paddingHandle)
    {
        if (paddingHandle == IntPtr.Zero)
        {
            Render(composer);
            return;
        }

        // DoNotTransfer because the Scaffold content lambda owns the
        // local ref for the duration of this call. We wrap, JavaCast to
        // the interface, then dispose immediately — the cast holds its
        // own peer reference, so the temporary Java.Lang.Object wrapper
        // doesn't need to outlive this method.
        IPaddingValues pv;
        using (var pvObj = new Java.Lang.Object(paddingHandle, JniHandleOwnership.DoNotTransfer))
            pv = pvObj.JavaCast<IPaddingValues>();

        var prev = _runtimeContentPadding;
        _runtimeContentPadding = pv;
        try
        {
            Render(composer);
        }
        finally
        {
            _runtimeContentPadding = prev;
        }
    }

    public override void Render(IComposer composer)
    {
        var modifier = BuildModifier();
        var content  = new ComposableLambda1(scopeObj =>
        {
            var scope = Android.Runtime.Extensions.JavaCast<ILazyListScope>(scopeObj!);
            // contentType is non-nullable in Kotlin (defaults to { null }),
            // so we always provide a stub; key is nullable and we pass null
            // to keep the Compose-default positional keying.
            // itemContent IS @Composable but runs at measure time inside the
            // lazy list's SubcomposeLayout, so route through Instantiate4
            // (composer-less ComposableLambdaInstance) — Wrap4 would crash
            // because there's no active composer once the DSL builder runs.
            scope.Items(
                _items.Count,
                key:         null,
                contentType: new ComposableLambda1(_ => { }),
                itemContent: ComposableLambdas.Instantiate4((_, indexBoxed, comp) =>
                {
                    var i = ((Java.Lang.Integer)indexBoxed!).IntValue();
                    _itemContent(_items[i]).Render(comp);
                }));
        });

        var contentPadding = ContentPadding?.Jvm ?? _runtimeContentPadding;

        int defaults = (int)LazyColumnDefault.All;
        if (modifier       is not null) defaults &= ~(int)LazyColumnDefault.Modifier;
        if (State          is not null) defaults &= ~(int)LazyColumnDefault.State;
        if (contentPadding is not null) defaults &= ~(int)LazyColumnDefault.ContentPadding;
        // Bool params lower to a single $default bit. Kotlin's default
        // is false, so only clear the bit when the caller asked for
        // true — otherwise let Kotlin substitute its own false and
        // save the explicit-pass round trip.
        if (ReverseLayout)              defaults &= ~(int)LazyColumnDefault.ReverseLayout;

        LazyDslKt.LazyColumn(
            modifier:            modifier,
            state:               State?.Jvm,
            contentPadding:      contentPadding,
            reverseLayout:       ReverseLayout,
            verticalArrangement: null,
            horizontalAlignment: null,
            flingBehavior:       null,
            userScrollEnabled:   true,
            overscrollEffect:    null,
            content:             content,
            _composer:           composer,
            p11:                 0,
            _changed:            defaults);
    }
}
