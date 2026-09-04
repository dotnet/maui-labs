using AndroidX.Compose.Material3;

namespace AndroidX.Compose;

/// <summary>
/// Caller-supplied state holder for <see cref="ModalNavigationDrawer"/>
/// and <see cref="DismissibleNavigationDrawer"/>. Wraps Kotlin's
/// <c>DrawerState</c> (created via <c>rememberDrawerState</c>) so a
/// facade can carry an initial <see cref="DrawerValue"/> across
/// recompositions and expose the live drawer position back to C#.
/// </summary>
/// <remarks>
/// <para>This type is named with a <c>Holder</c> suffix to avoid
/// colliding with the binding's
/// <see cref="DrawerState"/> class — both
/// would otherwise resolve to <c>DrawerState</c> when a user imports
/// <c>using AndroidX.Compose.Material3;</c> and
/// <c>using AndroidX.Compose;</c> at the same time.</para>
/// <para>Construct one inside <c>Remember</c> so the same instance
/// survives recomposition:</para>
/// <code>
/// var drawer = Remember(() =&gt; new DrawerStateHolder(DrawerValue.Closed));
///
/// new ModalNavigationDrawer(drawerState: drawer)
/// {
///     Drawer  = new ModalDrawerSheet { … },
///     Content = new Column { … },
///     ConfirmStateChange = v =&gt; !formIsDirty || v != DrawerValue.Closed,
/// };
/// </code>
/// </remarks>
public sealed class DrawerStateHolder
{
    internal DrawerState? Jvm;

    /// <summary>
    /// Initial <see cref="DrawerValue"/> the drawer remembers on first
    /// composition. Defaults to <see cref="DrawerValue.Closed"/>.
    /// </summary>
    public DrawerValue InitialValue { get; }

    /// <summary>
    /// Construct a holder with the given <paramref name="initialValue"/>
    /// (or <see cref="DrawerValue.Closed"/> when omitted).
    /// </summary>
    public DrawerStateHolder(DrawerValue? initialValue = null)
    {
        InitialValue = initialValue ?? DrawerValue.Closed!;
    }

    /// <summary>
    /// The drawer's current visual state. Falls back to
    /// <see cref="InitialValue"/> until the holder is bound to a live
    /// peer (i.e. the first render has happened).
    /// </summary>
    public DrawerValue CurrentValue => Jvm?.CurrentValue ?? InitialValue;

    /// <summary>
    /// The drawer's target state during animation, or the resting
    /// state otherwise. Falls back to <see cref="InitialValue"/> until
    /// bound.
    /// </summary>
    public DrawerValue TargetValue => Jvm?.TargetValue ?? InitialValue;

    /// <summary>
    /// <c>true</c> when the drawer is fully open. Equivalent to
    /// <c>CurrentValue == DrawerValue.Open</c>.
    /// </summary>
    public bool IsOpen => Jvm?.IsOpen ?? (InitialValue == DrawerValue.Open);

    /// <summary>
    /// <c>true</c> when the drawer is fully closed. Equivalent to
    /// <c>CurrentValue == DrawerValue.Closed</c>.
    /// </summary>
    public bool IsClosed => Jvm?.IsClosed ?? (InitialValue == DrawerValue.Closed);

    /// <summary>
    /// Slide the drawer open with the default animation. Mirrors
    /// Kotlin's <c>DrawerState.open()</c>. Safe to call from a button
    /// <c>onClick</c> — the returned <see cref="Task"/> completes when
    /// the animation lands. Throws
    /// <see cref="InvalidOperationException"/> if invoked before
    /// the holder is bound to a live peer (i.e. before the first
    /// composition pass that renders the
    /// <see cref="ModalNavigationDrawer"/> facade).
    /// </summary>
    public Task OpenAsync()
    {
        var jvm = Jvm
            ?? throw new InvalidOperationException(
                "DrawerStateHolder.OpenAsync requires the holder to be bound to a live drawer; call it after the first render.");
        return SuspendBridge.Invoke(cont =>
            ComposeBridges.DrawerStateOpen(((Java.Lang.Object)jvm).Handle, cont));
    }

    /// <summary>
    /// Slide the drawer closed with the default animation. Mirrors
    /// Kotlin's <c>DrawerState.close()</c>. See
    /// <see cref="OpenAsync"/> for the binding caveat.
    /// </summary>
    public Task CloseAsync()
    {
        var jvm = Jvm
            ?? throw new InvalidOperationException(
                "DrawerStateHolder.CloseAsync requires the holder to be bound to a live drawer; call it after the first render.");
        return SuspendBridge.Invoke(cont =>
            ComposeBridges.DrawerStateClose(((Java.Lang.Object)jvm).Handle, cont));
    }
}
