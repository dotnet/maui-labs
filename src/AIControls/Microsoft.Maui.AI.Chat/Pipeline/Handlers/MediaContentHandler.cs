// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>Built-in handler mapping M.E.AI <c>DataContent</c> (and image-generation results) into a <see cref="MediaContentBlock"/>.</summary>
/// <remarks>Also extracts generated images from <c>ImageGenerationToolResultContent.Outputs</c>.</remarks>
internal sealed class MediaContentHandler : ContentBlockHandler<MediaContentBlock>
{
    public override BlockMappingResult<MediaContentBlock> Handle(
        BlockMappingContext context, MediaContentBlock state)
    {
        AIContent? claimed = null;
        var images = new List<DataContent>();

        foreach (var content in context.UnhandledContents)
        {
            if (content is DataContent dc)
            {
                claimed = content;
                images.Add(dc);
                break;
            }

            // Image-generation results (e.g. from a HostedImageGenerationTool) wrap
            // their generated images as DataContent items in Outputs.
            if (content is ImageGenerationToolResultContent igr && igr.Outputs is { Count: > 0 })
            {
                foreach (var output in igr.Outputs)
                {
                    if (output is DataContent odc)
                    {
                        images.Add(odc);
                    }
                }

                if (images.Count > 0)
                {
                    claimed = content;
                    break;
                }
            }
        }

        if (claimed is null)
        {
            if (state.Items.Count > 0)
            {
                return BlockMappingResult<MediaContentBlock>.Complete();
            }
            return BlockMappingResult<MediaContentBlock>.Pass();
        }

        context.MarkHandled(claimed);

        var wasEmpty = state.Items.Count == 0;
        foreach (var image in images)
        {
            state.AddContent(image);
        }

        if (wasEmpty)
        {
            state.Id = context.Update.MessageId ?? Guid.NewGuid().ToString("N");
            return BlockMappingResult<MediaContentBlock>.Emit(state, state);
        }

        return BlockMappingResult<MediaContentBlock>.Update(state);
    }
}
