namespace Microsoft.Maui.CopilotChat.Controls;

/// <summary>
/// Picks a message <see cref="DataTemplate"/> based on <see cref="CopilotChatMessage.Kind"/>.
/// Template properties are set by the <see cref="CopilotChatView"/> from its BindableProperties.
/// </summary>
public sealed class ChatMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? UserTemplate { get; set; }
    public DataTemplate? AssistantTemplate { get; set; }
    public DataTemplate? ToolTemplate { get; set; }
    public DataTemplate? SystemTemplate { get; set; }
    public DataTemplate? ErrorTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
        item is CopilotChatMessage msg
            ? msg.Kind switch
            {
                ChatMessageKind.User => UserTemplate!,
                ChatMessageKind.Assistant => AssistantTemplate!,
                ChatMessageKind.Tool => ToolTemplate!,
                ChatMessageKind.System => SystemTemplate!,
                ChatMessageKind.Error => ErrorTemplate!,
                _ => AssistantTemplate!,
            }
            : AssistantTemplate!;
}
