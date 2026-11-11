// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>Maps M.E.AI <see cref="TextReasoningContent"/> into one <see cref="ReasoningContentBlock"/> per reasoning item.</summary>
/// <remarks>
/// A reasoning item ends when its message ends, or when it receives its protected data, which providers
/// send last. Its protected data stays with it, and the next item, such as the next reasoning summary,
/// starts a new block, as <c>ToChatResponse</c> keeps the items apart in the history.
/// </remarks>
internal sealed class ReasoningHandler : ContentBlockHandler<ReasoningContentBlock>
{
    public override BlockMappingResult<ReasoningContentBlock> Handle(
        BlockMappingContext context,
        ReasoningContentBlock state)
    {
        TextReasoningContent? reasoningContent = null;
        foreach (var content in context.UnhandledContents)
        {
            if (content is TextReasoningContent candidate)
            {
                reasoningContent = candidate;
                break;
            }
        }

        if (reasoningContent is null)
        {
            return state.TextLength > 0 || state.ProtectedData is not null
                ? BlockMappingResult<ReasoningContentBlock>.Complete()
                : BlockMappingResult<ReasoningContentBlock>.Pass();
        }

        // The block's item already ended with its protected data, or the reasoning belongs to another
        // message: the content starts a new block.
        if (state.ProtectedData is not null
            || (state.MessageId is { Length: > 0 } blockMessageId
                && context.Update.MessageId is { Length: > 0 } messageId
                && !string.Equals(blockMessageId, messageId, StringComparison.Ordinal)))
        {
            return BlockMappingResult<ReasoningContentBlock>.Complete();
        }

        context.MarkHandled(reasoningContent);

        if (reasoningContent.ProtectedData is not null)
            state.ProtectedData = reasoningContent.ProtectedData;

        if (!string.IsNullOrEmpty(reasoningContent.Text))
            state.AppendText(reasoningContent.Text);

        if (state.TextLength == 0 && state.ProtectedData is null)
            return BlockMappingResult<ReasoningContentBlock>.Pass();

        if (string.IsNullOrEmpty(state.Id))
        {
            state.MessageId = context.Update.MessageId;
            state.Id = context.Update.MessageId ?? Guid.NewGuid().ToString("N");
            return BlockMappingResult<ReasoningContentBlock>.Emit(state, state);
        }

        return BlockMappingResult<ReasoningContentBlock>.Update(state);
    }
}
