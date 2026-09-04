namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// One projected row: a single <see cref="MessageContent"/> in the context of its
/// <see cref="ConversationMessage"/>, participant, and neighbours.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ChatMessagesView"/> projects every message into one item per content, so the list stays
/// flat and virtualized — there is never a nested list inside a cell. The grouping flags are computed
/// once while projecting and refreshed in place when neighbours change, so templates and views never
/// scan the conversation to find out where a row sits.
/// </para>
/// <para>Items are single-thread affine and are not thread-safe.</para>
/// </remarks>
public class ChatContentItem : BindableObject
{
    private bool _isOutgoing;
    private bool _isFirstInMessage = true;
    private bool _isLastInMessage = true;
    private bool _isFirstFromParticipant = true;
    private bool _isLastFromParticipant = true;
    private ChatAppearance _appearance = ChatAppearance.Default;

    /// <summary>Creates a projected row.</summary>
    /// <param name="message">The message that owns the content.</param>
    /// <param name="content">The content this row renders.</param>
    /// <param name="conversation">The conversation the message belongs to, when known.</param>
    /// <param name="appearance">The styling source. Defaults to <see cref="ChatAppearance.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    public ChatContentItem(
        ConversationMessage message,
        MessageContent content,
        ChatConversation? conversation = null,
        ChatAppearance? appearance = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(content);

        Message = message;
        Content = content;
        Conversation = conversation;
        _appearance = appearance ?? ChatAppearance.Default;
        _isOutgoing = IsOutgoingFor(conversation, message.Participant);
    }

    /// <summary>Gets the conversation this row belongs to, when the host supplied one.</summary>
    public ChatConversation? Conversation { get; }

    /// <summary>Gets the message that owns <see cref="Content"/>.</summary>
    public ConversationMessage Message { get; }

    /// <summary>Gets the content this row renders.</summary>
    public MessageContent Content { get; }

    /// <summary>Gets the participant that authored <see cref="Message"/>.</summary>
    public ChatParticipant Participant => Message.Participant;

    /// <summary>Gets the timestamp of <see cref="Message"/>.</summary>
    public DateTimeOffset Timestamp => Message.CreatedAt;

    /// <summary>Gets whether this row belongs to the local participant and renders trailing-aligned.</summary>
    public bool IsOutgoing
    {
        get => _isOutgoing;
        private set => SetFlag(ref _isOutgoing, value, nameof(IsOutgoing), nameof(IsIncoming));
    }

    /// <summary>Gets whether this row renders leading-aligned. The inverse of <see cref="IsOutgoing"/>.</summary>
    public bool IsIncoming => !_isOutgoing;

    /// <summary>Gets whether this is the first content of its message.</summary>
    public bool IsFirstInMessage
    {
        get => _isFirstInMessage;
        private set => SetFlag(ref _isFirstInMessage, value, nameof(IsFirstInMessage));
    }

    /// <summary>Gets whether this is the last content of its message.</summary>
    public bool IsLastInMessage
    {
        get => _isLastInMessage;
        private set => SetFlag(ref _isLastInMessage, value, nameof(IsLastInMessage));
    }

    /// <summary>
    /// Gets whether this is the first row of a run of consecutive messages from the same participant.
    /// The default views show the avatar and participant name only on this row.
    /// </summary>
    public bool IsFirstFromParticipant
    {
        get => _isFirstFromParticipant;
        private set => SetFlag(ref _isFirstFromParticipant, value, nameof(IsFirstFromParticipant));
    }

    /// <summary>
    /// Gets whether this is the last row of a run of consecutive messages from the same participant.
    /// The default views show the timestamp and delivery status only on this row.
    /// </summary>
    public bool IsLastFromParticipant
    {
        get => _isLastFromParticipant;
        private set => SetFlag(ref _isLastFromParticipant, value, nameof(IsLastFromParticipant));
    }

    /// <summary>Gets or sets the styling source for this row. Never <see langword="null"/>.</summary>
    public ChatAppearance Appearance
    {
        get => _appearance;
        set
        {
            var appearance = value ?? ChatAppearance.Default;
            if (ReferenceEquals(_appearance, appearance))
                return;

            _appearance = appearance;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Updates the grouping flags in place. The projecting control calls this instead of replacing the
    /// item, so a cell is never rebuilt just because a neighbour changed.
    /// </summary>
    /// <param name="isOutgoing">Whether the row belongs to the local participant.</param>
    /// <param name="isFirstInMessage">Whether this is the first content of its message.</param>
    /// <param name="isLastInMessage">Whether this is the last content of its message.</param>
    /// <param name="isFirstFromParticipant">Whether this starts a run from the same participant.</param>
    /// <param name="isLastFromParticipant">Whether this ends a run from the same participant.</param>
    public void UpdateFlags(
        bool isOutgoing,
        bool isFirstInMessage,
        bool isLastInMessage,
        bool isFirstFromParticipant,
        bool isLastFromParticipant)
    {
        IsOutgoing = isOutgoing;
        IsFirstInMessage = isFirstInMessage;
        IsLastInMessage = isLastInMessage;
        IsFirstFromParticipant = isFirstFromParticipant;
        IsLastFromParticipant = isLastFromParticipant;
    }

    /// <summary>
    /// Signals that <see cref="Content"/> changed in place. Raises <see cref="BindableObject.PropertyChanged"/>
    /// for <see cref="Content"/> so bound views re-read it without the row being replaced.
    /// </summary>
    public void NotifyContentUpdated() => OnPropertyChanged(nameof(Content));

    /// <summary>
    /// Signals that <see cref="Message"/> changed — its status or error text, typically — so views can
    /// re-render the footer without the row being replaced.
    /// </summary>
    public void NotifyMessageUpdated() => OnPropertyChanged(nameof(Message));

    /// <summary>Gets whether <paramref name="participant"/> should render as outgoing in <paramref name="conversation"/>.</summary>
    /// <param name="conversation">The conversation, or <see langword="null"/> when unknown.</param>
    /// <param name="participant">The participant to test.</param>
    /// <returns><see langword="true"/> when the participant is the conversation's local participant, or is <see cref="ChatParticipantKind.Local"/>.</returns>
    public static bool IsOutgoingFor(ChatConversation? conversation, ChatParticipant? participant)
    {
        if (participant is null)
            return false;

        var local = conversation?.LocalParticipant;
        return local is not null
            ? string.Equals(local.Id, participant.Id, StringComparison.Ordinal)
            : participant.IsLocal;
    }

    private void SetFlag(ref bool field, bool value, string propertyName, string? alsoNotify = null)
    {
        if (field == value)
            return;

        field = value;
        OnPropertyChanged(propertyName);

        if (alsoNotify is not null)
            OnPropertyChanged(alsoNotify);
    }
}
