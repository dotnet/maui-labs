using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// Renders a <see cref="GardenFormattedTextBlock"/> with <see cref="GardenFormattedTextView"/>.
/// A tiny typed <see cref="ContentTemplate"/> subclass — the idiomatic first-party pattern
/// (like the built-in <c>MediaContentTemplate</c>) instead of a <c>GenericContentTemplate</c>.
/// </summary>
public sealed class GardenFormattedTextTemplate : ContentTemplate
{
    public GardenFormattedTextTemplate() => ViewType = typeof(GardenFormattedTextView);

    public override bool When(ContentContext context) => context.Block is GardenFormattedTextBlock;
}
