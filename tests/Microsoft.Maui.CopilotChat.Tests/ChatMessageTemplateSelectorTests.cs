using Microsoft.Maui.CopilotChat.Controls;
using Xunit;

namespace Microsoft.Maui.CopilotChat.Tests;

public class ChatMessageTemplateSelectorTests
{
    [Theory]
    [InlineData(ChatMessageKind.User)]
    [InlineData(ChatMessageKind.Assistant)]
    [InlineData(ChatMessageKind.Tool)]
    [InlineData(ChatMessageKind.System)]
    [InlineData(ChatMessageKind.Error)]
    public void OnSelectTemplate_ReturnsCorrectTemplate(ChatMessageKind kind)
    {
        var userTemplate = new DataTemplate();
        var assistantTemplate = new DataTemplate();
        var toolTemplate = new DataTemplate();
        var systemTemplate = new DataTemplate();
        var errorTemplate = new DataTemplate();

        var selector = new ChatMessageTemplateSelector
        {
            UserTemplate = userTemplate,
            AssistantTemplate = assistantTemplate,
            ToolTemplate = toolTemplate,
            SystemTemplate = systemTemplate,
            ErrorTemplate = errorTemplate,
        };

        var message = new CopilotChatMessage(kind, "test");
        var result = selector.SelectTemplate(message, null!);

        var expected = kind switch
        {
            ChatMessageKind.User => userTemplate,
            ChatMessageKind.Assistant => assistantTemplate,
            ChatMessageKind.Tool => toolTemplate,
            ChatMessageKind.System => systemTemplate,
            ChatMessageKind.Error => errorTemplate,
            _ => assistantTemplate,
        };

        Assert.Same(expected, result);
    }
}
