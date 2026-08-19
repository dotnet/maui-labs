using Microsoft.Maui.Chat.Controls;

namespace ChatControls.Sample;

public sealed class GardenStickerView : ChatContentView
{
    private readonly Label _glyph = new()
    {
        FontSize = 64,
        HorizontalTextAlignment = TextAlignment.Center,
    };

    public GardenStickerView()
    {
        Content = _glyph;
        AutomationId = "GardenSticker";
    }

    protected override void RefreshContent()
    {
        var sticker = Item?.Content as GardenStickerContent;
        _glyph.Text = sticker?.Glyph ?? string.Empty;
        SemanticProperties.SetDescription(this, sticker?.Description ?? string.Empty);
    }
}
