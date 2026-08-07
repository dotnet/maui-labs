// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>Built-in fallback handler: accumulates M.E.AI <see cref="TextContent"/> into a <see cref="TextContentBlock"/>.</summary>
/// <remarks>Registered last in the <see cref="BlockMappingPipeline"/>, so it claims any text no other handler took.</remarks>
internal sealed class TextBlockHandler : ContentBlockHandler<TextContentBlock>
{
    public override BlockMappingResult<TextContentBlock> Handle(
        BlockMappingContext context, TextContentBlock state)
    {
        TextContent? textContent = null;
        foreach (var content in context.UnhandledContents)
        {
            if (content is TextContent tc)
            {
                textContent = tc;
                break;
            }
        }

        if (textContent is null)
        {
            if (state.Id != string.Empty)
            {
                return BlockMappingResult<TextContentBlock>.Complete();
            }

            return BlockMappingResult<TextContentBlock>.Pass();
        }

        context.MarkHandled(textContent);
        state.AppendText(textContent.Text ?? string.Empty);
        RebuildParagraphs(state);

        if (state.Id == string.Empty)
        {
            state.Id = context.Update.MessageId ?? Guid.NewGuid().ToString("N");
            return BlockMappingResult<TextContentBlock>.Emit(state, state);
        }
        else
        {
            return BlockMappingResult<TextContentBlock>.Update(state);
        }
    }

    internal static void RebuildParagraphs(RichContentBlock state)
    {
        var rawText = state.RawText;
        if (rawText.Length == 0)
        {
            state.Content = Array.Empty<RichTextNode>();
            return;
        }

        var paragraphs = new List<RichTextNode>();
        var start = 0;
        while (start < rawText.Length)
        {
            var breakIndex = rawText.IndexOf("\n\n", start, StringComparison.Ordinal);
            if (breakIndex < 0)
            {
                AddParagraph(paragraphs, rawText.AsSpan(start));
                break;
            }

            if (breakIndex > start)
                AddParagraph(paragraphs, rawText.AsSpan(start, breakIndex - start));

            start = breakIndex + 2;
        }

        state.Content = paragraphs;
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
