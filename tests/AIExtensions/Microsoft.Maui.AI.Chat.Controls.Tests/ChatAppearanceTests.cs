using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;
using Microsoft.Maui.AI.Chat.Controls.Themes;
using Microsoft.Maui.Controls.Shapes;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class ChatAppearanceTests
{
    [Fact]
    public void BuiltInMessageView_TracksStandaloneMessageListAppearance()
    {
        var list = new MessageListView();
        var block = new TextContentBlock { Role = ChatRole.User };
        block.AppendText("Hello");
        var stroke = Color.FromArgb("#123456");

        list.ShowAvatars = true;
        list.AvatarSize = 36;
        list.UserDisplayName = "Morgan";
        list.AssistantDisplayName = "Sage";
        list.ShowTimestamps = true;
        list.BubbleCornerRadius = 20;
        list.BubbleStrokeThickness = 2;
        list.BubbleStrokeColor = stroke;
        list.MaxBubbleWidth = 480;

        var message = new ChatMessageView
        {
            ContentContext = new ContentContext(SessionFactory.Create(), block, list),
        };

        Assert.True(message.ShowAvatars);
        Assert.Equal(36, message.AvatarSize);
        Assert.Equal("Morgan", message.DisplayName);
        Assert.Equal("M", message.AvatarText);
        Assert.True(message.ShowTimestamp);
        Assert.Equal(20, message.BubbleCornerRadius);
        Assert.Equal(20, message.BubbleCornerRadii.BottomLeft);
        Assert.Equal(4, message.BubbleCornerRadii.BottomRight);
        Assert.Equal(2, message.BubbleStrokeThickness);
        Assert.Equal(stroke, message.BubbleStrokeColor);
        Assert.Equal(480, message.MaxBubbleWidth);
        Assert.Equal("Morgan: Hello", SemanticProperties.GetDescription(message));
        Assert.Equal("ChatMessage", message.AutomationId);

        block = new TextContentBlock { Role = ChatRole.Assistant };
        block.AppendText("Welcome");
        message.ContentContext = new ContentContext(SessionFactory.Create(), block, list);

        Assert.Equal("Sage", message.DisplayName);
        Assert.Equal("S", message.AvatarText);
        Assert.Equal(4, message.BubbleCornerRadii.BottomLeft);
        Assert.Equal(20, message.BubbleCornerRadii.BottomRight);
        Assert.Equal("Sage: Welcome", SemanticProperties.GetDescription(message));
        Assert.Equal("ChatMessage", message.AutomationId);
    }

    [Fact]
    public void CopilotChatView_ForwardsAppearanceToNestedMessageList()
    {
        var chat = new CopilotChatView();
        var list = new MessageListView();
        var stroke = Color.FromArgb("#654321");
        chat.AttachMessageListPart(list);

        chat.ShowAvatars = true;
        chat.AvatarSize = 40;
        chat.UserDisplayName = "Customer";
        chat.AssistantDisplayName = "Copilot";
        chat.ShowTimestamps = true;
        chat.BubbleCornerRadius = 18;
        chat.BubbleStrokeThickness = 3;
        chat.BubbleStrokeColor = stroke;
        chat.MaxBubbleWidth = 520;
        chat.UseDefaultContentTemplates = false;

        Assert.True(list.ShowAvatars);
        Assert.Equal(40, list.AvatarSize);
        Assert.Equal("Customer", list.UserDisplayName);
        Assert.Equal("Copilot", list.AssistantDisplayName);
        Assert.True(list.ShowTimestamps);
        Assert.Equal(18, list.BubbleCornerRadius);
        Assert.Equal(3, list.BubbleStrokeThickness);
        Assert.Equal(stroke, list.BubbleStrokeColor);
        Assert.Equal(520, list.MaxBubbleWidth);
        Assert.False(list.UseDefaultContentTemplates);
    }

    [Fact]
    public void InputAppearance_UpdatesAttachedTemplatePartsLive()
    {
        var chat = new CopilotChatView();
        var firstSendColor = Color.FromArgb("#112233");
        var secondSendColor = Color.FromArgb("#334455");
        var firstInputColor = Color.FromArgb("#556677");
        var secondInputColor = Color.FromArgb("#778899");
        var defaultSendColor = Color.FromArgb("#224466");
        var defaultInputColor = Color.FromArgb("#6688AA");
        var host = new ContentView();
        host.Resources[ChatThemeKeys.SendBackground] = defaultSendColor;
        host.Resources[ChatThemeKeys.InputBackground] = defaultInputColor;
        host.Content = chat;

        chat.SendButtonBackgroundColor = firstSendColor;
        chat.InputAreaBackgroundColor = firstInputColor;
        chat.InputAreaCornerRadius = 19;

        Assert.Equal(firstSendColor, chat.EffectiveSendButtonBackgroundColor);
        Assert.Equal(firstInputColor, chat.EffectiveInputAreaBackgroundColor);
        Assert.Equal(19, chat.InputAreaCornerRadius);

        chat.SendButtonBackgroundColor = secondSendColor;
        chat.InputAreaBackgroundColor = secondInputColor;
        chat.InputAreaCornerRadius = 27;

        Assert.Equal(secondSendColor, chat.EffectiveSendButtonBackgroundColor);
        Assert.Equal(secondInputColor, chat.EffectiveInputAreaBackgroundColor);
        Assert.Equal(27, chat.InputAreaCornerRadius);

        chat.SendButtonBackgroundColor = null;
        chat.InputAreaBackgroundColor = null;

        Assert.Equal(defaultSendColor, chat.EffectiveSendButtonBackgroundColor);
        Assert.Equal(defaultInputColor, chat.EffectiveInputAreaBackgroundColor);
    }

}
