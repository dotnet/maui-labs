namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Selects the template of a <see cref="ContentBlock"/> from a list of <see cref="BlockTemplate"/>s: the first whose
/// <see cref="BlockTemplate.When"/> matches the block. A block that wraps another, such as an approval and the call it
/// asks about, first gets the chrome of the first of its <see cref="Wrappers"/> that wraps it, around the template of
/// the wrapped block.
/// </summary>
/// <remarks>
/// <para>
/// Use it as the item template of any list of blocks, such as a <see cref="CollectionView"/> or a
/// <see cref="BindableLayout"/> bound to a turn's <see cref="ConversationTurn.ResponseBlocks"/>. Templates are tried
/// in order, so put the most specific first and a catch-all (a template without <see cref="BlockTemplate.When"/>)
/// last.
/// </para>
/// <para>
/// A block that no template matches, a matching template without a <see cref="BlockTemplate.Template"/>, and an item
/// that is not a block all get the same empty template (an empty <see cref="ContentView"/>), because a list needs a
/// template for every item. Add a catch-all template to show every block.
/// </para>
/// <para>
/// The selector returns the templates it holds, never copies, so a list that caches its item views by template
/// (as <see cref="CollectionView"/> does) reuses them. For a wrapped block it creates one template for each pair of
/// chrome and wrapped block template, and returns that every time. A <see cref="DataTemplateSelector"/> set as a
/// <see cref="BlockTemplate.Template"/>, such as another <see cref="BlockTemplateSelector"/>, is evaluated against
/// the block. A list selects a block's template when it shows the block; it doesn't select again when the block
/// changes, so match on what a block is (its type, tool, and role) rather than on content that streams in.
/// </para>
/// </remarks>
[ContentProperty(nameof(Templates))]
public sealed class BlockTemplateSelector : DataTemplateSelector
{
    private static readonly DataTemplate Empty = new(static () => new ContentView());

    private readonly Dictionary<(BlockWrapper Wrapper, ControlTemplate? Chrome, DataTemplate Wrapped), DataTemplate> _wrappedTemplates = [];

    /// <summary>Gets the templates, tried in order. In XAML, they are the element's content.</summary>
    public IList<BlockTemplate> Templates { get; } = new List<BlockTemplate>();

    /// <summary>
    /// Gets the wrappers, tried in order before <see cref="Templates"/>, such as an <see cref="ApprovalWrapper"/> that
    /// shows Approve and Reject around the card of the call an approval asks about.
    /// </summary>
    public IList<BlockWrapper> Wrappers { get; } = new List<BlockWrapper>();

    /// <inheritdoc />
    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        => item is ContentBlock block ? Select(block, container) : Empty;

    private DataTemplate Select(ContentBlock block, BindableObject container)
    {
        foreach (var wrapper in Wrappers)
        {
            if (wrapper?.GetWrappedBlock(block) is not { } wrapped)
                continue;

            if (ReferenceEquals(wrapped, block))
            {
                throw new InvalidOperationException(
                    $"{wrapper.GetType().Name}.{nameof(BlockWrapper.GetWrappedBlock)} returned the block it was given. Return the block it wraps, or null when it doesn't wrap the block.");
            }

            if (wrapper.When is { } when && !when.Matches(wrapped, GetShownTool(block, wrapped)))
                continue;

            return GetWrappedTemplate(wrapper, wrapper.Template, Select(wrapped, container));
        }

        foreach (var template in Templates)
        {
            if (template is null || (template.When is { } when && !when.Matches(block)))
                continue;

            return template.Template switch
            {
                null => Empty,
                DataTemplateSelector selector => selector.SelectTemplate(block, container) ?? Empty,
                var dataTemplate => dataTemplate,
            };
        }

        return Empty;
    }

    /// <summary>
    /// Gets the tool of the call that <paramref name="wrapped"/> shows for <paramref name="block"/>: the inner block of an
    /// approval shows the call the approval asks about, also when it is a block of the app's own type, which doesn't
    /// name the tool itself.
    /// </summary>
    private static string? GetShownTool(ContentBlock block, ContentBlock wrapped) =>
        block is InteractiveFunctionBlock interactive && ReferenceEquals(interactive.InnerBlock, wrapped)
            ? interactive.ToolName
            : null;

    private DataTemplate GetWrappedTemplate(BlockWrapper wrapper, ControlTemplate? chrome, DataTemplate wrappedTemplate)
    {
        var key = (wrapper, chrome, wrappedTemplate);
        if (!_wrappedTemplates.TryGetValue(key, out var template))
        {
            template = new DataTemplate(() => new WrappedBlockView(wrapper, chrome, wrappedTemplate));
            _wrappedTemplates[key] = template;
        }

        return template;
    }
}
