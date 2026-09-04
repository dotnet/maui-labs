namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>A labeled prompt suggestion shown while the conversation is empty.</summary>
public sealed class ChatSuggestion : Microsoft.Maui.Chat.Controls.ChatSuggestion
{
    public ChatSuggestion(string label, string prompt)
        : base(label, prompt)
    {
    }
}
