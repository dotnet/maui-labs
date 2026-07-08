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
/// Broadcast when the rendering axis is toggled between the designed views and the raw block-preview
/// inspector. The chat view swaps to the preview template set (which visualizes every block's type and
/// values) in response.
/// </summary>
public sealed class ChatBlockPreviewModeChangedMessage(bool isPreview)
{
    public bool IsPreview { get; } = isPreview;
}

/// <summary>
/// Request that the chat starts a fresh conversation, carrying the desired handler mode. The chat view
/// clears the session when the mode is unchanged, or recreates it with the new handler set when the mode
/// differs — so "new chat" and "switch handlers" flow through one path. (Handlers are baked into a
/// session's pipeline, so changing them requires a new session.)
/// </summary>
public sealed class StartNewChatSessionMessage(bool useCustomHandlers)
{
    public bool UseCustomHandlers { get; } = useCustomHandlers;
}
