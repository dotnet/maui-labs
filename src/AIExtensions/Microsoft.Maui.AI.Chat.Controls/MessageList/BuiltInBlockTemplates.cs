namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>The built-in block templates, used when no app template matches a response block.</summary>
internal static class BuiltInBlockTemplates
{
    /// <summary>
    /// Gets the built-in templates. Assistant text renders as plain text until the Markdown renderer
    /// replaces the <see cref="TextContentBlock"/> entry here.
    /// </summary>
    public static IReadOnlyList<BlockTemplate> All { get; } =
    [
        new BlockTemplate { BlockType = typeof(TextContentBlock), Factory = static () => new TextBlockView() },
        new BlockTemplate { BlockType = typeof(RichContentBlock), Factory = static () => new RichTextBlockView() },
        new BlockTemplate { BlockType = typeof(FunctionInvocationContentBlock), Factory = static () => new ToolCallView() },
        new BlockTemplate { BlockType = typeof(ToolApprovalBlock), Factory = static () => new ToolApprovalView() },
        new BlockTemplate { BlockType = typeof(UIActionBlock), Factory = static () => new UIActionView() },
        new BlockTemplate { BlockType = typeof(ReasoningContentBlock), Factory = static () => new ReasoningView() },
        new BlockTemplate { BlockType = typeof(MediaContentBlock), Factory = static () => new MediaBlockView() },
    ];

    /// <summary>
    /// Selects the template for a block: app templates first, then (optionally) the built-ins. Within a tier the
    /// highest priority wins, then the most specific, then the last declared. The winner may have no view, which
    /// hides the block.
    /// </summary>
    public static BlockTemplate? Select(ContentBlock block, IEnumerable<BlockTemplate>? appTemplates, bool includeBuiltIns) =>
        SelectFrom(appTemplates, block) ?? (includeBuiltIns ? SelectFrom(All, block) : null);

    private static BlockTemplate? SelectFrom(IEnumerable<BlockTemplate>? templates, ContentBlock block)
    {
        if (templates is null)
            return null;

        BlockTemplate? best = null;
        foreach (var template in templates)
        {
            if (template is null || !template.When(block))
                continue;

            if (best is null || Compare(template, best) >= 0)
                best = template;
        }

        return best;
    }

    private static int Compare(BlockTemplate left, BlockTemplate right)
    {
        var priority = left.Priority.CompareTo(right.Priority);
        return priority != 0 ? priority : left.Specificity.CompareTo(right.Specificity);
    }
}
