namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// What the composer is about to send: trimmed <see cref="Text"/> plus any picked
/// <see cref="Attachments"/>.
/// </summary>
/// <remarks>
/// A draft is immutable. <see cref="ChatConversation.SendAsync"/> receives it, decides whether to
/// accept it, and the composer clears its input only when the draft was accepted.
/// </remarks>
public sealed class ChatDraft
{
    private static readonly ChatAttachment[] NoAttachments = [];

    /// <summary>Creates a draft.</summary>
    /// <param name="text">The composed text. <see langword="null"/> is treated as empty; the value is trimmed.</param>
    /// <param name="attachments">The picked attachments, if any. <see langword="null"/> entries are ignored.</param>
    public ChatDraft(string? text, IEnumerable<ChatAttachment>? attachments = null)
    {
        Text = text?.Trim() ?? string.Empty;
        Attachments = attachments is null
            ? NoAttachments
            : [.. attachments.Where(static a => a is not null)];
    }

    /// <summary>Gets the trimmed text. Never <see langword="null"/>.</summary>
    public string Text { get; }

    /// <summary>Gets the attachments in pick order.</summary>
    public IReadOnlyList<ChatAttachment> Attachments { get; }

    /// <summary>Gets whether the draft has text.</summary>
    public bool HasText => Text.Length > 0;

    /// <summary>Gets whether the draft has neither text nor attachments, in which case it cannot be sent.</summary>
    public bool IsEmpty => !HasText && Attachments.Count == 0;

    /// <summary>
    /// Creates the message content this draft represents: the text (when present) followed by one
    /// <see cref="MediaMessageContent"/> per attachment.
    /// </summary>
    /// <returns>The ordered content, ready to add to a <see cref="ConversationMessage"/>.</returns>
    public IReadOnlyList<MessageContent> CreateContents()
    {
        var contents = new List<MessageContent>(Attachments.Count + 1);

        if (HasText)
            contents.Add(new TextMessageContent(Text));

        foreach (var attachment in Attachments)
            contents.Add(attachment.ToContent());

        return contents;
    }
}
