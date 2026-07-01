// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>Plain user or assistant text, accumulated as it streams in.</summary>
/// <remarks>
/// Emitted by the built-in <see cref="TextBlockHandler"/> from M.E.AI <see cref="TextContent"/>. This is
/// the pipeline's fallback block: any text no earlier handler claims ends up here.
/// </remarks>
public class TextContentBlock : ContentBlock
{
    private readonly List<string> _segments = new();
    private string? _cachedText;

    public string RawText => _cachedText ??= string.Concat(_segments);

    public void AppendText(string text)
    {
        _segments.Add(text);
        _cachedText = null;
    }
}
