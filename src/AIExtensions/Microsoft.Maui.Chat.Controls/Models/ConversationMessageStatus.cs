namespace Microsoft.Maui.Chat.Controls;

/// <summary>The delivery state of a <see cref="ConversationMessage"/>.</summary>
/// <remarks>
/// The states are ordered from composition to acknowledgement. A transport that does not report a
/// state simply never sets it; views only render the states they are given.
/// </remarks>
public enum ConversationMessageStatus
{
    /// <summary>Composed locally and not submitted yet.</summary>
    Draft,

    /// <summary>Submitted and awaiting confirmation.</summary>
    Sending,

    /// <summary>Accepted by the transport.</summary>
    Sent,

    /// <summary>Delivered to the other participants.</summary>
    Delivered,

    /// <summary>Seen by the other participants.</summary>
    Read,

    /// <summary>Could not be sent. <see cref="ConversationMessage.ErrorText"/> may carry a safe summary.</summary>
    Failed,
}
