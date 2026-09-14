namespace Microsoft.Maui.Chat;

/// <summary>
/// A participant in a <see cref="ChatConversation"/>: a stable <see cref="Id"/> plus the bindable
/// presentation data (<see cref="DisplayName"/>, <see cref="Avatar"/>, <see cref="Kind"/>) that chat
/// controls render.
/// </summary>
/// <remarks>
/// Instances are single-thread affine and are not thread-safe: create, read, and mutate them on the
/// UI thread only. <see cref="Id"/> never changes so projections can group by participant cheaply,
/// while the presentation properties are observable and may change at any time.
/// </remarks>
public class ChatParticipant : ObservableChatObject
{
    private string _displayName = string.Empty;
    private object? _avatar;
    private ChatParticipantKind _kind = ChatParticipantKind.Remote;

    /// <summary>Creates a participant.</summary>
    /// <param name="id">A stable identifier that is unique within the conversation.</param>
    /// <param name="displayName">The name shown next to the participant's messages. Defaults to <paramref name="id"/>.</param>
    /// <param name="kind">What the participant represents. Defaults to <see cref="ChatParticipantKind.Remote"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is <see langword="null"/>, empty, or whitespace.</exception>
    public ChatParticipant(
        string id,
        string? displayName = null,
        ChatParticipantKind kind = ChatParticipantKind.Remote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Id = id;
        DisplayName = displayName ?? id;
        Kind = kind;
    }

    /// <summary>Gets the stable identifier of this participant. Never changes.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the name displayed next to this participant's messages.</summary>
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value ?? string.Empty))
                OnPropertyChanged(nameof(Initials));
        }
    }

    /// <summary>
    /// Gets or sets the optional renderer-native avatar value. For example, native renderers can assign
    /// their image value, while Blazor renderers can assign a URL or data URI. When
    /// <see langword="null"/>, views fall back to <see cref="Initials"/>.
    /// </summary>
    public object? Avatar
    {
        get => _avatar;
        set => SetProperty(ref _avatar, value);
    }

    /// <summary>Gets or sets what this participant represents.</summary>
    public ChatParticipantKind Kind
    {
        get => _kind;
        set
        {
            if (SetProperty(ref _kind, value))
                OnPropertyChanged(nameof(IsLocal));
        }
    }

    /// <summary>Gets whether this participant is the local user (<see cref="ChatParticipantKind.Local"/>).</summary>
    public bool IsLocal => Kind == ChatParticipantKind.Local;

    /// <summary>
    /// Gets up to two uppercase initials derived from <see cref="DisplayName"/>, used by the default
    /// avatar when <see cref="Avatar"/> is not set. Returns <c>"?"</c> when no name is available.
    /// </summary>
    public string Initials
    {
        get
        {
            var name = DisplayName;
            if (string.IsNullOrWhiteSpace(name))
                return "?";

            Span<char> initials = stackalloc char[2];
            var count = 0;
            var atWordStart = true;

            foreach (var c in name)
            {
                if (char.IsWhiteSpace(c))
                {
                    atWordStart = true;
                    continue;
                }

                if (atWordStart && char.IsLetterOrDigit(c))
                {
                    initials[count++] = char.ToUpperInvariant(c);
                    if (count == 2)
                        break;
                }

                atWordStart = false;
            }

            return count == 0 ? "?" : new string(initials[..count]);
        }
    }
}
