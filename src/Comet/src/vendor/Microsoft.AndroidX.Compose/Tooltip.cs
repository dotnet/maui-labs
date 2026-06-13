using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>TooltipBox</c> with a plain tooltip popup. The popup
/// position provider comes from <c>TooltipDefaults.rememberPlainTooltipPositionProvider</c>;
/// <c>TooltipState</c> from <c>rememberTooltipState</c>. Both are
/// resolved via JNI inside <see cref="ComposeBridges"/>.
///
/// Use named-property syntax: <see cref="Tip"/> is the popup body shown
/// on long-press / hover, <see cref="Anchor"/> is the always-visible
/// content the popup attaches to.
/// </summary>
public sealed class Tooltip : ComposableNode
{
    readonly bool _isPersistent;
    public Tooltip(bool isPersistent = false) => _isPersistent = isPersistent;

    /// <summary>Required: the popup body shown on long-press / hover.</summary>
    public ComposableNode? Tip { get; set; }

    /// <summary>Required: the always-visible anchor the tooltip attaches to.</summary>
    public ComposableNode? Anchor { get; set; }

    public override void Render(IComposer composer)
    {
        if (Tip is null || Anchor is null)
            throw new InvalidOperationException(
                "Tooltip requires both Tip (popup body) and Anchor (visible content).");

        var positionProvider = ComposeBridges.RememberPlainTooltipPositionProvider(composer);
        var stateHandle      = ComposeBridges.RememberTooltipState(_isPersistent, composer);

        var tooltip = ComposableLambdas.Wrap3(composer, c => Tip.Render(c));
        var anchor  = ComposableLambdas.Wrap2(composer, c => Anchor.Render(c));

        var modifier = BuildModifier();
        int defaults = (int)TooltipBoxDefault.All;
        if (modifier is not null) defaults &= ~(int)TooltipBoxDefault.Modifier;

        ComposeBridges.TooltipBox(
            positionProvider: positionProvider,
            tooltip:          tooltip,
            state:            stateHandle,
            modifier:         modifier,
            content:          anchor,
            defaults:         defaults,
            composer:         composer);
    }
}
