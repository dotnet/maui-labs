// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>One media item, such as an image, audio clip or file.</summary>
/// <remarks>
/// Emitted by <see cref="MediaContentHandler"/>, one block per item, for M.E.AI <see cref="DataContent"/>
/// and <see cref="UriContent"/>, including the outputs of a hosted image generation tool
/// (<see cref="ImageGenerationToolResultContent"/>). While an image generation call streams partial
/// images, its block's <see cref="Content"/> is replaced by each newer image and the block raises
/// <see cref="ContentBlock.PropertyChanged"/> for it.
/// </remarks>
public class MediaContentBlock : ContentBlock
{
    private AIContent _content;

    /// <summary>Initializes a new instance of the <see cref="MediaContentBlock"/> class.</summary>
    /// <param name="content">The media item: a <see cref="DataContent"/> or a <see cref="UriContent"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="content"/> is neither kind of media content.</exception>
    public MediaContentBlock(AIContent content)
    {
        _content = ValidateContent(content);
    }

    /// <summary>
    /// Gets the media item: a <see cref="DataContent"/> with inline data, or a <see cref="UriContent"/>
    /// that links to it. Use their <c>MediaType</c> to choose how to show it.
    /// </summary>
    public AIContent Content
    {
        get => _content;
        internal set
        {
            ValidateContent(value);
            if (ReferenceEquals(_content, value))
                return;

            _content = value;
            OnPropertyChanged();
        }
    }

    private static AIContent ValidateContent(AIContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content is not (DataContent or UriContent))
        {
            throw new ArgumentException(
                $"Media content must be a {nameof(DataContent)} or a {nameof(UriContent)}.",
                nameof(content));
        }

        return content;
    }
}
