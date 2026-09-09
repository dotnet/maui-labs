using Microsoft.Maui.AI.Chat;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// A purely visual "the agent is working" placeholder (e.g. a spinner with "Thinking…").
/// </summary>
/// <remarks>
/// This is a UI-only block: the engine never produces it and it never enters <see cref="AgentContext"/>'s
/// turns or the persistable message thread. <see cref="MessageListView"/> injects it into its item list
/// while <see cref="ConversationStatus.Streaming"/> and removes it once real content is the tail — so it
/// renders inline like a message without polluting the conversation history.
/// </remarks>
public sealed class ThinkingContentBlock : ContentBlock
{
    public ThinkingContentBlock(string text = "Thinking…")
    {
        Text = text;
    }

    public string Text { get; }
}
