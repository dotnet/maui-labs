namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// A ready-to-use in-memory <see cref="ChatConversation"/> with the small set of mutation methods a
/// chat UI actually needs, plus a pluggable send behaviour.
/// </summary>
/// <remarks>
/// <para>
/// Set <see cref="SendHandler"/> to route drafts to a transport, or override
/// <see cref="ChatConversation.SendCoreAsync"/> for full control. With neither, sending appends the
/// draft as a message from <see cref="ChatConversation.LocalParticipant"/>, which is enough for
/// prototypes, samples, and tests.
/// </para>
/// <para>Like every model in this package it is single-thread affine and not thread-safe.</para>
/// </remarks>
public class ObservableChatConversation : ChatConversation
{
    /// <summary>Creates an empty conversation.</summary>
    public ObservableChatConversation()
    {
    }

    /// <summary>Creates a conversation and registers <paramref name="localParticipant"/> as the local user.</summary>
    /// <param name="localParticipant">The participant that represents this device.</param>
    /// <exception cref="ArgumentNullException"><paramref name="localParticipant"/> is <see langword="null"/>.</exception>
    public ObservableChatConversation(ChatParticipant localParticipant)
    {
        ArgumentNullException.ThrowIfNull(localParticipant);

        LocalParticipant = localParticipant;
        Participants.Add(localParticipant);
    }

    /// <summary>
    /// Gets or sets the send behaviour. Receives the conversation, the draft, and a cancellation token,
    /// and returns whether the draft was accepted. When <see langword="null"/>, the draft is appended as
    /// a local message.
    /// </summary>
    public Func<ObservableChatConversation, ChatDraft, CancellationToken, Task<bool>>? SendHandler { get; set; }

    /// <summary>Appends a message and publishes <see cref="ChatConversationChangeKind.MessageAdded"/>.</summary>
    /// <param name="message">The message to append.</param>
    /// <returns>The same <paramref name="message"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    public ConversationMessage AddMessage(ConversationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        MessageList.Add(message);
        return message;
    }

    /// <summary>Appends a text message from <paramref name="participant"/>.</summary>
    /// <param name="participant">The author.</param>
    /// <param name="text">The message text.</param>
    /// <returns>The message that was appended.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="participant"/> is <see langword="null"/>.</exception>
    public ConversationMessage AddMessage(ChatParticipant participant, string? text)
    {
        ArgumentNullException.ThrowIfNull(participant);

        return AddMessage(new ConversationMessage(participant, text));
    }

    /// <summary>Removes a message and publishes <see cref="ChatConversationChangeKind.MessageRemoved"/>.</summary>
    /// <param name="message">The message to remove.</param>
    /// <returns><see langword="true"/> when the message was present.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    public bool RemoveMessage(ConversationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return MessageList.Remove(message);
    }

    /// <summary>Appends content to a message and publishes <see cref="ChatConversationChangeKind.ContentAdded"/>.</summary>
    /// <typeparam name="T">The content type.</typeparam>
    /// <param name="message">The message to append to.</param>
    /// <param name="content">The content to append.</param>
    /// <returns>The same <paramref name="content"/> instance.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public T AddContent<T>(ConversationMessage message, T content)
        where T : MessageContent
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(content);

        message.Contents.Add(content);
        return content;
    }

    /// <summary>
    /// Publishes <see cref="ChatConversationChangeKind.ContentChanged"/> for content that was mutated
    /// through a path that does not raise <see cref="MessageContent.ContentChanged"/> itself.
    /// </summary>
    /// <param name="message">The message that owns the content.</param>
    /// <param name="content">The content that changed.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public void NotifyContentChanged(ConversationMessage message, MessageContent content)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(content);

        RaiseChange(ChatConversationChange.ContentChanged(message, content));
    }

    /// <summary>Sets <see cref="ChatConversation.Status"/> and publishes <see cref="ChatConversationChangeKind.StatusChanged"/>.</summary>
    /// <param name="status">The new status.</param>
    public void SetStatus(ChatConversationStatus status) => Status = status;

    /// <summary>
    /// Clears the messages, typing participants, and error state, and publishes
    /// <see cref="ChatConversationChangeKind.Reset"/>. Participants and
    /// <see cref="ChatConversation.LocalParticipant"/> are kept.
    /// </summary>
    public void Reset()
    {
        TypingParticipants.Clear();
        MessageList.Clear();
        Status = ChatConversationStatus.Idle;
    }

    /// <inheritdoc />
    protected override Task<bool> SendCoreAsync(ChatDraft draft, CancellationToken cancellationToken)
    {
        if (SendHandler is { } handler)
            return handler(this, draft, cancellationToken);

        var participant = LocalParticipant;
        if (participant is null)
            return Task.FromResult(false);

        var message = new ConversationMessage(participant)
        {
            Status = ConversationMessageStatus.Sent,
        };

        foreach (var content in draft.CreateContents())
            message.Contents.Add(content);

        AddMessage(message);
        return Task.FromResult(true);
    }
}
