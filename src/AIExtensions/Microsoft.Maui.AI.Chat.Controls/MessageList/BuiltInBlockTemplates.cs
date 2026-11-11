namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// The built-in block views, as a fallback <see cref="BlockTemplateSelector"/> tried after the app's
/// <see cref="AIChatView.ContentTemplates"/>, and the selection rule both share.
/// </summary>
internal static class BuiltInBlockTemplates
{
    /// <summary>
    /// Gets the built-in templates. Assistant text (<see cref="TextContentBlock"/>) renders as Markdown, and a
    /// provider's structured rich text (<see cref="RichContentBlock"/>) with the same renderer. The first match wins,
    /// so text comes before rich text, which it derives from.
    /// </summary>
    public static BlockTemplateSelector Selector { get; } = new()
    {
        Templates =
        {
            Create<TextContentBlock>(static () => new TextBlockView()),
            Create<RichContentBlock>(static () => new RichTextBlockView()),
            Create<FunctionInvocationContentBlock>(static () => new ToolCallView()),
            Create<ToolApprovalBlock>(static () => new ToolApprovalView()),
            Create<UIActionBlock>(static () => new UIActionView()),
            Create<ReasoningContentBlock>(static () => new ReasoningView()),
            Create<MediaContentBlock>(static () => new MediaBlockView()),
        },
    };

    /// <summary>Gets the built-in templates, in the order they are tried.</summary>
    public static IList<BlockTemplate> All => Selector.Templates;

    /// <summary>
    /// Selects the template for a block: the first app template that matches, then (optionally) the first built-in
    /// one, the rule of <see cref="BlockTemplateSelector"/>. The winner may have no view, which hides the block.
    /// </summary>
    public static BlockTemplate? Select(ContentBlock block, IEnumerable<BlockTemplate>? appTemplates, bool includeBuiltIns) =>
        SelectFrom(appTemplates, block) ?? (includeBuiltIns ? SelectFrom(All, block) : null);

    /// <summary>Gets whether <paramref name="template"/> is one of the built-in templates.</summary>
    public static bool IsBuiltIn(BlockTemplate template) => All.Contains(template);

    /// <summary>
    /// Creates the view <paramref name="template"/> shows <paramref name="block"/> with, evaluating a
    /// <see cref="DataTemplateSelector"/> against the block.
    /// </summary>
    public static View CreateView(BlockTemplate template, ContentBlock block, BindableObject container)
    {
        var content = template.Template
            ?? throw new InvalidOperationException($"The {nameof(BlockTemplate)} has no {nameof(BlockTemplate.Template)}.");
        return CreateView(content is DataTemplateSelector selector ? selector.SelectTemplate(block, container) : content);
    }

    /// <summary>Creates the view of a block template's <see cref="DataTemplate"/>.</summary>
    public static View CreateView(DataTemplate template) =>
        template.CreateContent() switch
        {
            View view => view,
            var other => throw new InvalidOperationException(
                $"The {nameof(BlockTemplate.Template)} of a {nameof(BlockTemplate)} must create a View, not '{other?.GetType().Name ?? "null"}'."),
        };

    private static BlockTemplate? SelectFrom(IEnumerable<BlockTemplate>? templates, ContentBlock block)
    {
        if (templates is null)
            return null;

        foreach (var template in templates)
        {
            if (template is not null && (template.When is null || template.When.Matches(block)))
                return template;
        }

        return null;
    }

    private static BlockTemplate Create<TBlock>(Func<View> create)
        where TBlock : ContentBlock =>
        new() { When = BlockMatch.Is<TBlock>(), Template = new DataTemplate(create) };
}
