// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>Base class for structured rich-text nodes.</summary>
public abstract class RichTextNode
{
    private List<RichTextNode>? _children;

    public IReadOnlyList<RichTextNode> Children =>
        _children ?? (IReadOnlyList<RichTextNode>)Array.Empty<RichTextNode>();

    public void AddChild(RichTextNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children ??= new();
        _children.Add(child);
    }
}

public class ParagraphNode : RichTextNode;
public class BlockQuoteNode : RichTextNode;
public class EmphasisNode : RichTextNode;
public class StrongNode : RichTextNode;
public class StrikethroughNode : RichTextNode;
public class LineBreakNode : RichTextNode;
public class ThematicBreakNode : RichTextNode;
public class TableRowNode : RichTextNode;
public class TableCellNode : RichTextNode;
public class FootnoteNode : RichTextNode;

public class TextNode : RichTextNode
{
    public TextNode() { }
    public TextNode(string text) => Text = text;
    public string Text { get; set; } = string.Empty;
}

public class HeadingNode : RichTextNode
{
    public HeadingNode() { }
    public HeadingNode(int level) => Level = level;
    public int Level { get; set; } = 1;
}

public class CodeBlockNode : RichTextNode
{
    public CodeBlockNode() { }

    public CodeBlockNode(string code, string? language = null)
    {
        Code = code;
        Language = language;
    }

    public string? Language { get; set; }
    public string Code { get; set; } = string.Empty;
}

public class InlineCodeNode : RichTextNode
{
    public InlineCodeNode() { }
    public InlineCodeNode(string code) => Code = code;
    public string Code { get; set; } = string.Empty;
}

public class LinkNode : RichTextNode
{
    public LinkNode() { }

    public LinkNode(string url, string? title = null)
    {
        Url = url;
        Title = title;
    }

    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
}

public class ImageNode : RichTextNode
{
    public ImageNode() { }

    public ImageNode(string url, string? alt = null, string? title = null)
    {
        Url = url;
        Alt = alt;
        Title = title;
    }

    public string Url { get; set; } = string.Empty;
    public string? Alt { get; set; }
    public string? Title { get; set; }
}

public class ListNode : RichTextNode
{
    public ListNode() { }

    public ListNode(bool ordered, int? start = null)
    {
        Ordered = ordered;
        Start = start;
    }

    public bool Ordered { get; set; }
    public int? Start { get; set; }
}

public class ListItemNode : RichTextNode
{
    public bool? Checked { get; set; }
}

public class TableNode : RichTextNode
{
    public IReadOnlyList<TableColumnAlignment> Alignment { get; set; } =
        Array.Empty<TableColumnAlignment>();
}

public class HtmlNode : RichTextNode
{
    public HtmlNode() { }
    public HtmlNode(string value) => Value = value;
    public string Value { get; set; } = string.Empty;
}

public class DefinitionNode : RichTextNode
{
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
}

public class LinkReferenceNode : RichTextNode
{
    public string Label { get; set; } = string.Empty;
    public ReferenceKind ReferenceKind { get; set; }
}

public class ImageReferenceNode : RichTextNode
{
    public string Label { get; set; } = string.Empty;
    public string? Alt { get; set; }
    public ReferenceKind ReferenceKind { get; set; }
}

public class FootnoteDefinitionNode : RichTextNode
{
    public string Label { get; set; } = string.Empty;
}

public class FootnoteReferenceNode : RichTextNode
{
    public string Label { get; set; } = string.Empty;
}

public enum ReferenceKind
{
    Shortcut,
    Collapsed,
    Full,
}

public enum TableColumnAlignment
{
    None,
    Left,
    Center,
    Right,
}
