using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Matches reasoning that the provider sent only encrypted, which has no text to show. Its template has no view, so
/// the chat shows nothing for it.
/// </summary>
/// <remarks>
/// The engine creates a reasoning block with its first text or its encrypted data, and starts a new block for text that
/// follows encrypted data, so whether a block is encrypted is known when the chat selects its template.
/// </remarks>
public sealed class EncryptedReasoningMatch : BlockMatch
{
    /// <inheritdoc />
    public override bool Matches(ContentBlock block) => block is ReasoningContentBlock { IsEncrypted: true };
}
