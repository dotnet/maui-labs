using AndroidX.Compose.Material3;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>VerticalDivider</c> — a thin vertical line used to
/// separate sibling content blocks inside a <see cref="Row"/>.
/// </summary>
public sealed class VerticalDivider : ComposableNode
{
    /// <summary>Optional explicit thickness in Dp. Leave null to use the Material default.</summary>
    public float? ThicknessDp { get; set; }

    /// <summary>Optional explicit color. Leave null to use the Material default.</summary>
    public Color? Color { get; set; }

    public override void Render(IComposer composer)
    {
        var modifier = BuildModifier();

        int defaults = (int)VerticalDividerDefault.All;
        if (modifier is not null)        defaults &= ~(int)VerticalDividerDefault.Modifier;
        if (ThicknessDp.HasValue)        defaults &= ~(int)VerticalDividerDefault.Thickness;
        if (Color.HasValue)              defaults &= ~(int)VerticalDividerDefault.Color;

        DividerKt.VerticalDivider(
            modifier:  modifier,
            thickness: ThicknessDp ?? 0f,
            color:     Color is { } c ? c : 0L,
            _composer: composer,
            p4:        0,
            _changed:  defaults);
    }
}
