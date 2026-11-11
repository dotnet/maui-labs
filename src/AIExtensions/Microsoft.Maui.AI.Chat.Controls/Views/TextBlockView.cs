namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Assistant text, rendered as Markdown. Full width, no bubble.</summary>
/// <remarks>
/// Each coalesced streaming update re-parses the text, and <see cref="MarkdownView"/> updates only the views of what
/// changed (usually the end of the last block) in place, so the rest of the answer keeps its views while it grows.
/// </remarks>
internal sealed class TextBlockView : BlockView
{
    private readonly MarkdownView _markdown = new();

    public TextBlockView()
    {
        AutomationId = "AIChatAssistantText";
        Content = _markdown;
    }

    internal MarkdownView Markdown => _markdown;

    protected override void OnBlockChanged()
    {
        _markdown.ApplyImageSettings(Owner);
        _markdown.SetMarkdown((Block as RichContentBlock)?.RawText);
    }
}
