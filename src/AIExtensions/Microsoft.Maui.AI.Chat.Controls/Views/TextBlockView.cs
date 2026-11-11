namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Assistant text. Full width, no bubble.</summary>
/// <remarks>
/// Plain text for now. The Markdown renderer replaces this view: it is registered for
/// <see cref="TextContentBlock"/> in <see cref="BuiltInBlockTemplates"/>.
/// </remarks>
internal sealed class TextBlockView : BlockView
{
    private readonly Label _label = ChatVisuals.Label(AIChatThemeKeys.AssistantTextStyle);

    public TextBlockView()
    {
        AutomationId = "AIChatAssistantText";
        Content = _label;
    }

    internal Label Label => _label;

    protected override void OnBlockChanged() => _label.Text = (Block as RichContentBlock)?.RawText;
}
