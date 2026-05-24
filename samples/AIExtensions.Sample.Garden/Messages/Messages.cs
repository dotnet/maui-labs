using AIExtensions.Sample.Garden.ViewModels;

namespace AIExtensions.Sample.Garden.Messages;

/// <summary>
/// Broadcast when the cart contents change (add, remove, clear, qty change).
/// </summary>
public sealed class CartChangedMessage;

/// <summary>
/// Broadcast after the AI chat completes a full turn (response + tool calls).
/// </summary>
public sealed class ChatTurnCompletedMessage;

/// <summary>
/// Broadcast when a new chat message is appended so views can scroll.
/// </summary>
public sealed class ChatMessageAddedMessage(ChatMessageViewModel message)
{
    public ChatMessageViewModel Message { get; } = message;
}

/// <summary>
/// Request that the chat VM starts a fresh session (clears history + messages).
/// </summary>
public sealed class StartNewChatSessionMessage;
