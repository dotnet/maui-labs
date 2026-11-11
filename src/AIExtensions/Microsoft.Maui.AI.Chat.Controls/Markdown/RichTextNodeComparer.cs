// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Structural equality for rich-text trees, used to keep views of unchanged blocks.</summary>
internal static class RichTextNodeComparer
{
    // Exact node types whose full state is known here. A subclass may carry extra state, so
    // instances of unknown types are only equal to themselves.
    private static readonly HashSet<Type> s_knownTypes =
    [
        typeof(ParagraphNode),
        typeof(BlockQuoteNode),
        typeof(EmphasisNode),
        typeof(StrongNode),
        typeof(StrikethroughNode),
        typeof(LineBreakNode),
        typeof(ThematicBreakNode),
        typeof(TableRowNode),
        typeof(TableCellNode),
        typeof(FootnoteNode),
        typeof(TextNode),
        typeof(HeadingNode),
        typeof(CodeBlockNode),
        typeof(InlineCodeNode),
        typeof(LinkNode),
        typeof(ImageNode),
        typeof(ListNode),
        typeof(ListItemNode),
        typeof(TableNode),
        typeof(HtmlNode),
        typeof(DefinitionNode),
        typeof(LinkReferenceNode),
        typeof(ImageReferenceNode),
        typeof(FootnoteDefinitionNode),
        typeof(FootnoteReferenceNode),
    ];

    internal static bool AreEqual(RichTextNode? left, RichTextNode? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;

        // Iterative so arbitrarily deep provider documents cannot overflow the stack.
        var pending = new Stack<(RichTextNode Left, RichTextNode Right)>();
        pending.Push((left, right));
        while (pending.Count > 0)
        {
            var (a, b) = pending.Pop();
            if (ReferenceEquals(a, b))
                continue;

            var type = a.GetType();
            if (type != b.GetType() || !s_knownTypes.Contains(type) || !HaveEqualValues(a, b))
                return false;

            var leftChildren = a.Children;
            var rightChildren = b.Children;
            if (leftChildren.Count != rightChildren.Count)
                return false;

            for (var i = 0; i < leftChildren.Count; i++)
                pending.Push((leftChildren[i], rightChildren[i]));
        }

        return true;
    }

    private static bool HaveEqualValues(RichTextNode left, RichTextNode right) => left switch
    {
        TextNode text => Same(text.Text, ((TextNode)right).Text),
        HeadingNode heading => heading.Level == ((HeadingNode)right).Level,
        CodeBlockNode code => Same(code.Code, ((CodeBlockNode)right).Code)
            && Same(code.Language, ((CodeBlockNode)right).Language),
        InlineCodeNode code => Same(code.Code, ((InlineCodeNode)right).Code),
        LinkNode link => Same(link.Url, ((LinkNode)right).Url)
            && Same(link.Title, ((LinkNode)right).Title),
        ImageNode image => Same(image.Url, ((ImageNode)right).Url)
            && Same(image.Alt, ((ImageNode)right).Alt)
            && Same(image.Title, ((ImageNode)right).Title),
        ListNode list => list.Ordered == ((ListNode)right).Ordered
            && list.Start == ((ListNode)right).Start,
        ListItemNode item => item.Checked == ((ListItemNode)right).Checked,
        TableNode table => SameAlignment(table.Alignment, ((TableNode)right).Alignment),
        HtmlNode html => Same(html.Value, ((HtmlNode)right).Value),
        DefinitionNode definition => Same(definition.Label, ((DefinitionNode)right).Label)
            && Same(definition.Url, ((DefinitionNode)right).Url)
            && Same(definition.Title, ((DefinitionNode)right).Title),
        LinkReferenceNode reference => Same(reference.Label, ((LinkReferenceNode)right).Label)
            && reference.ReferenceKind == ((LinkReferenceNode)right).ReferenceKind,
        ImageReferenceNode reference => Same(reference.Label, ((ImageReferenceNode)right).Label)
            && Same(reference.Alt, ((ImageReferenceNode)right).Alt)
            && reference.ReferenceKind == ((ImageReferenceNode)right).ReferenceKind,
        FootnoteDefinitionNode footnote => Same(footnote.Label, ((FootnoteDefinitionNode)right).Label),
        FootnoteReferenceNode footnote => Same(footnote.Label, ((FootnoteReferenceNode)right).Label),
        _ => true,
    };

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.Ordinal);

    private static bool SameAlignment(
        IReadOnlyList<TableColumnAlignment> left,
        IReadOnlyList<TableColumnAlignment> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
                return false;
        }

        return true;
    }
}
