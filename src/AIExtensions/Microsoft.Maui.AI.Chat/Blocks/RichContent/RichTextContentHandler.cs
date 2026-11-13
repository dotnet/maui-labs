// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>Maps provider-supplied <see cref="RichTextContent"/> snapshots into one rich block per message.</summary>
/// <remarks>
/// The last snapshot in an update wins. A <see cref="TextContent"/> sent alongside a snapshot is the
/// plain-text form of the same message, so it is claimed too instead of rendering a second block.
/// </remarks>
internal sealed class RichTextContentHandler : ContentBlockHandler<RichContentBlock>
{
    public override BlockMappingResult<RichContentBlock> Handle(
        BlockMappingContext context,
        RichContentBlock state)
    {
        RichTextContent? snapshot = null;
        foreach (var content in context.UnhandledContents)
        {
            if (content is RichTextContent richText)
            {
                snapshot = richText;
            }
        }

        if (snapshot is null)
        {
            // Other output ends the message; an update without output, such as metadata, does not.
            return state.Id.Length > 0 && context.HasOutput
                ? BlockMappingResult<RichContentBlock>.Complete()
                : BlockMappingResult<RichContentBlock>.Pass();
        }

        if (state.Id.Length > 0
            && context.Update.MessageId is { Length: > 0 } messageId
            && !string.Equals(state.Id, messageId, StringComparison.Ordinal))
        {
            return BlockMappingResult<RichContentBlock>.Complete();
        }

        foreach (var content in context.UnhandledContents)
        {
            if (content is RichTextContent or TextContent)
            {
                context.MarkHandled(content);
            }
        }

        state.ReplaceContent(snapshot.Text, snapshot.Nodes);

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
