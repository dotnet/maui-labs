using AndroidX.Compose.Material3;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>HorizontalDivider</c> — a thin horizontal line used to
/// separate sibling content blocks (typically inside a
/// <see cref="Column"/>).
/// </summary>
public sealed class HorizontalDivider : ComposableNode
{
    /// <summary>Optional explicit thickness in Dp. Leave null to use the Material default.</summary>
    public float? ThicknessDp { get; set; }

    /// <summary>Optional explicit color. Leave null to use the Material default.</summary>
    public Color? Color { get; set; }

    public override void Render(IComposer composer)
    {
        var modifier = BuildModifier();

        int defaults = (int)HorizontalDividerDefault.All;
        if (modifier is not null)        defaults &= ~(int)HorizontalDividerDefault.Modifier;
        if (ThicknessDp.HasValue)        defaults &= ~(int)HorizontalDividerDefault.Thickness;
        if (Color.HasValue)              defaults &= ~(int)HorizontalDividerDefault.Color;

        DividerKt.HorizontalDivider(
            modifier:  modifier,
            thickness: ThicknessDp ?? 0f,
            color:     Color is { } c ? c : 0L,
            _composer: composer,
            p4:        0,
            _changed:  defaults);
    }
}
