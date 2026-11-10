using Microsoft.Maui.AI.Chat.Controls;
using Microsoft.Maui.Chat;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// Renders a <see cref="GardenFormattedTextBlock"/> with <see cref="GardenFormattedTextView"/>.
/// A tiny typed <see cref="ContentTemplate"/> subclass instead of a
/// <c>GenericContentTemplate</c>.
/// </summary>
public sealed class GardenFormattedTextTemplate : ContentTemplate
{
    public GardenFormattedTextTemplate()
    {
        ViewType = typeof(GardenFormattedTextView);
        Presentation = ChatContentPresentation.Bubble;
    }

    public override bool When(ContentContext context) => context.Block is GardenFormattedTextBlock;
}
