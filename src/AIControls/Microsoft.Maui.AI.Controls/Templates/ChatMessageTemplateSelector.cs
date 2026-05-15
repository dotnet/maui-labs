namespace Microsoft.Maui.AI.Controls.Templates;

/// <summary>
/// Picks a message <see cref="DataTemplate"/> based on <see cref="ChatMessageViewModel.Role"/>.
/// Template properties are set by the <see cref="AgentChatView"/> from its BindableProperties.
/// </summary>
public sealed class ChatMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? UserTemplate { get; set; }
    public DataTemplate? AssistantTemplate { get; set; }
    public DataTemplate? ToolTemplate { get; set; }
    public DataTemplate? SystemTemplate { get; set; }
    public DataTemplate? ErrorTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        if (item is ChatMessageViewModel msg)
        {
            if (msg.Role == Microsoft.Extensions.AI.ChatRole.User)
                return UserTemplate!;
            if (msg.Role == Microsoft.Extensions.AI.ChatRole.Assistant)
                return AssistantTemplate!;
            if (msg.Role == Microsoft.Extensions.AI.ChatRole.Tool)
                return ToolTemplate!;
            if (msg.Role == Microsoft.Extensions.AI.ChatRole.System)
                return SystemTemplate!;

            // Check for "error" role by name
            if (string.Equals(msg.Role.Value, "error", StringComparison.OrdinalIgnoreCase))
                return ErrorTemplate!;

            return AssistantTemplate!;
        }

        return AssistantTemplate!;
    }
}
