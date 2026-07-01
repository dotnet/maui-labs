// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

public class RichContentBlock : ContentBlock
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
