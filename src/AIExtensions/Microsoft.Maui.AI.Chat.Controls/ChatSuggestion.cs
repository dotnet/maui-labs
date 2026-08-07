namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>A labeled prompt suggestion shown while the conversation is empty.</summary>
public sealed class ChatSuggestion
{
    public ChatSuggestion(string label, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        Label = label;
        Prompt = prompt;
    }

    public string Label { get; }

    public string Prompt { get; }

    public string? Icon { get; init; }
}
