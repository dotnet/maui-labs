using AndroidX.Compose.Material3;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>BottomSheetScaffold</c>. Hosts a persistent bottom
/// sheet alongside a primary content area, plus optional top bar and
/// snackbar slots. Pass an optional <see cref="SheetStateHolder"/> to
/// configure the underlying <c>SheetState</c>; set
/// <see cref="ConfirmValueChange"/> to veto user-initiated transitions.
/// </summary>
/// <remarks>
/// Stays hand-written: the Kotlin signature has two non-nullable
/// <c>@Composable</c> lambdas (<c>sheetContent</c> and <c>content</c>),
/// which the source generator's hybrid-container path doesn't yet
/// support. The new <see cref="SheetStateHolder"/> /
/// <see cref="ConfirmValueChange"/> wiring mirrors the generator's
/// <c>[StateHolder] + [ConfirmStateChange]</c> contract so a future
/// migration is mechanical.
///
/// <code>
/// var sheet = Remember(() =&gt; new SheetStateHolder(skipPartiallyExpanded: false));
/// new BottomSheetScaffold(sheetState: sheet)
/// {
///     ConfirmValueChange = v =&gt; v != SheetValue.Hidden,
///     SheetContent = new Column { new Text("Sheet") },
///     TopBar       = new TopAppBar { Title = new Text("App") },
///     new Text("Main content"),
/// }
/// </code>
/// </remarks>
public sealed class BottomSheetScaffold : ComposableContainer
{
    readonly SheetStateHolder? _sheetState;

    // One JCW per scaffold instance — its JNI identity is part of the
    // Kotlin `remember` cache key, so allocating fresh each render
    // would invalidate the cached state holder. Callback is read on
    // every Invoke, so callers can mutate ConfirmValueChange freely.
    readonly SheetValueConfirmStateChange _confirmValueChangeAdapter = new();

    /// <summary>
    /// Construct the scaffold. Pass a <paramref name="sheetState"/>
    /// to opt into the <c>skipPartiallyExpanded</c> setting and gain
    /// imperative control via <see cref="SheetStateHolder.ShowAsync"/>
    /// / <see cref="SheetStateHolder.HideAsync"/>.
    /// </summary>
    public BottomSheetScaffold(SheetStateHolder? sheetState = null)
    {
        _sheetState = sheetState;
    }

    /// <summary>Required: the persistent bottom-sheet content.</summary>
    public ComposableNode? SheetContent { get; set; }

    /// <summary>Optional: the sheet's drag handle.</summary>
    public ComposableNode? SheetDragHandle { get; set; }

    /// <summary>Optional: persistent top bar above the main content.</summary>
    public ComposableNode? TopBar { get; set; }

    /// <summary>
    /// Veto callback consulted on every sheet transition. Return
    /// <c>false</c> to refuse the transition (gesture or programmatic).
    /// <c>null</c> behaves as Kotlin's default <c>{ true }</c> —
    /// every transition allowed.
    /// </summary>
    public Func<SheetValue, bool>? ConfirmValueChange { get; set; }

    public override void Render(IComposer composer)
    {
        if (SheetContent is null)
            throw new InvalidOperationException(
                "BottomSheetScaffold.SheetContent is required.");

        // Update the JCW callback BEFORE the Remember calls. Compose
        // captures the IFunction1's JNI peer at first composition; we
        // keep the peer's identity stable but read Callback fresh on
        // every Invoke so the user can mutate ConfirmValueChange.
        _confirmValueChangeAdapter.Callback = ConfirmValueChange;

        SheetState? sheetState = null;
        if (_sheetState is not null || ConfirmValueChange is not null)
        {
            // Always call RememberStandardBottomSheetState on every
            // recomposition — Kotlin's slot-table-keyed remember{}
            // returns the same instance, but skipping the call would
            // shift slot positions for the RememberBottomSheetScaffoldState
            // call that follows. Cache identity at the wrapper level
            // (Jvm field) but keep the call shape stable.
            sheetState = BottomSheetScaffoldKt.RememberStandardBottomSheetState(
                initialValue:        SheetValue.PartiallyExpanded,
                confirmValueChange:  _confirmValueChangeAdapter,
                skipHiddenState:     true,
                _composer:           composer,
                p4:                  0,
                _changed:            0);
            if (_sheetState is not null)
                _sheetState.Jvm = sheetState;
        }

        // Bound C# call — RememberBottomSheetScaffoldState is NOT stripped.
        // _changed (= Kotlin's $default) bit 0 = bottomSheetState defaulted,
        // bit 1 = snackbarHostState defaulted. We never wire snackbar so
        // bit 1 stays set; bit 0 clears only when we provided a sheetState.
        var changed = sheetState is null ? 3 : 2;
        var scaffoldState = BottomSheetScaffoldKt.RememberBottomSheetScaffoldState(
            bottomSheetState:   sheetState,
            snackbarHostState:  null,
            _composer:          composer,
            p3:                 0,
            _changed:           changed);

        var sheet   = ComposableLambdas.Wrap3(composer, c => SheetContent.Render(c));
        var content = ComposableLambdas.Wrap3(composer, c => RenderChildren(c));

        var dragHandle = SheetDragHandle is null ? null
            : ComposableLambdas.Wrap2(composer, c => SheetDragHandle.Render(c));
        var topBar = TopBar is null ? null
            : ComposableLambdas.Wrap2(composer, c => TopBar.Render(c));

        int defaults = (int)BottomSheetScaffoldDefault.All;
        var modifier = BuildModifier();
        if (modifier   is not null) defaults &= ~(int)BottomSheetScaffoldDefault.Modifier;
        if (dragHandle is not null) defaults &= ~(int)BottomSheetScaffoldDefault.SheetDragHandle;
        if (topBar     is not null) defaults &= ~(int)BottomSheetScaffoldDefault.TopBar;

        ComposeBridges.BottomSheetScaffold(
            sheetContent:    sheet,
            modifier:        modifier,
            scaffoldState:   ((Java.Lang.Object)scaffoldState).Handle,
            sheetDragHandle: dragHandle,
            topBar:          topBar,
            snackbarHost:    null,
            content:         content,
            defaults:        defaults,
            composer:        composer);
    }
}
