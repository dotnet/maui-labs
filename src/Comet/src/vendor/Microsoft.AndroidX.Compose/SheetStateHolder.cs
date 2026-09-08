using AndroidX.Compose.Material3;

namespace AndroidX.Compose;

/// <summary>
/// Caller-supplied state holder for <see cref="ModalBottomSheet"/>.
/// Wraps Kotlin's <c>SheetState</c> (created via
/// <c>rememberModalBottomSheetState</c>) so a facade can carry an
/// initial <see cref="SkipPartiallyExpanded"/> setting across
/// recompositions and expose the live sheet position back to C#.
/// </summary>
/// <remarks>
/// <para>Construct one inside <c>Remember</c> so the same instance
/// survives recomposition:</para>
/// <code>
/// var sheet = Remember(() =&gt; new SheetStateHolder(skipPartiallyExpanded: true));
///
/// new ModalBottomSheet(onDismissRequest: () =&gt; show.Value = false, sheetState: sheet)
/// {
///     ConfirmValueChange = v =&gt; !formIsDirty || v != SheetValue.Hidden,
///     new Column { ... },
/// }
/// </code>
/// <para>Imperative open / close from C# uses the
/// <see cref="ShowAsync"/> / <see cref="HideAsync"/> /
/// <see cref="ExpandAsync"/> / <see cref="PartialExpandAsync"/>
/// helpers — the returned <see cref="Task"/> completes when the
/// animation lands.</para>
/// </remarks>
public sealed class SheetStateHolder
{
    internal SheetState? Jvm;

    /// <summary>
    /// When <c>true</c>, the sheet skips the half-expanded resting
    /// state and either hides fully or expands fully. Mirrors Kotlin
    /// <c>rememberModalBottomSheetState(skipPartiallyExpanded = …)</c>.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool SkipPartiallyExpanded { get; }

    /// <summary>
    /// Construct a holder, optionally opting out of the partially-
    /// expanded resting state.
    /// </summary>
    public SheetStateHolder(bool skipPartiallyExpanded = false)
    {
        SkipPartiallyExpanded = skipPartiallyExpanded;
    }

    /// <summary>
    /// The sheet's current <see cref="SheetValue"/>. Returns
    /// <see cref="SheetValue.Hidden"/> until the holder is bound to a
    /// live peer (i.e. the first render has happened).
    /// </summary>
    public SheetValue CurrentValue => Jvm?.CurrentValue ?? SheetValue.Hidden!;

    /// <summary>
    /// The sheet's target <see cref="SheetValue"/> during animation,
    /// or the resting state otherwise. Returns
    /// <see cref="SheetValue.Hidden"/> until bound.
    /// </summary>
    public SheetValue TargetValue => Jvm?.TargetValue ?? SheetValue.Hidden!;

    /// <summary>
    /// <c>true</c> when the sheet is non-hidden — equivalent to
    /// <c>CurrentValue != SheetValue.Hidden</c>. Returns <c>false</c>
    /// until bound.
    /// </summary>
    public bool IsVisible => Jvm?.IsVisible ?? false;

    /// <summary>
    /// <c>true</c> when the sheet's anchored set includes a fully-
    /// expanded resting position. Returns <c>false</c> until bound.
    /// </summary>
    public bool HasExpandedState => Jvm?.HasExpandedState ?? false;

    /// <summary>
    /// <c>true</c> when the sheet's anchored set includes a half-
    /// expanded resting position. Returns <c>false</c> until bound,
    /// or when <see cref="SkipPartiallyExpanded"/> is in effect.
    /// </summary>
    public bool HasPartiallyExpandedState => Jvm?.HasPartiallyExpandedState ?? false;

    /// <summary>
    /// Animate the sheet to the half-expanded state (or fully expand
    /// when partial expand is disabled). Mirrors Kotlin
    /// <c>SheetState.show()</c>. Throws
    /// <see cref="InvalidOperationException"/> if invoked before the
    /// holder is bound to a live peer.
    /// </summary>
    public Task ShowAsync(CancellationToken cancellationToken = default)
    {
        var jvm = RequireJvm(nameof(ShowAsync));
        return SuspendBridge.Invoke(cont =>
            ComposeBridges.SheetStateShow(((Java.Lang.Object)jvm).Handle, cont),
            cancellationToken);
    }

    /// <summary>
    /// Animate the sheet to <see cref="SheetValue.Hidden"/>. Mirrors
    /// Kotlin <c>SheetState.hide()</c>. See <see cref="ShowAsync"/>
    /// for the binding caveat.
    /// </summary>
    public Task HideAsync(CancellationToken cancellationToken = default)
    {
        var jvm = RequireJvm(nameof(HideAsync));
        return SuspendBridge.Invoke(cont =>
            ComposeBridges.SheetStateHide(((Java.Lang.Object)jvm).Handle, cont),
            cancellationToken);
    }

    /// <summary>
    /// Animate the sheet to the fully expanded state. Mirrors
    /// Kotlin <c>SheetState.expand()</c>. See <see cref="ShowAsync"/>
    /// for the binding caveat.
    /// </summary>
    public Task ExpandAsync(CancellationToken cancellationToken = default)
    {
        var jvm = RequireJvm(nameof(ExpandAsync));
        return SuspendBridge.Invoke(cont =>
            ComposeBridges.SheetStateExpand(((Java.Lang.Object)jvm).Handle, cont),
            cancellationToken);
    }

    /// <summary>
    /// Animate the sheet to <see cref="SheetValue.PartiallyExpanded"/>.
    /// No-op when the sheet was constructed with
    /// <see cref="SkipPartiallyExpanded"/> = <c>true</c>. Mirrors
    /// Kotlin <c>SheetState.partialExpand()</c>. See
    /// <see cref="ShowAsync"/> for the binding caveat.
    /// </summary>
    public Task PartialExpandAsync(CancellationToken cancellationToken = default)
    {
        var jvm = RequireJvm(nameof(PartialExpandAsync));
        return SuspendBridge.Invoke(cont =>
            ComposeBridges.SheetStatePartialExpand(((Java.Lang.Object)jvm).Handle, cont),
            cancellationToken);
    }

    SheetState RequireJvm(string member) =>
        Jvm ?? throw new InvalidOperationException(
            "SheetStateHolder." + member + " requires the holder to be bound to a live sheet; call it after the first render.");
}
