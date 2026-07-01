using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// A purely visual error placeholder that lets a failed turn render as a bubble in the conversation.
/// </summary>
/// <remarks>
/// This is a UI-only block: the engine surfaces failures via <see cref="ConversationStatus.Error"/> and
/// <see cref="AgentContext.Error"/> only. <see cref="MessageListView"/> injects one of these into its item
/// list when the session enters the error state and leaves it there (errors stick in the scrollback),
/// without ever adding it to the engine's turns or the persistable message thread.
/// </remarks>
public sealed class ErrorContentBlock : ContentBlock
{
    public ErrorContentBlock(string message)
    {
        Message = message;
    }

    public string Message { get; }
}
