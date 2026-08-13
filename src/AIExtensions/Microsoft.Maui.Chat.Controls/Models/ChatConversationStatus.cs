namespace Microsoft.Maui.Chat.Controls;

/// <summary>The overall state of a <see cref="ChatConversation"/>.</summary>
public enum ChatConversationStatus
{
    /// <summary>Nothing in flight. Sending is allowed.</summary>
    Idle,

    /// <summary>Work is in flight (sending, or a remote participant is responding). Sending is blocked.</summary>
    Busy,

    /// <summary>The conversation is waiting for the local participant to act.</summary>
    AwaitingInput,

    /// <summary>The conversation failed. Hosts surface an already user-safe message.</summary>
    Error,
}
