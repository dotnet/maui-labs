using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Chat.Presentation;

/// <summary>
/// A purely visual error placeholder that lets a failed turn render as a bubble in the conversation.
/// </summary>
/// <remarks>
/// This is a UI-only block: the engine surfaces failures via <see cref="ConversationStatus.Error"/> and
/// <see cref="AgentContext.Error"/> only. The presentation injects one of these into its item
/// list when the session enters the error state and leaves it there (errors stick in the scrollback),
/// without ever adding it to the engine's turns or the persistable message thread.
/// </remarks>
public class ErrorContentBlock : ContentBlock
{
    /// <summary>The safe message shown when the agent failure is not suitable for display.</summary>
    public const string DefaultUserMessage = "Something went wrong. Please try again.";

    /// <summary>Creates an error presentation block.</summary>
    public ErrorContentBlock(string message)
    {
        Message = message;
    }

    /// <summary>Gets the safe message to render.</summary>
    public string Message { get; }
}
