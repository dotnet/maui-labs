namespace Microsoft.Maui.CopilotChat;

/// <summary>
/// Identifies the kind of chat message for template selection.
/// </summary>
public enum ChatMessageKind
{
    User,
    Assistant,
    Tool,
    System,
    Error,
}
