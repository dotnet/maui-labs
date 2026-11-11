// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Built-in handler mapping M.E.AI <see cref="DataContent"/> and <see cref="UriContent"/> into one
/// <see cref="MediaContentBlock"/> per item, each with a unique id.
/// </summary>
/// <remarks>
/// Also shows the media outputs of <see cref="ImageGenerationToolResultContent"/>. A later result of the
/// same image generation call, such as the next partial image or the final image, replaces the content
/// of the call's blocks instead of adding new ones.
/// </remarks>
internal sealed class MediaContentHandler : ContentBlockHandler<MediaContentHandler.MediaHandlerState>
{
    private readonly Dictionary<string, List<MediaContentBlock>> _generatedImages =
        new(StringComparer.Ordinal);
    private readonly Dictionary<ImageGenerationToolResultContent, List<MediaContentBlock>> _unmatchedImages =
        new(ReferenceEqualityComparer.Instance);

    public override BlockMappingResult<MediaHandlerState> Handle(
        BlockMappingContext context,
        MediaHandlerState state)
    {
        foreach (var content in context.UnhandledContents)
        {
            if (content is ImageGenerationToolResultContent result
                && GetMedia(result) is { Count: > 0 } media)
            {
                var blocks = GetGeneratedImageBlocks(result);
                if (state.Block is not null && !blocks.Contains(state.Block))
                    continue;

                return HandleGeneratedImages(context, state, result, media, blocks);
            }
        }

        if (state.Block is not null)
            return BlockMappingResult<MediaHandlerState>.Complete();

        foreach (var content in context.UnhandledContents)
        {
            if (content is DataContent or UriContent)
            {
                context.MarkHandled(content);
                return Emit(content);
            }
        }

        return BlockMappingResult<MediaHandlerState>.Pass();
    }

    private static BlockMappingResult<MediaHandlerState> HandleGeneratedImages(
        BlockMappingContext context,
        MediaHandlerState state,
        ImageGenerationToolResultContent result,
        List<AIContent> media,
        List<MediaContentBlock> blocks)
    {
        var activeBlockChanged = false;
        for (var i = 0; i < blocks.Count && i < media.Count; i++)
        {
            var block = blocks[i];
            if (ReferenceEquals(block.Content, media[i]))
                continue;

            block.Content = media[i];
            if (ReferenceEquals(block, state.Block))
                activeBlockChanged = true;
            else
                block.InvokeNotifyChanged();
        }

        if (blocks.Count < media.Count)
        {
            // Only a fresh handler state can emit; the pipeline asks it again for each remaining output.
            if (state.Block is not null)
                return BlockMappingResult<MediaHandlerState>.Complete();

            var block = CreateBlock(media[blocks.Count]);
            blocks.Add(block);
            if (blocks.Count == media.Count)
                context.MarkHandled(result);
            else
                context.ReportProgress();

            return BlockMappingResult<MediaHandlerState>.Emit(block, new MediaHandlerState { Block = block });
        }

        context.MarkHandled(result);
        return activeBlockChanged
            ? BlockMappingResult<MediaHandlerState>.Update(state)
            : BlockMappingResult<MediaHandlerState>.Pass();
    }

    private List<MediaContentBlock> GetGeneratedImageBlocks(ImageGenerationToolResultContent result)
    {
        if (string.IsNullOrEmpty(result.CallId))
        {
            // A result without a call id cannot be matched with later results of its call.
            if (!_unmatchedImages.TryGetValue(result, out var unmatched))
            {
                unmatched = new List<MediaContentBlock>();
                _unmatchedImages.Add(result, unmatched);
            }

            return unmatched;
        }

        if (!_generatedImages.TryGetValue(result.CallId, out var blocks))
        {
            blocks = new List<MediaContentBlock>();
            _generatedImages.Add(result.CallId, blocks);
        }

        return blocks;
    }

    private static List<AIContent>? GetMedia(ImageGenerationToolResultContent result)
    {
        List<AIContent>? media = null;
        if (result.Outputs is { } outputs)
        {
            foreach (var output in outputs)
            {
                if (output is DataContent or UriContent)
                    (media ??= new List<AIContent>()).Add(output);
            }
        }

        return media;
    }

    private static BlockMappingResult<MediaHandlerState> Emit(AIContent content)
    {
        var block = CreateBlock(content);
        return BlockMappingResult<MediaHandlerState>.Emit(block, new MediaHandlerState { Block = block });
    }

    private static MediaContentBlock CreateBlock(AIContent content)
        => new(content) { Id = Guid.NewGuid().ToString("N") };

    internal sealed class MediaHandlerState
    {
        internal MediaContentBlock? Block { get; set; }
    }
}
