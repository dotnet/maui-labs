// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

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
            return state.Text.Length > 0 || state.ProtectedData is not null
                ? BlockMappingResult<ReasoningContentBlock>.Complete()
                : BlockMappingResult<ReasoningContentBlock>.Pass();
        }

        context.MarkHandled(reasoningContent);

        if (reasoningContent.ProtectedData is not null)
            state.ProtectedData = reasoningContent.ProtectedData;

        if (!string.IsNullOrEmpty(reasoningContent.Text))
            state.AppendText(reasoningContent.Text);

        if (state.Text.Length == 0 && state.ProtectedData is null)
            return BlockMappingResult<ReasoningContentBlock>.Pass();

        if (string.IsNullOrEmpty(state.Id))
        {
            state.Id = context.Update.MessageId ?? Guid.NewGuid().ToString("N");
            return BlockMappingResult<ReasoningContentBlock>.Emit(state, state);
        }

        return BlockMappingResult<ReasoningContentBlock>.Update(state);
    }
}
