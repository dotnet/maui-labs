// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>One or more media items (typically images).</summary>
/// <remarks>
/// Emitted by <see cref="MediaContentHandler"/> from M.E.AI <see cref="DataContent"/>, including images
/// produced by an image-generation tool (extracted from <see cref="ImageGenerationToolResultContent"/>).
/// </remarks>
public class MediaContentBlock : ContentBlock
{
    private readonly List<DataContent> _items = new();

    public IReadOnlyList<DataContent> Items => _items;

    public void AddContent(DataContent content)
    {
        _items.Add(content);
    }
}
