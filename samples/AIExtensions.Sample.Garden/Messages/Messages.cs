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
/// Request that the chat starts a fresh session (clears the conversation).
/// </summary>
public sealed class StartNewChatSessionMessage;
