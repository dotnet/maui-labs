using System.Collections.ObjectModel;

namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// One message in a <see cref="ChatConversation"/>: who sent it, when, its ordered
/// <see cref="Contents"/>, and its delivery <see cref="Status"/>.
/// </summary>
/// <remarks>
/// <para>
/// A message owns an ordered, mutable content collection so a transport can append content (a second
/// text block, an image) as it arrives. Adding, removing, and mutating content is observable, and a
/// conversation republishes those signals through <see cref="ChatConversation.Subscribe"/>.
/// </para>
/// <para>Instances are single-thread affine and are not thread-safe. Mutate them on the UI thread only.</para>
/// </remarks>
public class ConversationMessage : BindableObject
{
    /// <summary>Backing property for <see cref="Status"/>.</summary>
    public static readonly BindableProperty StatusProperty =
        BindableProperty.Create(
            nameof(Status),
            typeof(ConversationMessageStatus),
            typeof(ConversationMessage),
            ConversationMessageStatus.Draft);

    /// <summary>Backing property for <see cref="ErrorText"/>.</summary>
    public static readonly BindableProperty ErrorTextProperty =
        BindableProperty.Create(nameof(ErrorText), typeof(string), typeof(ConversationMessage));

    /// <summary>Creates a message with no content.</summary>
    /// <param name="participant">The participant that authored the message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="participant"/> is <see langword="null"/>.</exception>
    public ConversationMessage(ChatParticipant participant)
        : this(participant, id: null, createdAt: null)
    {
    }

    /// <summary>Creates a message with no content, an explicit identity, and an explicit timestamp.</summary>
    /// <param name="participant">The participant that authored the message.</param>
    /// <param name="id">A stable identifier. When <see langword="null"/>, a new unique identifier is generated.</param>
    /// <param name="createdAt">The creation timestamp. When <see langword="null"/>, <see cref="DateTimeOffset.Now"/> is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="participant"/> is <see langword="null"/>.</exception>
    public ConversationMessage(
        ChatParticipant participant,
        string? id,
        DateTimeOffset? createdAt)
    {
        ArgumentNullException.ThrowIfNull(participant);

        Participant = participant;
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id;
        CreatedAt = createdAt ?? DateTimeOffset.Now;
        Contents = [];
    }

    /// <summary>Creates a message that starts with a single <see cref="TextMessageContent"/>.</summary>
    /// <param name="participant">The participant that authored the message.</param>
    /// <param name="text">The initial text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="participant"/> is <see langword="null"/>.</exception>
    public ConversationMessage(ChatParticipant participant, string? text)
        : this(participant, id: null, createdAt: null) => Contents.Add(new TextMessageContent(text));

    /// <summary>Creates a message that starts with a single <see cref="TextMessageContent"/>.</summary>
    /// <param name="participant">The participant that authored the message.</param>
    /// <param name="text">The initial text.</param>
    /// <param name="id">A stable identifier. When <see langword="null"/>, a new unique identifier is generated.</param>
    /// <param name="createdAt">The creation timestamp. When <see langword="null"/>, <see cref="DateTimeOffset.Now"/> is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="participant"/> is <see langword="null"/>.</exception>
    public ConversationMessage(
        ChatParticipant participant,
        string? text,
        string? id,
        DateTimeOffset? createdAt)
        : this(participant, id, createdAt) => Contents.Add(new TextMessageContent(text));

    /// <summary>Gets the stable identifier of this message. Never changes.</summary>
    public string Id { get; }

    /// <summary>Gets the participant that authored this message.</summary>
    public ChatParticipant Participant { get; }

    /// <summary>Gets when this message was created.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the ordered, mutable content of this message. Each entry is projected as one row.</summary>
    public ObservableCollection<MessageContent> Contents { get; }

    /// <summary>Gets or sets the delivery state of this message.</summary>
    public ConversationMessageStatus Status
    {
        get => (ConversationMessageStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>
    /// Gets or sets an optional, already user-safe failure summary shown when <see cref="Status"/> is
    /// <see cref="ConversationMessageStatus.Failed"/>. Never assign raw exception text.
    /// </summary>
    public string? ErrorText
    {
        get => (string?)GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    /// <summary>Adds content to the end of <see cref="Contents"/> and returns it for chaining.</summary>
    /// <typeparam name="T">The content type.</typeparam>
    /// <param name="content">The content to add.</param>
    /// <returns>The same <paramref name="content"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    public T AddContent<T>(T content)
        where T : MessageContent
    {
        ArgumentNullException.ThrowIfNull(content);

        Contents.Add(content);
        return content;
    }
}
