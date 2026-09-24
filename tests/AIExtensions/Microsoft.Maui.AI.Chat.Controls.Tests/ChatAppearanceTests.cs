using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class ChatAppearanceTests
{
    [Fact]
    public void CopilotChatView_ForwardsAppearanceToNestedMessageList()
    {
        var appearance = new ChatAppearance
        {
            ShowAvatars = true,
            AvatarSize = 40,
            ShowTimestamps = true,
            BubbleCornerRadius = 18,
            BubbleStrokeThickness = 3,
            BubbleStrokeColor = Color.FromArgb("#654321"),
            MaxBubbleWidth = 520,
        };
        var chat = new CopilotChatView
        {
            Appearance = appearance,
            UserDisplayName = "Customer",
            AssistantDisplayName = "Copilot",
            UseDefaultContentTemplates = false,
        };
        var list = new MessageListView();
        chat.AttachMessageListPart(list);

        Assert.Same(appearance, list.Appearance);
        Assert.False(list.UseDefaultContentTemplates);
    }

    [Fact]
    public void NestedMessageListSharesTheTypedTemplateCollection()
    {
        var chat = new CopilotChatView();
        var list = new MessageListView();
        var outerTemplate = new TextContentTemplate();
        chat.ContentTemplates.Add(outerTemplate);
        chat.AttachMessageListPart(list);

        list.ContentTemplates.Add(new ErrorContentTemplate());

        Assert.Equal(2, chat.ContentTemplates.Count);
        Assert.Same(outerTemplate, chat.ContentTemplates[0]);
        Assert.IsType<ErrorContentTemplate>(chat.ContentTemplates[1]);
    }

    [Fact]
    public void SharedShellStyles_AreAssignableOnCopilotChatView()
    {
        var chat = new CopilotChatView();
        var inputAreaStyle = new Style(typeof(Border));
        var inputEntryStyle = new Style(typeof(Entry));
        var attachButtonStyle = new Style(typeof(Button));
        var sendButtonStyle = new Style(typeof(Button));

        chat.InputAreaStyle = inputAreaStyle;
        chat.InputEntryStyle = inputEntryStyle;
        chat.AttachButtonStyle = attachButtonStyle;
        chat.SendButtonStyle = sendButtonStyle;
        chat.InputAreaCornerRadius = 19;

        Assert.Same(inputAreaStyle, chat.InputAreaStyle);
        Assert.Same(inputEntryStyle, chat.InputEntryStyle);
        Assert.Same(attachButtonStyle, chat.AttachButtonStyle);
        Assert.Same(sendButtonStyle, chat.SendButtonStyle);
        Assert.Equal(19, chat.InputAreaCornerRadius);
    }

}
