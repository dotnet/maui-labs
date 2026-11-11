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
            // Other output ends the text; an update without output, such as metadata, does not.
            return state.Id != string.Empty && context.HasOutput
                ? BlockMappingResult<TextContentBlock>.Complete()
                : BlockMappingResult<TextContentBlock>.Pass();
        }

        // Each message gets its own block, for example consecutive restored assistant messages.
        if (state.MessageId is { Length: > 0 } blockMessageId
            && context.Update.MessageId is { Length: > 0 } messageId
            && !string.Equals(blockMessageId, messageId, StringComparison.Ordinal))
        {
            return BlockMappingResult<TextContentBlock>.Complete();
        }

        context.MarkHandled(textContent);
        state.AppendText(textContent.Text ?? string.Empty);

        if (state.Id == string.Empty)
        {
            state.MessageId = context.Update.MessageId;
            state.Id = context.Update.MessageId ?? Guid.NewGuid().ToString("N");
            return BlockMappingResult<TextContentBlock>.Emit(state, state);
        }
        else
        {
            return BlockMappingResult<TextContentBlock>.Update(state);
        }
    }
}
