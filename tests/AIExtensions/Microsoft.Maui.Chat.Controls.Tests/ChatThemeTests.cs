using Microsoft.Maui.Chat.Controls.Themes;

namespace Microsoft.Maui.Chat.Controls.Tests;

/// <summary>
/// Covers the theme: the dictionaries load, expose the documented <c>MauiChat.*</c> keys, and can be
/// merged idempotently.
/// </summary>
public class ChatThemeTests
{
    [Fact]
    public void Theme_MergesTheThreeLayers()
    {
        var theme = new ChatControlsTheme();

        Assert.Equal(3, theme.MergedDictionaries.Count);
        Assert.Contains(theme.MergedDictionaries, dictionary => dictionary is ChatStyles);
        Assert.Contains(theme.MergedDictionaries, dictionary => dictionary is ChatMessagesTheme);
        Assert.Contains(theme.MergedDictionaries, dictionary => dictionary is ChatViewTheme);
    }

    [Fact]
    public void Theme_ExposesTheControlTemplates()
    {
        var theme = new ChatControlsTheme();

        Assert.True(theme.TryGetValue(ChatThemeKeys.ChatViewTemplate, out var chatTemplate));
        Assert.IsType<ControlTemplate>(chatTemplate);

        Assert.True(theme.TryGetValue(ChatThemeKeys.ChatMessagesViewTemplate, out var messagesTemplate));
        Assert.IsType<ControlTemplate>(messagesTemplate);
    }

    [Theory]
    [InlineData(ChatThemeKeys.IncomingBubbleStyle)]
    [InlineData(ChatThemeKeys.OutgoingBubbleStyle)]
    [InlineData(ChatThemeKeys.IncomingTextStyle)]
    [InlineData(ChatThemeKeys.OutgoingTextStyle)]
    [InlineData(ChatThemeKeys.IncomingSpanStyle)]
    [InlineData(ChatThemeKeys.OutgoingSpanStyle)]
    [InlineData(ChatThemeKeys.ParticipantNameStyle)]
    [InlineData(ChatThemeKeys.MetadataStyle)]
    [InlineData(ChatThemeKeys.AvatarStyle)]
    [InlineData(ChatThemeKeys.AvatarTextStyle)]
    [InlineData(ChatThemeKeys.FileCardStyle)]
    [InlineData(ChatThemeKeys.FileNameStyle)]
    [InlineData(ChatThemeKeys.FileDetailStyle)]
    [InlineData(ChatThemeKeys.SuggestionStyle)]
    [InlineData(ChatThemeKeys.AttachmentStyle)]
    [InlineData(ChatThemeKeys.InputAreaStyle)]
    [InlineData(ChatThemeKeys.InputEntryStyle)]
    [InlineData(ChatThemeKeys.SendButtonStyle)]
    [InlineData(ChatThemeKeys.AttachButtonStyle)]
    [InlineData(ChatThemeKeys.ErrorTextStyle)]
    [InlineData(ChatThemeKeys.TypingIndicatorStyle)]
    [InlineData(ChatThemeKeys.WelcomeIconStyle)]
    [InlineData(ChatThemeKeys.WelcomeMessageStyle)]
    public void Theme_DefinesEveryDocumentedStyle(string key)
    {
        var styles = new ChatStyles();

        Assert.True(styles.TryGetValue(key, out var value));
        Assert.IsType<Style>(value);
    }

    [Fact]
    public void Theme_KeysAreNamespaced()
    {
        var keys = new[]
        {
            ChatThemeKeys.ChatViewTemplate,
            ChatThemeKeys.ChatMessagesViewTemplate,
            ChatThemeKeys.IncomingBubbleStyle,
            ChatThemeKeys.SendButtonStyle,
        };

        Assert.All(keys, key => Assert.StartsWith("MauiChat.", key, StringComparison.Ordinal));
    }

    [Fact]
    public void BubbleStyles_TargetBordersAndCarryLightAndDarkColors()
    {
        var styles = new ChatStyles();
        var style = Assert.IsType<Style>(styles[ChatThemeKeys.IncomingBubbleStyle]);

        Assert.Equal(typeof(Border), style.TargetType);
        Assert.NotEmpty(style.Setters);
    }

    [Fact]
    public void EnsureLoaded_MergesOnceIntoTheGivenResources()
    {
        var resources = new ResourceDictionary();

        ChatControlsTheme.EnsureLoaded(resources);
        ChatControlsTheme.EnsureLoaded(resources);

        Assert.Single(resources.MergedDictionaries);
        Assert.IsType<ChatControlsTheme>(resources.MergedDictionaries.First());
    }

    [Fact]
    public void EnsureLoaded_WithNull_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ChatControlsTheme.EnsureLoaded(null!));

    [Fact]
    public void EnsureLoaded_WithoutAnApplication_IsANoOp() => ChatControlsTheme.EnsureLoaded();

    [Fact]
    public void UseChatControls_WithNullBuilder_Throws() =>
        Assert.Throws<ArgumentNullException>(() => AppBuilderExtensions.UseChatControls(null!));

    [Fact]
    public void MessagesView_AcceptsAControlTemplateBuiltInCode()
    {
        // A code-built template has no name scope, so no part resolves. The control must cope with that
        // instead of throwing, otherwise "the template is replaceable" would only be true from XAML.
        var view = new ChatMessagesView
        {
            ControlTemplate = new ControlTemplate(() => new Grid()),
        };

        Assert.NotNull(view.ControlTemplate);
        Assert.Empty(view.Items);
    }

    [Fact]
    public void ChatView_AcceptsAControlTemplateBuiltInCode()
    {
        var view = new ChatView
        {
            ControlTemplate = new ControlTemplate(() => new Grid()),
        };

        Assert.NotNull(view.ControlTemplate);
        Assert.True(view.ShowWelcome);
    }

    [Fact]
    public void ChatView_DefaultMessageListFactoryCreatesNeutralList() =>
        Assert.IsType<ChatMessagesView>(new FactoryChatView().CreateMessageList());

    [Fact]
    public void ChatView_DefaultTemplateExposesMultimodalActionParts()
    {
        var theme = new ChatControlsTheme();
        var view = new PartChatView
        {
            AllowAudioCapture = true,
            AllowLiveSpeech = true,
            ControlTemplate = Assert.IsType<ControlTemplate>(
                theme[ChatThemeKeys.ChatViewTemplate]),
        };

        Assert.Equal("ChatAttachButton", view.AttachButton?.AutomationId);
        Assert.Equal("ChatAudioButton", view.AudioButton?.AutomationId);
        Assert.Equal("ChatLiveSpeechButton", view.LiveSpeechButton?.AutomationId);
        Assert.Equal("ChatSendButton", view.SendButton?.AutomationId);
        Assert.Equal("ChatStopButton", view.StopButton?.AutomationId);
    }

    [Fact]
    public async Task ChatView_WithACodeTemplate_StillSends()
    {
        var conversation = TestHelpers.ChatFactory.Conversation();
        var view = new ChatView
        {
            ControlTemplate = new ControlTemplate(() => new Grid()),
            Conversation = conversation,
            Text = "hello",
        };

        await view.SendAsync();

        Assert.Single(conversation.Messages);
    }

    [Fact]
    public void HeaderTemplate_InheritsTheChatBindingContext()
    {
        var context = new object();
        var view = CreateTemplatedView(
            context,
            new DataTemplate(() => new Label()));

        var label = Assert.IsType<Label>(view.HeaderPart?.Content);
        Assert.Same(context, label.BindingContext);
    }

    [Fact]
    public void HeaderTemplate_PreservesAnExplicitBindingContext()
    {
        var inheritedContext = new object();
        var explicitContext = new object();
        var view = CreateTemplatedView(
            inheritedContext,
            new DataTemplate(() => new Label
            {
                BindingContext = explicitContext,
            }));

        var label = Assert.IsType<Label>(view.HeaderPart?.Content);
        Assert.Same(explicitContext, label.BindingContext);
    }

    private static PartChatView CreateTemplatedView(
        object bindingContext,
        DataTemplate headerTemplate)
    {
        var theme = new ChatControlsTheme();
        return new PartChatView
        {
            BindingContext = bindingContext,
            HeaderTemplate = headerTemplate,
            ControlTemplate = Assert.IsType<ControlTemplate>(
                theme[ChatThemeKeys.ChatViewTemplate]),
        };
    }

    private sealed class FactoryChatView : ChatView
    {
        public ChatMessagesView CreateMessageList() => CreateMessageListView();
    }

    private sealed class PartChatView : ChatView
    {
        public ContentView? HeaderPart => FindPart<ContentView>(HeaderPartName);

        public Button? AttachButton => FindPart<Button>(AttachButtonPartName);

        public Button? AudioButton => FindPart<Button>(AudioButtonPartName);

        public Button? LiveSpeechButton =>
            FindPart<Button>(LiveSpeechButtonPartName);

        public Button? SendButton => FindPart<Button>(SendButtonPartName);

        public Button? StopButton => FindPart<Button>(StopButtonPartName);
    }
}
