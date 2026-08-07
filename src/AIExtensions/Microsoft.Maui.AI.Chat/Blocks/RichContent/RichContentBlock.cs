// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Streaming textual content together with a structured rich-text projection.
/// </summary>
/// <remarks>
/// The built-in text handler currently projects paragraphs and plain text only. The remaining
/// rich-text node types are an extensibility contract for custom parsers and renderers, not a claim
/// that the engine performs complete Markdown parsing.
/// </remarks>
public class RichContentBlock : ContentBlock
{
    private readonly List<string> _segments = new();
    private string? _cachedText;

    /// <summary>Gets the complete streamed text accumulated so far.</summary>
    public string RawText => _cachedText ??= string.Concat(_segments);

    /// <summary>Gets the current structured projection of <see cref="RawText"/>.</summary>
    public IReadOnlyList<RichTextNode> Content { get; protected internal set; } =
        Array.Empty<RichTextNode>();

    /// <summary>Appends one streaming text segment.</summary>
    public virtual void AppendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _segments.Add(text);
        _cachedText = null;
    }
}
