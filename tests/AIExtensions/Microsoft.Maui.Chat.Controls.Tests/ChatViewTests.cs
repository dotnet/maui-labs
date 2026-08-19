using Microsoft.Maui.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.Chat.Controls.Tests;

/// <summary>
/// Covers <see cref="ChatView"/>: composer validation, the send path and its reentrancy guard, generic
/// error reporting, attachments, suggestions, and the empty and busy states.
/// </summary>
public class ChatViewTests
{
    [Fact]
    public void NewView_HasSensibleDefaults()
    {
        var view = new ChatView();

        Assert.Equal(string.Empty, view.Text);
        Assert.False(view.IsBusy);
        Assert.True(view.IsEmpty);
        Assert.True(view.ShowWelcome);
        Assert.False(view.ShowEmptyView);
        Assert.False(view.ShowSuggestions);
        Assert.False(view.CanSend);
        Assert.False(view.HasAttachments);
        Assert.False(view.AllowAttachments);
        Assert.Null(view.SendError);
        Assert.Null(view.AttachmentError);
        Assert.False(view.HasSendError);
        Assert.False(view.HasAttachmentError);
        Assert.Empty(view.Attachments);
        Assert.Empty(view.EffectiveSuggestions);
        Assert.True(view.UseDefaultContentTemplates);
        Assert.True(view.AutoScrollToLatest);
        Assert.NotNull(view.Appearance);
        Assert.Equal(10L * 1024 * 1024, view.MaxAttachmentBytes);
    }

    [Fact]
    public void AssigningAConversationWithMessages_LeavesTheEmptyState()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        conversation.AddMessage(local, "hello");

        var view = new ChatView { Conversation = conversation };

        Assert.False(view.IsEmpty);
        Assert.False(view.ShowWelcome);
    }

    [Fact]
    public void AddingTheFirstMessage_LeavesTheEmptyState()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var view = new ChatView { Conversation = conversation };

        conversation.AddMessage(local, "hello");

        Assert.False(view.IsEmpty);
        Assert.False(view.ShowWelcome);
    }

    [Fact]
    public void RemovingTheLastMessage_ReturnsToTheEmptyState()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "hello");
        var view = new ChatView { Conversation = conversation };

        conversation.RemoveMessage(message);

        Assert.True(view.IsEmpty);
        Assert.True(view.ShowWelcome);
    }

    [Fact]
    public void EmptyViewTemplate_ReplacesTheWelcomePanel()
    {
        var view = new ChatView
        {
            EmptyViewTemplate = new DataTemplate(() => new Label { Text = "Nothing here" }),
        };

        Assert.True(view.ShowEmptyView);
        Assert.False(view.ShowWelcome);
    }

    [Fact]
    public void BusyStatus_DrivesIsBusy()
    {
        var conversation = ChatFactory.Conversation();
        var view = new ChatView { Conversation = conversation };

        conversation.SetStatus(ChatConversationStatus.Busy);
        Assert.True(view.IsBusy);
        Assert.True(view.IsBusyIndicatorVisible);

        view.ShowBusyIndicator = false;
        Assert.True(view.IsBusy);
        Assert.False(view.IsBusyIndicatorVisible);

        conversation.SetStatus(ChatConversationStatus.Idle);
        Assert.False(view.IsBusy);
        Assert.False(view.IsBusyIndicatorVisible);
    }

    [Fact]
    public void TypingParticipants_UpdateTheTypingSummary()
    {
        var conversation = ChatFactory.Conversation();
        var priya = ChatFactory.Remote("priya", "Priya");
        var diego = ChatFactory.Remote("diego", "Diego");
        var view = new ChatView { Conversation = conversation };

        conversation.TypingParticipants.Add(priya);
        Assert.True(view.HasTypingParticipants);
        Assert.Equal("Priya is typing…", view.TypingText);

        conversation.TypingParticipants.Add(diego);
        Assert.Equal("Priya and Diego are typing…", view.TypingText);

        conversation.TypingParticipants.Clear();
        Assert.False(view.HasTypingParticipants);
        Assert.Equal(string.Empty, view.TypingText);
    }

    [Fact]
    public void SwappingConversations_StopsTrackingTheOldOne()
    {
        var first = ChatFactory.Conversation(out var firstLocal, out _);
        var second = ChatFactory.Conversation();
        var view = new ChatView { Conversation = first };

        view.Conversation = second;
        first.SetStatus(ChatConversationStatus.Busy);
        first.AddMessage(firstLocal, "ignored");

        Assert.False(view.IsBusy);
        Assert.True(view.IsEmpty);
    }

    [Fact]
    public void ClearingTheConversation_ReturnsToTheEmptyState()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        conversation.AddMessage(local, "hello");
        var view = new ChatView { Conversation = conversation };

        view.Conversation = null;

        Assert.True(view.IsEmpty);
        Assert.False(view.CanSend);
    }

    [Fact]
    public void CanSend_RequiresAConversationAndSomethingToSend()
    {
        var view = new ChatView { Text = "hello" };
        Assert.False(view.CanSend);

        view.Conversation = ChatFactory.Conversation();
        Assert.True(view.CanSend);

        view.Text = "   ";
        Assert.False(view.CanSend);

        view.AddAttachment(ChatFactory.Attachment());
        Assert.True(view.CanSend);
    }

    [Fact]
    public void CanSend_IsFalseWhileTheConversationIsBusy()
    {
        var conversation = ChatFactory.Conversation();
        var view = new ChatView { Conversation = conversation, Text = "hello" };

        conversation.SetStatus(ChatConversationStatus.Busy);

        Assert.False(view.CanSend);
    }

    [Fact]
    public void CreateDraft_CarriesTrimmedTextAndAttachments()
    {
        var view = new ChatView { Text = "  hello  " };
        view.AddAttachment(ChatFactory.Attachment());

        var draft = view.CreateDraft();

        Assert.Equal("hello", draft.Text);
        Assert.Single(draft.Attachments);
    }

    [Fact]
    public async Task SendAsync_TextOnly_SendsAndClearsTheComposer()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var view = new ChatView { Conversation = conversation, Text = "hello" };

        await view.SendAsync();

        var message = Assert.Single(conversation.Messages);
        Assert.Same(local, message.Participant);
        Assert.Equal("hello", Assert.IsType<TextMessageContent>(Assert.Single(message.Contents)).Text);
        Assert.Equal(string.Empty, view.Text);
    }

    [Fact]
    public async Task SendAsync_AttachmentOnly_Sends()
    {
        var conversation = ChatFactory.Conversation();
        var view = new ChatView { Conversation = conversation };
        view.AddAttachment(ChatFactory.Attachment("cat.png"));

        await view.SendAsync();

        var message = Assert.Single(conversation.Messages);
        var media = Assert.IsType<MediaMessageContent>(Assert.Single(message.Contents));
        Assert.Equal("cat.png", media.FileName);
        Assert.Empty(view.Attachments);
    }

    [Fact]
    public async Task SendAsync_Mixed_SendsTextThenAttachments()
    {
        var conversation = ChatFactory.Conversation();
        var view = new ChatView { Conversation = conversation, Text = "look" };
        view.AddAttachment(ChatFactory.Attachment("cat.png"));

        await view.SendAsync();

        var message = Assert.Single(conversation.Messages);
        Assert.Collection(
            message.Contents,
            content => Assert.Equal("look", Assert.IsType<TextMessageContent>(content).Text),
            content => Assert.IsType<MediaMessageContent>(content));
        Assert.Equal(string.Empty, view.Text);
        Assert.Empty(view.Attachments);
    }

    [Fact]
    public async Task SendAsync_WithNothingToSend_DoesNothing()
    {
        var conversation = ChatFactory.Conversation();
        var view = new ChatView { Conversation = conversation, Text = "   " };

        await view.SendAsync();

        Assert.Empty(conversation.Messages);
        Assert.Null(view.SendError);
    }

    [Fact]
    public async Task SendAsync_WithoutAConversation_DoesNothing()
    {
        var view = new ChatView { Text = "hello" };

        await view.SendAsync();

        Assert.Equal("hello", view.Text);
        Assert.Null(view.SendError);
    }

    [Fact]
    public async Task SendAsync_WhileBusy_DoesNothing()
    {
        var conversation = ChatFactory.Conversation();
        conversation.SetStatus(ChatConversationStatus.Busy);
        var view = new ChatView { Conversation = conversation, Text = "hello" };

        await view.SendAsync();

        Assert.Empty(conversation.Messages);
        Assert.Equal("hello", view.Text);
    }

    [Fact]
    public async Task SendAsync_WhenTheDraftIsRejected_KeepsTheComposerIntact()
    {
        var conversation = ChatFactory.Conversation();
        conversation.SendHandler = (_, _, _) => Task.FromResult(false);
        var view = new ChatView { Conversation = conversation, Text = "hello" };
        view.AddAttachment(ChatFactory.Attachment());

        await view.SendAsync();

        Assert.Equal("hello", view.Text);
        Assert.Single(view.Attachments);
        Assert.Null(view.SendError);
    }

    [Fact]
    public async Task SendAsync_WhenSendingThrows_ReportsAGenericErrorAndKeepsTheDraft()
    {
        var conversation = ChatFactory.Conversation();
        conversation.SendHandler = (_, _, _) => throw new InvalidOperationException("boom: secret detail");
        var view = new ChatView { Conversation = conversation, Text = "hello" };

        await view.SendAsync();

        Assert.Equal(ChatView.DefaultSendErrorMessage, view.SendError);
        Assert.True(view.HasSendError);
        Assert.DoesNotContain("secret", view.SendError!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("hello", view.Text);
    }

    [Fact]
    public async Task SendAsync_WhenSendingIsCancelled_ReportsNoError()
    {
        var conversation = ChatFactory.Conversation();
        conversation.SendHandler = (_, _, _) => Task.FromCanceled<bool>(new CancellationToken(canceled: true));
        var view = new ChatView { Conversation = conversation, Text = "hello" };

        await view.SendAsync();

        Assert.Null(view.SendError);
        Assert.Equal("hello", view.Text);
    }

    [Fact]
    public async Task SendAsync_AfterAFailure_ClearsThePreviousError()
    {
        var conversation = ChatFactory.Conversation();
        var shouldThrow = true;
        conversation.SendHandler = (_, _, _) =>
            shouldThrow ? throw new InvalidOperationException("boom") : Task.FromResult(true);

        var view = new ChatView { Conversation = conversation, Text = "hello" };
        await view.SendAsync();
        Assert.NotNull(view.SendError);

        shouldThrow = false;
        view.Text = "again";
        await view.SendAsync();

        Assert.Null(view.SendError);
        Assert.False(view.HasSendError);
    }

    [Fact]
    public async Task SendAsync_WhileAlreadySending_IsIgnored()
    {
        var conversation = ChatFactory.Conversation();
        var gate = new TaskCompletionSource<bool>();
        var calls = 0;
        conversation.SendHandler = (_, _, _) =>
        {
            calls++;
            return gate.Task;
        };

        var view = new ChatView { Conversation = conversation, Text = "hello" };

        var first = view.SendAsync();
        var second = view.SendAsync();

        Assert.Equal(1, calls);
        Assert.False(view.CanSend);

        gate.SetResult(true);
        await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.Equal(string.Empty, view.Text);
        Assert.False(view.CanSend);
    }

    [Fact]
    public async Task SendAsync_DraftChangedWhileSending_PreservesTheNewDraft()
    {
        var conversation = ChatFactory.Conversation();
        var gate = new TaskCompletionSource<bool>();
        conversation.SendHandler = (_, _, _) => gate.Task;
        var firstAttachment = ChatFactory.Attachment("first.png");
        var nextAttachment = ChatFactory.Attachment("next.png");
        var view = new ChatView
        {
            Conversation = conversation,
            Text = "first",
        };
        view.AddAttachment(firstAttachment);

        var send = view.SendAsync();
        view.Text = "next";
        view.AddAttachment(nextAttachment);

        gate.SetResult(true);
        await send;

        Assert.Equal("next", view.Text);
        Assert.Same(nextAttachment, Assert.Single(view.Attachments));
    }

    [Fact]
    public async Task SendAsync_AfterAPreviousSendFinished_SendsAgain()
    {
        var conversation = ChatFactory.Conversation();
        var view = new ChatView { Conversation = conversation, Text = "one" };

        await view.SendAsync();
        view.Text = "two";
        await view.SendAsync();

        Assert.Equal(2, conversation.Messages.Count);
    }

    [Fact]
    public void AddAttachment_TracksTheComposerState()
    {
        var view = new ChatView();
        var attachment = ChatFactory.Attachment();

        view.AddAttachment(attachment);

        Assert.True(view.HasAttachments);
        Assert.Same(attachment, Assert.Single(view.Attachments));
    }

    [Fact]
    public void RemoveAttachment_UpdatesTheComposerState()
    {
        var view = new ChatView();
        var attachment = ChatFactory.Attachment();
        view.AddAttachment(attachment);

        Assert.True(view.RemoveAttachment(attachment));
        Assert.False(view.HasAttachments);
        Assert.False(view.RemoveAttachment(attachment));
    }

    [Fact]
    public void ClearAttachments_EmptiesTheComposer()
    {
        var view = new ChatView();
        view.AddAttachment(ChatFactory.Attachment("a.png"));
        view.AddAttachment(ChatFactory.Attachment("b.png"));

        view.ClearAttachments();

        Assert.Empty(view.Attachments);
        Assert.False(view.HasAttachments);
    }

    [Fact]
    public void AttachmentMethods_WithNull_Throw()
    {
        var view = new ChatView();

        Assert.Throws<ArgumentNullException>(() => view.AddAttachment(null!));
        Assert.Throws<ArgumentNullException>(() => view.RemoveAttachment(null!));
    }

    [Fact]
    public async Task PickAttachmentsAsync_StagesWhatThePickerReturns()
    {
        var picker = new FakePicker([ChatFactory.Attachment("a.png"), ChatFactory.Attachment("b.png")]);
        var view = new ChatView { AttachmentPicker = picker, AllowAttachments = true };

        await view.PickAttachmentsAsync();

        Assert.Equal(2, view.Attachments.Count);
        Assert.Null(view.AttachmentError);
        Assert.Equal(view.MaxAttachmentBytes, picker.SeenMaxBytes);
    }

    [Fact]
    public async Task PickAttachmentsAsync_PassesTheConfiguredFileTypes()
    {
        var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>());
        var picker = new FakePicker([]);
        var view = new ChatView { AttachmentPicker = picker, AttachmentFileTypes = fileTypes };

        await view.PickAttachmentsAsync();

        Assert.Same(fileTypes, picker.SeenFileTypes);
    }

    [Fact]
    public async Task PickAttachmentsAsync_WhenThePickerFails_ReportsAGenericError()
    {
        var picker = new FakePicker(new InvalidOperationException("path /Users/secret/file.png is too large"));
        var view = new ChatView { AttachmentPicker = picker };

        await view.PickAttachmentsAsync();

        Assert.Equal(ChatView.DefaultAttachmentErrorMessage, view.AttachmentError);
        Assert.True(view.HasAttachmentError);
        Assert.DoesNotContain("secret", view.AttachmentError!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(view.Attachments);
    }

    [Fact]
    public async Task PickAttachmentsAsync_WhenCancelled_ReportsNoError()
    {
        var picker = new FakePicker(new OperationCanceledException());
        var view = new ChatView { AttachmentPicker = picker };

        await view.PickAttachmentsAsync();

        Assert.Null(view.AttachmentError);
    }

    [Fact]
    public async Task PickAttachmentsAsync_ClearsThePreviousError()
    {
        var view = new ChatView { AttachmentPicker = new FakePicker(new InvalidOperationException()) };
        await view.PickAttachmentsAsync();
        Assert.NotNull(view.AttachmentError);

        view.AttachmentPicker = new FakePicker([ChatFactory.Attachment()]);
        await view.PickAttachmentsAsync();

        Assert.Null(view.AttachmentError);
        Assert.False(view.HasAttachmentError);
    }

    [Fact]
    public async Task PickAttachmentsAsync_IgnoresNullResults()
    {
        var view = new ChatView { AttachmentPicker = new FakePicker((IReadOnlyList<ChatAttachment>?)null) };

        await view.PickAttachmentsAsync();

        Assert.Empty(view.Attachments);
        Assert.Null(view.AttachmentError);
    }

    [Fact]
    public void Suggestions_AreProjectedInDeclarationOrder()
    {
        var view = new ChatView();
        view.Suggestions.Add(new ChatSuggestion("Rich"));
        view.SuggestionPrompts.Add("Plain");

        Assert.Collection(
            view.EffectiveSuggestions,
            suggestion => Assert.Equal("Rich", suggestion.Label),
            suggestion => Assert.Equal("Plain", suggestion.Label));
    }

    [Fact]
    public void Suggestions_IgnoreBlankPrompts()
    {
        var view = new ChatView();
        view.SuggestionPrompts.Add("   ");
        view.SuggestionPrompts.Add("Real");

        Assert.Equal("Real", Assert.Single(view.EffectiveSuggestions).Label);
    }

    [Fact]
    public void Suggestions_AreOnlyShownWhileTheConversationIsEmpty()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var view = new ChatView { Conversation = conversation };
        view.SuggestionPrompts.Add("Say hello");

        Assert.True(view.ShowSuggestions);

        conversation.AddMessage(local, "hello");
        Assert.False(view.ShowSuggestions);
    }

    [Fact]
    public void Suggestions_ReplacingTheCollectionRebuildsTheProjection()
    {
        var view = new ChatView
        {
            Suggestions = new System.Collections.ObjectModel.ObservableCollection<ChatSuggestion>
            {
                new("First"),
            },
        };

        Assert.Equal("First", Assert.Single(view.EffectiveSuggestions).Label);

        view.Suggestions.Add(new ChatSuggestion("Second"));
        Assert.Equal(2, view.EffectiveSuggestions.Count);
    }

    [Fact]
    public void Suggestions_RemovingOneUpdatesTheProjection()
    {
        var view = new ChatView();
        var suggestion = new ChatSuggestion("Only");
        view.Suggestions.Add(suggestion);

        view.Suggestions.Remove(suggestion);

        Assert.Empty(view.EffectiveSuggestions);
        Assert.False(view.ShowSuggestions);
    }

    [Fact]
    public void ContentTemplates_AreExposedForTheMessageList()
    {
        var view = new ChatView();
        var template = new GenericChatContentTemplate { ViewType = typeof(ChatTextContentView) };

        view.ContentTemplates.Add(template);

        Assert.Same(template, Assert.Single(view.ContentTemplates));
    }

    [Fact]
    public void ComposerStyles_CanBeSetPerControl()
    {
        var inputAreaStyle = new Style(typeof(Border));
        var inputEntryStyle = new Style(typeof(Entry));
        var attachButtonStyle = new Style(typeof(Button));
        var sendButtonStyle = new Style(typeof(Button));
        var view = new ChatView
        {
            InputAreaStyle = inputAreaStyle,
            InputEntryStyle = inputEntryStyle,
            AttachButtonStyle = attachButtonStyle,
            SendButtonStyle = sendButtonStyle,
        };

        Assert.Same(inputAreaStyle, view.InputAreaStyle);
        Assert.Same(inputEntryStyle, view.InputEntryStyle);
        Assert.Same(attachButtonStyle, view.AttachButtonStyle);
        Assert.Same(sendButtonStyle, view.SendButtonStyle);
    }

    [Fact]
    public void PartNames_AreStableContractValues()
    {
        Assert.Equal("PART_Header", ChatView.HeaderPartName);
        Assert.Equal("PART_MessageList", ChatView.MessageListPartName);
        Assert.Equal("PART_WelcomePanel", ChatView.WelcomePanelPartName);
        Assert.Equal("PART_WelcomeIcon", ChatView.WelcomeIconPartName);
        Assert.Equal("PART_WelcomeMessage", ChatView.WelcomeMessagePartName);
        Assert.Equal("PART_EmptyView", ChatView.EmptyViewPartName);
        Assert.Equal("PART_BusyIndicator", ChatView.BusyIndicatorPartName);
        Assert.Equal("PART_Suggestions", ChatView.SuggestionsPartName);
        Assert.Equal("PART_TypingIndicator", ChatView.TypingIndicatorPartName);
        Assert.Equal("PART_Footer", ChatView.FooterPartName);
        Assert.Equal("PART_InputArea", ChatView.InputAreaPartName);
        Assert.Equal("PART_Attachments", ChatView.AttachmentsPartName);
        Assert.Equal("PART_AttachButton", ChatView.AttachButtonPartName);
        Assert.Equal("PART_InputEntry", ChatView.InputEntryPartName);
        Assert.Equal("PART_SendButton", ChatView.SendButtonPartName);
        Assert.Equal("PART_Messages", ChatMessagesView.MessagesPartName);
    }

    [Fact]
    public void DefaultPicker_IsASharedInstance()
    {
        Assert.Same(FileChatAttachmentPicker.Default, FileChatAttachmentPicker.Default);
    }

    [Fact]
    public async Task DefaultPicker_WithANonPositiveLimit_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => FileChatAttachmentPicker.Default.PickAsync(null, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => FileChatAttachmentPicker.Default.PickAsync(null, -1));
    }

    private sealed class FakePicker : IChatAttachmentPicker
    {
        private readonly IReadOnlyList<ChatAttachment>? _result;
        private readonly Exception? _failure;

        public FakePicker(IReadOnlyList<ChatAttachment>? result) => _result = result;

        public FakePicker(Exception failure) => _failure = failure;

        public FilePickerFileType? SeenFileTypes { get; private set; }

        public long SeenMaxBytes { get; private set; }

        public Task<IReadOnlyList<ChatAttachment>> PickAsync(
            FilePickerFileType? fileTypes,
            long maxBytesPerFile,
            CancellationToken cancellationToken = default)
        {
            SeenFileTypes = fileTypes;
            SeenMaxBytes = maxBytesPerFile;

            return _failure is not null
                ? Task.FromException<IReadOnlyList<ChatAttachment>>(_failure)
                : Task.FromResult(_result!);
        }
    }
}
