using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Matches assistant text that is a JSON object, such as a structured response, so it renders with its own view
/// instead of as text.
/// </summary>
/// <remarks>
/// A list selects a block's template once, when the block joins its turn. The engine creates a text block from the first
/// text the model streams, so JSON is recognized from its first character.
/// </remarks>
public sealed class StructuredResponseMatch : BlockMatch
{
    /// <inheritdoc />
    public override bool Matches(ContentBlock block) =>
        block is TextContentBlock text &&
        text.Role == ChatRole.Assistant &&
        StructuredResponseFormatter.LooksLikeJsonObject(text.RawText);
}
