using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Matches assistant text that is a JSON object, such as a structured response, so it renders with its own view
/// instead of as text.
/// </summary>
/// <remarks>
/// The chat view selects templates again whenever a block changes, so streamed JSON switches to this match's template
/// as soon as its first character arrives.
/// </remarks>
public sealed class StructuredResponseMatch : BlockMatch
{
    /// <inheritdoc />
    public override bool Matches(ContentBlock block) =>
        block is TextContentBlock text &&
        text.Role == ChatRole.Assistant &&
        StructuredResponseFormatter.LooksLikeJsonObject(text.RawText);
}
