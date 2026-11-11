namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Assistant text, rendered as Markdown. Full width, no bubble.</summary>
/// <remarks>
/// Each coalesced streaming update re-parses the text, and <see cref="MarkdownView"/> rebuilds only the blocks that
/// changed (usually the last one), so earlier blocks keep their views while the answer grows.
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

    protected override void OnBlockChanged() => _markdown.SetMarkdown((Block as RichContentBlock)?.RawText);
}
