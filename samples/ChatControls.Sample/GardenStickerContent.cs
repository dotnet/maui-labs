using Microsoft.Maui.Chat.Controls;

namespace ChatControls.Sample;

public sealed class GardenStickerContent : MessageContent
{
    public GardenStickerContent(string glyph, string description)
    {
        Glyph = string.IsNullOrWhiteSpace(glyph)
            ? throw new ArgumentException("A sticker glyph is required.", nameof(glyph))
            : glyph;
        Description = string.IsNullOrWhiteSpace(description)
            ? throw new ArgumentException("A sticker description is required.", nameof(description))
            : description;
        Presentation = ChatContentPresentation.Bare;
    }

    public string Glyph { get; }

    public string Description { get; }
}
