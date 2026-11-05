using AndroidX.Compose.Material3;

namespace AndroidX.Compose;

/// <summary>
/// Caller-supplied state holder for <see cref="ModalWideNavigationRail"/>.
/// Wraps Kotlin's <c>WideNavigationRailState</c> (created via
/// <c>rememberWideNavigationRailState</c>) so a facade can carry an
/// initial <see cref="WideNavigationRailValue"/> across recompositions.
/// </summary>
/// <remarks>
/// Construct inside <c>Remember</c> so the same instance survives
/// recomposition:
/// <code>
/// var rail = Remember(() =&gt; new WideNavigationRailState(WideNavigationRailValue.Expanded));
/// new ModalWideNavigationRail(state: rail) { … };
/// </code>
/// </remarks>
public sealed class WideNavigationRailState
{
    internal IWideNavigationRailState? Jvm;

    /// <summary>
    /// Initial <see cref="WideNavigationRailValue"/> the rail
    /// remembers on first composition. Defaults to
    /// <see cref="WideNavigationRailValue.Expanded"/> so the rail
    /// pops open as soon as it mounts (matches the modal-overlay
    /// pattern documented on <see cref="ModalWideNavigationRail"/>).
    /// </summary>
    public WideNavigationRailValue InitialValue { get; }

    /// <summary>
    /// Construct a holder with the given <paramref name="initialValue"/>
    /// (or <see cref="WideNavigationRailValue.Expanded"/> when omitted).
    /// </summary>
    public WideNavigationRailState(WideNavigationRailValue? initialValue = null)
    {
        InitialValue = initialValue ?? WideNavigationRailValue.Expanded!;
    }

    /// <summary>
    /// The rail's current visual state. Falls back to
    /// <see cref="InitialValue"/> until the holder is bound to a live
    /// peer (i.e. the first render has happened).
    /// </summary>
    public WideNavigationRailValue CurrentValue =>
        Jvm?.CurrentValue ?? InitialValue;

    /// <summary>
    /// The rail's target state during animation, or the resting state
    /// otherwise. Falls back to <see cref="InitialValue"/> until bound.
    /// </summary>
    public WideNavigationRailValue TargetValue =>
        Jvm?.TargetValue ?? InitialValue;
}
