namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// An immutable description of one change to a <see cref="ChatConversation"/>, delivered synchronously
/// and in order to every <see cref="ChatConversation.Subscribe">subscriber</see>.
/// </summary>
/// <remarks>
/// Which members carry a value depends on <see cref="Kind"/>. Use the static factory methods to create
/// instances; a default instance describes <see cref="ChatConversationChangeKind.Reset"/>.
/// </remarks>
public readonly struct ChatConversationChange
{
    private ChatConversationChange(
        ChatConversationChangeKind kind,
        ConversationMessage? message,
        MessageContent? content,
        int index,
        ChatConversationStatus status)
    {
        Kind = kind;
        Message = message;
        Content = content;
        Index = index;
        Status = status;
    }

    /// <summary>Gets what this change describes.</summary>
    public ChatConversationChangeKind Kind { get; }

    /// <summary>Gets the affected message, or <see langword="null"/> for <see cref="ChatConversationChangeKind.Reset"/> and <see cref="ChatConversationChangeKind.StatusChanged"/>.</summary>
    public ConversationMessage? Message { get; }

    /// <summary>Gets the affected content for the <c>Content*</c> kinds, otherwise <see langword="null"/>.</summary>
    public MessageContent? Content { get; }

    /// <summary>Gets the message or content index for the <c>*Added</c> and <c>*Removed</c> kinds, otherwise <c>-1</c>.</summary>
    public int Index { get; }

    /// <summary>Gets the new conversation status for <see cref="ChatConversationChangeKind.StatusChanged"/>, otherwise the status at the time of the change.</summary>
    public ChatConversationStatus Status { get; }

    /// <summary>Creates a <see cref="ChatConversationChangeKind.Reset"/> change.</summary>
    /// <returns>The change.</returns>
    public static ChatConversationChange Reset() =>
        new(ChatConversationChangeKind.Reset, null, null, -1, ChatConversationStatus.Idle);

    /// <summary>Creates a <see cref="ChatConversationChangeKind.MessageAdded"/> change.</summary>
    /// <param name="message">The message that was added.</param>
    /// <param name="index">The index it was inserted at.</param>
    /// <returns>The change.</returns>
    public static ChatConversationChange MessageAdded(ConversationMessage message, int index) =>
        new(ChatConversationChangeKind.MessageAdded, message, null, index, ChatConversationStatus.Idle);

    /// <summary>Creates a <see cref="ChatConversationChangeKind.MessageRemoved"/> change.</summary>
    /// <param name="message">The message that was removed.</param>
    /// <param name="index">The index it was removed from.</param>
    /// <returns>The change.</returns>
    public static ChatConversationChange MessageRemoved(ConversationMessage message, int index) =>
        new(ChatConversationChangeKind.MessageRemoved, message, null, index, ChatConversationStatus.Idle);

    /// <summary>Creates a <see cref="ChatConversationChangeKind.MessageChanged"/> change.</summary>
    /// <param name="message">The message whose own state changed.</param>
    /// <returns>The change.</returns>
    public static ChatConversationChange MessageChanged(ConversationMessage message) =>
        new(ChatConversationChangeKind.MessageChanged, message, null, -1, ChatConversationStatus.Idle);

    /// <summary>Creates a <see cref="ChatConversationChangeKind.ContentAdded"/> change.</summary>
    /// <param name="message">The message that owns the content.</param>
    /// <param name="content">The content that was added.</param>
    /// <param name="index">The index it was inserted at.</param>
    /// <returns>The change.</returns>
    public static ChatConversationChange ContentAdded(
        ConversationMessage message,
        MessageContent content,
        int index) =>
        new(ChatConversationChangeKind.ContentAdded, message, content, index, ChatConversationStatus.Idle);

    /// <summary>Creates a <see cref="ChatConversationChangeKind.ContentRemoved"/> change.</summary>
    /// <param name="message">The message that owned the content.</param>
    /// <param name="content">The content that was removed.</param>
    /// <param name="index">The index it was removed from.</param>
    /// <returns>The change.</returns>
    public static ChatConversationChange ContentRemoved(
        ConversationMessage message,
        MessageContent content,
        int index) =>
        new(ChatConversationChangeKind.ContentRemoved, message, content, index, ChatConversationStatus.Idle);

    /// <summary>Creates a <see cref="ChatConversationChangeKind.ContentChanged"/> change.</summary>
    /// <param name="message">The message that owns the content.</param>
    /// <param name="content">The content that changed in place.</param>
    /// <returns>The change.</returns>
    public static ChatConversationChange ContentChanged(ConversationMessage message, MessageContent content) =>
        new(ChatConversationChangeKind.ContentChanged, message, content, -1, ChatConversationStatus.Idle);

    /// <summary>Creates a <see cref="ChatConversationChangeKind.StatusChanged"/> change.</summary>
    /// <param name="status">The new conversation status.</param>
    /// <returns>The change.</returns>
    public static ChatConversationChange StatusChanged(ChatConversationStatus status) =>
        new(ChatConversationChangeKind.StatusChanged, null, null, -1, status);
}
