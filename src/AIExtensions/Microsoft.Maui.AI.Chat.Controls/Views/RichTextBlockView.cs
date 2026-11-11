namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Structured rich text from a provider (a <see cref="RichContentBlock"/> that is not plain model text), drawn by the
/// same renderer as Markdown answers.
/// </summary>
internal sealed class RichTextBlockView : ChatBlockView
{
    private readonly MarkdownView _markdown = new();

    public RichTextBlockView()
    {
        AutomationId = "AIChatRichText";
        Content = _markdown;
    }

    internal MarkdownView Markdown => _markdown;

    protected override void OnBlockChanged()
    {
        _markdown.ApplyImageSettings(Owner);
        if (Block is not RichContentBlock rich)
        {
            _markdown.SetDocument([]);
            return;
        }

        // A block without structure still has its text.
        IReadOnlyList<RichTextNode> nodes = rich.Content.Count > 0 || string.IsNullOrWhiteSpace(rich.RawText)
            ? rich.Content
            : [CreateParagraph(rich.RawText)];
        _markdown.SetDocument(nodes);
    }

    private static ParagraphNode CreateParagraph(string text)
    {
        var paragraph = new ParagraphNode();
        paragraph.AddChild(new TextNode(text));
        return paragraph;
    }
}
