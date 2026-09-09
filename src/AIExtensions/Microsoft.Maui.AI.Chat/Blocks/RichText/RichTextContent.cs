// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Provider-supplied rich text containing both its source text and a parsed node tree.
/// </summary>
/// <remarks>
/// This type carries an existing structured projection. It does not parse Markdown or other markup.
/// </remarks>
public sealed class RichTextContent : AIContent
{
    /// <summary>Initializes a rich-text snapshot.</summary>
    public RichTextContent(string text, IReadOnlyList<RichTextNode> nodes)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
    }

    /// <summary>Gets the source text represented by <see cref="Nodes"/>.</summary>
    public string Text { get; }

    /// <summary>Gets the provider-supplied structured projection.</summary>
    public IReadOnlyList<RichTextNode> Nodes { get; }
}
