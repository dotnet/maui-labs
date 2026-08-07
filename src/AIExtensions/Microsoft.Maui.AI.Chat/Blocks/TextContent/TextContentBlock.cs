// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Compatibility name for user or assistant <see cref="RichContentBlock"/> text.
/// </summary>
/// <remarks>
/// Emitted by the built-in <see cref="TextBlockHandler"/> from M.E.AI <see cref="TextContent"/>. This is
/// the pipeline's fallback block: any text no earlier handler claims ends up here. New code may target
/// <see cref="RichContentBlock"/> to handle both this built-in type and custom rich-content subclasses.
/// </remarks>
public class TextContentBlock : RichContentBlock
{
}
