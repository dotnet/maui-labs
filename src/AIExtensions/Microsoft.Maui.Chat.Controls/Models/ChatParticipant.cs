namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// A participant in a <see cref="ChatConversation"/>: a stable <see cref="Id"/> plus the bindable
/// presentation data (<see cref="DisplayName"/>, <see cref="Avatar"/>, <see cref="Kind"/>) the chat
/// controls render.
/// </summary>
/// <remarks>
/// Instances are single-thread affine and are not thread-safe: create, read, and mutate them on the
/// UI thread only. <see cref="Id"/> never changes so projections can group by participant cheaply,
/// while the presentation properties are bindable and may change at any time.
/// </remarks>
public class ChatParticipant : BindableObject
{
    /// <summary>Backing property for <see cref="DisplayName"/>.</summary>
    public static readonly BindableProperty DisplayNameProperty =
        BindableProperty.Create(
            nameof(DisplayName),
            typeof(string),
            typeof(ChatParticipant),
            string.Empty,
            propertyChanged: static (bindable, _, _) =>
                ((ChatParticipant)bindable).OnPropertyChanged(nameof(Initials)),
            coerceValue: static (_, value) => value ?? string.Empty);

    /// <summary>Backing property for <see cref="Avatar"/>.</summary>
    public static readonly BindableProperty AvatarProperty =
        BindableProperty.Create(nameof(Avatar), typeof(ImageSource), typeof(ChatParticipant));

    /// <summary>Backing property for <see cref="Kind"/>.</summary>
    public static readonly BindableProperty KindProperty =
        BindableProperty.Create(
            nameof(Kind),
            typeof(ChatParticipantKind),
            typeof(ChatParticipant),
            ChatParticipantKind.Remote,
            propertyChanged: static (bindable, _, _) =>
                ((ChatParticipant)bindable).OnPropertyChanged(nameof(IsLocal)));

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
        get => (string)GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    /// <summary>Gets or sets the optional avatar image. When <see langword="null"/>, views fall back to <see cref="Initials"/>.</summary>
    public ImageSource? Avatar
    {
        get => (ImageSource?)GetValue(AvatarProperty);
        set => SetValue(AvatarProperty, value);
    }

    /// <summary>Gets or sets what this participant represents.</summary>
    public ChatParticipantKind Kind
    {
        get => (ChatParticipantKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
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
