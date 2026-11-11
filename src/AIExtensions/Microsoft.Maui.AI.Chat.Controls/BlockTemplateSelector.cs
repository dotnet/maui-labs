namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Selects the template of a <see cref="ContentBlock"/> from a list of <see cref="BlockTemplate"/>s: the first whose
/// <see cref="BlockTemplate.When"/> matches the block.
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
/// (as <see cref="CollectionView"/> does) reuses them. A <see cref="DataTemplateSelector"/> set as a
/// <see cref="BlockTemplate.Template"/>, such as another <see cref="BlockTemplateSelector"/>, is evaluated against
/// the block. A list selects a block's template when it shows the block; it doesn't select again when the block
/// changes, so match on what a block is (its type, tool, and role) rather than on content that streams in.
/// </para>
/// </remarks>
[ContentProperty(nameof(Templates))]
public sealed class BlockTemplateSelector : DataTemplateSelector
{
    private static readonly DataTemplate Empty = new(static () => new ContentView());

    /// <summary>Gets the templates, tried in order. In XAML, they are the element's content.</summary>
    public IList<BlockTemplate> Templates { get; } = new List<BlockTemplate>();

    /// <inheritdoc />
    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        if (item is not ContentBlock block)
            return Empty;

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
}
