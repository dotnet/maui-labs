namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Describes how a piece of AI content should be treated in a chat transcript.
/// </summary>
public enum ContentRole
{
    User,
    Assistant,
    Tool,
    Approval,
    Error,
}
