// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>Maps provider-supplied <see cref="RichTextContent"/> snapshots into one rich block.</summary>
internal sealed class RichTextContentHandler : ContentBlockHandler<RichContentBlock>
{
    public override BlockMappingResult<RichContentBlock> Handle(
        BlockMappingContext context,
        RichContentBlock state)
    {
        RichTextContent? richText = null;
        foreach (var content in context.UnhandledContents)
        {
            if (content is RichTextContent candidate)
            {
                richText = candidate;
                break;
            }
        }

        if (richText is null)
        {
            return state.Id.Length > 0
                ? BlockMappingResult<RichContentBlock>.Complete()
                : BlockMappingResult<RichContentBlock>.Pass();
        }

        context.MarkHandled(richText);
        state.ReplaceContent(richText.Text, richText.Nodes);

        if (state.Id.Length == 0)
        {
            state.Id = context.Update.MessageId
                ?? context.Update.ResponseId
                ?? Guid.NewGuid().ToString("N");
            return BlockMappingResult<RichContentBlock>.Emit(state, state);
        }

        return BlockMappingResult<RichContentBlock>.Update(state);
    }
}
