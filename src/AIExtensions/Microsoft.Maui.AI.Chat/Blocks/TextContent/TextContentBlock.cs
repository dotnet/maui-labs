// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// User or assistant text streamed as M.E.AI <see cref="TextContent"/>.
/// </summary>
/// <remarks>
/// Emitted by the built-in <see cref="TextBlockHandler"/>, one block per message. This is the pipeline's
/// fallback block: any text no earlier handler claims ends up here, and its paragraphs are built from
/// <see cref="RichContentBlock.RawText"/>. The type lets a renderer tell plain text from a provider's
/// rich text; target <see cref="RichContentBlock"/> to handle both.
/// </remarks>
public class TextContentBlock : RichContentBlock
{
    /// <summary>Gets or sets the identifier of the message whose text the block shows.</summary>
    internal string? MessageId { get; set; }
}
