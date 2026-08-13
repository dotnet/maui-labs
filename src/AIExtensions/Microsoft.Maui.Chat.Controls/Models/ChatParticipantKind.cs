namespace Microsoft.Maui.Chat.Controls;

/// <summary>Describes what kind of actor a <see cref="ChatParticipant"/> represents.</summary>
/// <remarks>
/// The kind is presentation metadata only: it drives default alignment, avatar styling, and the
/// templates that match a participant. It carries no transport or provider semantics.
/// </remarks>
public enum ChatParticipantKind
{
    /// <summary>The person using this device. Their messages render as outgoing.</summary>
    Local,

    /// <summary>Another human participant. Their messages render as incoming.</summary>
    Remote,

    /// <summary>A bot, assistant, or other automated participant. Their messages render as incoming.</summary>
    Agent,

    /// <summary>The conversation itself (joins, notices, separators) rather than an actor.</summary>
    System,
}
