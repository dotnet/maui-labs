using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Selects assistant text that is a JSON object, such as a structured response, so it renders with its own view
/// instead of as text.
/// </summary>
/// <remarks>
/// The chat view selects templates again whenever a block changes, so streamed JSON switches to this template as
/// soon as its first character arrives.
/// </remarks>
public sealed class StructuredResponseTemplate : BlockTemplate
{
    /// <summary>Initializes the template for assistant text blocks.</summary>
    public StructuredResponseTemplate()
    {
        BlockType = typeof(TextContentBlock);
        Role = "assistant";
    }

    /// <inheritdoc />
    public override bool When(ContentBlock block) =>
        base.When(block) &&
        block is RichContentBlock text &&
        StructuredResponseFormatter.LooksLikeJsonObject(text.RawText);
}
