// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Streaming textual content together with a structured rich-text projection.
/// </summary>
/// <remarks>
/// The built-in text handler currently projects paragraphs and plain text only. The remaining
/// rich-text node types are an extensibility contract for custom parsers and renderers, not a claim
/// that the engine performs complete Markdown parsing. The block raises
/// <see cref="ContentBlock.PropertyChanged"/> for <see cref="RawText"/> and <see cref="Content"/> as text
/// streams in.
/// </remarks>
public class RichContentBlock : ContentBlock
{
    private static readonly PropertyChangedEventArgs s_rawTextChanged = new(nameof(RawText));
    private static readonly PropertyChangedEventArgs s_contentChanged = new(nameof(Content));

    private readonly List<string> _segments = new();
    private readonly List<RichTextNode> _completedParagraphs = new();
    private string? _cachedText;
    private IReadOnlyList<RichTextNode>? _content;
    private IReadOnlyList<RichTextNode>? _paragraphs;
    private int _completedParagraphsEnd;

    /// <summary>Gets the complete streamed text accumulated so far.</summary>
    public string RawText => _cachedText ??= string.Concat(_segments);

    /// <summary>Gets the current structured projection of <see cref="RawText"/>.</summary>
    /// <remarks>
    /// Unless a provider supplied structured content, this is the text's paragraphs, separated by blank
    /// lines. They are computed when read, and paragraphs that a later blank line completed are kept
    /// rather than rebuilt while more text streams in.
    /// </remarks>
    public IReadOnlyList<RichTextNode> Content
    {
        get => _content ?? (_paragraphs ??= ProjectParagraphs());
        internal set
        {
            if (ReferenceEquals(_content, value))
                return;

            _content = value;
            RaisePropertyChanged(s_contentChanged);
        }
    }

    /// <summary>Appends one streaming text segment.</summary>
    /// <remarks>
    /// Raises <see cref="ContentBlock.PropertyChanged"/> for <see cref="RawText"/>, and for
    /// <see cref="Content"/> while it is projected from the text.
    /// </remarks>
    public void AppendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            return;

        _segments.Add(text);
        _cachedText = null;
        _paragraphs = null;
        RaisePropertyChanged(s_rawTextChanged);
        if (_content is null)
            RaisePropertyChanged(s_contentChanged);
    }

    /// <summary>Replaces the complete text and structured projection with one provider snapshot.</summary>
    internal void ReplaceContent(string text, IReadOnlyList<RichTextNode> content)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(content);

        var textChanged = !string.Equals(RawText, text, StringComparison.Ordinal);
        _segments.Clear();
        _segments.Add(text);
        _cachedText = text;
        _content = content;
        _paragraphs = null;
        _completedParagraphs.Clear();
        _completedParagraphsEnd = 0;
        if (textChanged)
            RaisePropertyChanged(s_rawTextChanged);
        RaisePropertyChanged(s_contentChanged);
    }

    private IReadOnlyList<RichTextNode> ProjectParagraphs()
    {
        var text = RawText;

        // Appending text cannot change paragraphs that end before an earlier blank line.
        int breakIndex;
        while ((breakIndex = text.IndexOf("\n\n", _completedParagraphsEnd, StringComparison.Ordinal)) >= 0)
        {
            AddParagraph(_completedParagraphs, text.AsSpan(_completedParagraphsEnd, breakIndex - _completedParagraphsEnd));
            _completedParagraphsEnd = breakIndex + 2;
        }

        var paragraphs = new List<RichTextNode>(_completedParagraphs.Count + 1);
        paragraphs.AddRange(_completedParagraphs);
        AddParagraph(paragraphs, text.AsSpan(_completedParagraphsEnd));
        return paragraphs.Count == 0 ? Array.Empty<RichTextNode>() : paragraphs;
    }

    private static void AddParagraph(List<RichTextNode> paragraphs, ReadOnlySpan<char> text)
    {
        var trimmed = text.TrimEnd("\r\n".AsSpan());
        if (trimmed.Length == 0)
            return;

        var paragraph = new ParagraphNode();
        paragraph.AddChild(new TextNode(trimmed.ToString()));
        paragraphs.Add(paragraph);
    }
}
