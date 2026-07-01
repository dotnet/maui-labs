// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

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
}
