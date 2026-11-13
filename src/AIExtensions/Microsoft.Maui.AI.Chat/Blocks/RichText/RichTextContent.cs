// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Provider-supplied rich text containing both its source text and a parsed node tree.
/// </summary>
/// <remarks>
/// This type carries an existing structured projection. It does not parse Markdown or other markup.
/// Providers can emit a new snapshot as text streams; the block pipeline replaces the previous
/// snapshot. The constructor copies the top-level node list but not the nodes, so callers must not
/// mutate nodes after publishing the content.
/// </remarks>
public sealed class RichTextContent : AIContent
{
    /// <summary>Initializes a rich-text snapshot.</summary>
    /// <param name="text">The plain-text representation of the content.</param>
    /// <param name="nodes">The structured nodes that represent <paramref name="text"/>. The list is copied.</param>
    public RichTextContent(string text, IReadOnlyList<RichTextNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(nodes);

        Text = text;
        Nodes = [.. nodes];
    }

    /// <summary>Gets the source text represented by <see cref="Nodes"/>.</summary>
    public string Text { get; }

    /// <summary>Gets the provider-supplied structured projection.</summary>
    public IReadOnlyList<RichTextNode> Nodes { get; }
}
