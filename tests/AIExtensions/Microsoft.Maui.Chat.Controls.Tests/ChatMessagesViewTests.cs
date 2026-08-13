using Microsoft.Maui.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.Chat.Controls.Tests;

/// <summary>
/// Covers <see cref="ChatMessagesView"/>: the flat projection, its grouping flags, in-place updates, the
/// template tiers, and the scrolling seams.
/// </summary>
public class ChatMessagesViewTests
{
    [Fact]
    public void NewView_HasNoRowsAndSensibleDefaults()
    {
        var view = new ChatMessagesView();

        Assert.Empty(view.Items);
        Assert.Empty(view.ContentTemplates);
        Assert.True(view.UseDefaultContentTemplates);
        Assert.True(view.AutoScrollToLatest);
        Assert.Equal(-1, view.LoadEarlierThreshold);
        Assert.NotNull(view.Appearance);
        Assert.NotNull(view.ItemTemplateSelector);
    }

    [Fact]
    public void AssigningAConversation_ProjectsOneRowPerContent()
    {
        var conversation = ChatFactory.Conversation(out var local, out var remote);
        var message = conversation.AddMessage(local, "hello");
        message.Contents.Add(ChatFactory.Image());
        conversation.AddMessage(remote, "hi back");

        var view = new ChatMessagesView { Conversation = conversation };

        Assert.Equal(3, view.Items.Count);
        Assert.Same(message.Contents[0], view.Items[0].Content);
        Assert.Same(message.Contents[1], view.Items[1].Content);
        Assert.Same(remote, view.Items[2].Participant);
    }

    [Fact]
    public void AddingAMessage_AppendsRows()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var view = new ChatMessagesView { Conversation = conversation };

        conversation.AddMessage(local, "hello");

        var row = Assert.Single(view.Items);
        Assert.True(row.IsOutgoing);
        Assert.Same(conversation, row.Conversation);
    }

    [Fact]
    public void AddingContent_AppendsARowWithoutReplacingTheExistingOne()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "hello");
        var view = new ChatMessagesView { Conversation = conversation };
        var firstRow = view.Items[0];
        using var recorder = new CollectionRecorder(view.Items);

        conversation.AddContent(message, ChatFactory.Image());

        Assert.Equal(2, view.Items.Count);
        Assert.Same(firstRow, view.Items[0]);
        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Add], recorder.Actions);
    }

    [Fact]
    public void RemovingAMessage_RemovesItsRows()
    {
        var conversation = ChatFactory.Conversation(out var local, out var remote);
        conversation.AddMessage(local, "one");
        var second = conversation.AddMessage(remote, "two");
        second.Contents.Add(ChatFactory.Image());
        var view = new ChatMessagesView { Conversation = conversation };

        conversation.RemoveMessage(second);

        var row = Assert.Single(view.Items);
        Assert.Same(local, row.Participant);
    }

    [Fact]
    public void RemovingContent_RemovesOnlyThatRow()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "one");
        var image = conversation.AddContent(message, ChatFactory.Image());
        var view = new ChatMessagesView { Conversation = conversation };
        var textRow = view.Items[0];

        message.Contents.Remove(image);

        Assert.Same(textRow, Assert.Single(view.Items));
    }

    [Fact]
    public void Reset_ClearsTheProjection()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        conversation.AddMessage(local, "one");
        conversation.AddMessage(local, "two");
        var view = new ChatMessagesView { Conversation = conversation };

        conversation.Reset();

        Assert.Empty(view.Items);
    }

    [Fact]
    public void ClearingTheConversation_ClearsTheProjection()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        conversation.AddMessage(local, "one");
        var view = new ChatMessagesView { Conversation = conversation };

        view.Conversation = null;

        Assert.Empty(view.Items);
    }

    [Fact]
    public void SwappingConversations_ProjectsTheNewOneAndUnsubscribesFromTheOld()
    {
        var first = ChatFactory.Conversation(out var firstLocal, out _);
        first.AddMessage(firstLocal, "one");
        var second = ChatFactory.Conversation(out var secondLocal, out _);
        second.AddMessage(secondLocal, "two");

        var view = new ChatMessagesView { Conversation = first };
        view.Conversation = second;

        Assert.Single(view.Items);
        Assert.Same(second.Messages[0].Contents[0], view.Items[0].Content);

        first.AddMessage(firstLocal, "ignored");
        Assert.Single(view.Items);
    }

    [Fact]
    public void InPlaceContentUpdate_DoesNotTouchTheItemsCollection()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "He");
        var view = new ChatMessagesView { Conversation = conversation };
        var row = view.Items[0];
        using var recorder = new CollectionRecorder(view.Items);

        ((TextMessageContent)message.Contents[0]).Append("llo");

        Assert.Empty(recorder.Events);
        Assert.Same(row, view.Items[0]);
        Assert.DoesNotContain(
            System.Collections.Specialized.NotifyCollectionChangedAction.Replace,
            recorder.Actions);
    }

    [Fact]
    public void InPlaceContentUpdate_NotifiesTheRow()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "He");
        var view = new ChatMessagesView { Conversation = conversation };
        using var recorder = new PropertyRecorder(view.Items[0]);

        ((TextMessageContent)message.Contents[0]).Append("llo");

        Assert.Equal(1, recorder.CountOf(nameof(ChatContentItem.Content)));
    }

    [Fact]
    public void MessageStatusChange_NotifiesTheRowWithoutReplacingIt()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "hello");
        var view = new ChatMessagesView { Conversation = conversation };
        var row = view.Items[0];
        using var items = new CollectionRecorder(view.Items);
        using var properties = new PropertyRecorder(row);

        message.Status = ConversationMessageStatus.Delivered;

        Assert.Empty(items.Events);
        Assert.Equal(1, properties.CountOf(nameof(ChatContentItem.Message)));
    }

    [Fact]
    public void Flags_ForASingleMessageWithSeveralContents()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "one");
        message.Contents.Add(new TextMessageContent("two"));
        message.Contents.Add(new TextMessageContent("three"));

        var view = new ChatMessagesView { Conversation = conversation };

        AssertFlags(view.Items[0], first: true, last: false, firstFrom: true, lastFrom: false);
        AssertFlags(view.Items[1], first: false, last: false, firstFrom: false, lastFrom: false);
        AssertFlags(view.Items[2], first: false, last: true, firstFrom: false, lastFrom: true);
    }

    [Fact]
    public void Flags_GroupConsecutiveMessagesFromTheSameParticipant()
    {
        var conversation = ChatFactory.Conversation(out var local, out var remote);
        conversation.AddMessage(remote, "one");
        conversation.AddMessage(remote, "two");
        conversation.AddMessage(local, "three");

        var view = new ChatMessagesView { Conversation = conversation };

        AssertFlags(view.Items[0], first: true, last: true, firstFrom: true, lastFrom: false);
        AssertFlags(view.Items[1], first: true, last: true, firstFrom: false, lastFrom: true);
        AssertFlags(view.Items[2], first: true, last: true, firstFrom: true, lastFrom: true);
    }

    [Fact]
    public void Flags_AreUpdatedInPlaceWhenAParticipantRunGrows()
    {
        var conversation = ChatFactory.Conversation(out _, out var remote);
        conversation.AddMessage(remote, "one");
        var view = new ChatMessagesView { Conversation = conversation };
        var firstRow = view.Items[0];

        Assert.True(firstRow.IsLastFromParticipant);

        conversation.AddMessage(remote, "two");

        Assert.Same(firstRow, view.Items[0]);
        Assert.False(firstRow.IsLastFromParticipant);
        Assert.True(view.Items[1].IsLastFromParticipant);
        Assert.False(view.Items[1].IsFirstFromParticipant);
    }

    [Fact]
    public void Flags_AreUpdatedWhenAMessageIsInsertedInTheMiddle()
    {
        var local = ChatFactory.Local();
        var remote = ChatFactory.Remote();
        var conversation = new InsertableConversation { LocalParticipant = local };
        conversation.AddMessage(remote, "one");
        conversation.AddMessage(remote, "two");

        var view = new ChatMessagesView { Conversation = conversation };
        var firstRow = view.Items[0];
        var lastRow = view.Items[1];

        Assert.False(firstRow.IsLastFromParticipant);

        conversation.Insert(1, new ConversationMessage(local, "interrupt"));

        Assert.Equal(3, view.Items.Count);
        Assert.Same(firstRow, view.Items[0]);
        Assert.Same(lastRow, view.Items[2]);

        AssertFlags(view.Items[0], first: true, last: true, firstFrom: true, lastFrom: true);
        AssertFlags(view.Items[1], first: true, last: true, firstFrom: true, lastFrom: true);
        AssertFlags(view.Items[2], first: true, last: true, firstFrom: true, lastFrom: true);
        Assert.True(view.Items[1].IsOutgoing);
    }

    [Fact]
    public void MovingAMessage_ReordersRowsWithoutRecreatingThem()
    {
        var local = ChatFactory.Local();
        var conversation = new InsertableConversation { LocalParticipant = local };
        var first = conversation.AddMessage(local, "one");
        conversation.AddMessage(ChatFactory.Remote(), "two");

        var view = new ChatMessagesView { Conversation = conversation };
        var firstRow = view.Items[0];

        conversation.Move(0, 1);

        Assert.Equal(2, view.Items.Count);
        Assert.Same(firstRow, view.Items[1]);
        Assert.Same(first.Contents[0], view.Items[1].Content);
    }

    [Fact]
    public void Flags_FollowTheLocalParticipant()
    {
        var conversation = ChatFactory.Conversation(out var local, out var remote);
        conversation.AddMessage(remote, "one");
        var view = new ChatMessagesView { Conversation = conversation };

        Assert.False(view.Items[0].IsOutgoing);

        conversation.LocalParticipant = remote;

        Assert.True(view.Items[0].IsOutgoing);
        Assert.False(ChatContentItem.IsOutgoingFor(conversation, local));
    }

    [Fact]
    public void Flags_IgnoreMessagesThatHaveNoContent()
    {
        var conversation = ChatFactory.Conversation(out _, out var remote);
        conversation.AddMessage(remote, "one");
        conversation.AddMessage(new ConversationMessage(ChatFactory.Local()));
        conversation.AddMessage(remote, "two");

        var view = new ChatMessagesView { Conversation = conversation };

        Assert.Equal(2, view.Items.Count);
        Assert.False(view.Items[0].IsLastFromParticipant);
        Assert.False(view.Items[1].IsFirstFromParticipant);
    }

    [Fact]
    public void Appearance_IsAppliedToEveryRow()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        conversation.AddMessage(local, "one");
        var appearance = new ChatAppearance();

        var view = new ChatMessagesView { Appearance = appearance, Conversation = conversation };

        Assert.Same(appearance, view.Items[0].Appearance);
    }

    [Fact]
    public void Appearance_Change_FlowsToExistingRows()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        conversation.AddMessage(local, "one");
        var view = new ChatMessagesView { Conversation = conversation };
        var replacement = new ChatAppearance();

        view.Appearance = replacement;

        Assert.Same(replacement, view.Items[0].Appearance);
    }

    [Fact]
    public void TemplateSelector_IncludesTheBuiltInFallbacks()
    {
        var view = new ChatMessagesView();

        var selector = Assert.IsType<ChatContentTemplateSelector>(view.ItemTemplateSelector);

        Assert.Empty(selector.Templates);
        Assert.Equal(3, selector.FallbackTemplates.Count);
    }

    [Fact]
    public void TemplateSelector_WithDefaultsDisabled_HasNoFallbacks()
    {
        var view = new ChatMessagesView { UseDefaultContentTemplates = false };

        var selector = Assert.IsType<ChatContentTemplateSelector>(view.ItemTemplateSelector);

        Assert.Empty(selector.FallbackTemplates);
    }

    [Fact]
    public void TemplateSelector_IsRebuiltWhenConsumerTemplatesChange()
    {
        var view = new ChatMessagesView();
        var before = view.ItemTemplateSelector;

        view.ContentTemplates.Add(new GenericChatContentTemplate { ViewType = typeof(ChatTextContentView) });

        var after = Assert.IsType<ChatContentTemplateSelector>(view.ItemTemplateSelector);
        Assert.NotSame(before, after);
        Assert.Single(after.Templates);
    }

    [Fact]
    public void TemplateSelector_IsRebuiltWhenTheTemplateCollectionIsReplaced()
    {
        var view = new ChatMessagesView
        {
            ContentTemplates = new System.Collections.ObjectModel.ObservableCollection<ChatContentTemplate>
            {
                new GenericChatContentTemplate { ViewType = typeof(ChatTextContentView) },
            },
        };

        var selector = Assert.IsType<ChatContentTemplateSelector>(view.ItemTemplateSelector);
        Assert.Single(selector.Templates);
    }

    [Fact]
    public void TemplateSelector_PicksAConsumerTemplateForARow()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        conversation.AddMessage(local, "hello");
        var consumer = new GenericChatContentTemplate
        {
            ContentType = typeof(TextMessageContent),
            ViewType = typeof(ChatFileContentView),
        };

        var view = new ChatMessagesView { Conversation = conversation };
        view.ContentTemplates.Add(consumer);

        var selected = view.ItemTemplateSelector!.SelectTemplate(view.Items[0], view);

        Assert.Same(consumer.GetTemplate(), selected);
    }

    [Fact]
    public void OnScrolled_NearTheStart_RaisesLoadEarlierOnce()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        for (var i = 0; i < 5; i++)
            conversation.AddMessage(local, $"m{i}");

        var view = new ChatMessagesView { Conversation = conversation, LoadEarlierThreshold = 1 };
        var raised = 0;
        var commandRuns = 0;
        view.LoadEarlierRequested += (_, _) => raised++;
        view.LoadEarlierCommand = new Command(() => commandRuns++);

        view.OnScrolled(0, 3);
        view.OnScrolled(1, 4);

        Assert.Equal(1, raised);
        Assert.Equal(1, commandRuns);
    }

    [Fact]
    public void OnScrolled_AfterNewRows_CanRaiseLoadEarlierAgain()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        conversation.AddMessage(local, "one");
        var view = new ChatMessagesView { Conversation = conversation, LoadEarlierThreshold = 0 };
        var raised = 0;
        view.LoadEarlierRequested += (_, _) => raised++;

        view.OnScrolled(0, 0);
        conversation.AddMessage(local, "two");
        view.OnScrolled(0, 1);

        Assert.Equal(2, raised);
    }

    [Fact]
    public void OnScrolled_WithTheSeamDisabled_RaisesNothing()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        conversation.AddMessage(local, "one");
        var view = new ChatMessagesView { Conversation = conversation };
        var raised = 0;
        view.LoadEarlierRequested += (_, _) => raised++;

        view.OnScrolled(0, 0);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void OnScrolled_BelowTheThreshold_RaisesNothing()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        for (var i = 0; i < 10; i++)
            conversation.AddMessage(local, $"m{i}");

        var view = new ChatMessagesView { Conversation = conversation, LoadEarlierThreshold = 1 };
        var raised = 0;
        view.LoadEarlierRequested += (_, _) => raised++;

        view.OnScrolled(5, 9);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void OnScrolled_WithNoRows_RaisesNothing()
    {
        var view = new ChatMessagesView { LoadEarlierThreshold = 0 };
        var raised = 0;
        view.LoadEarlierRequested += (_, _) => raised++;

        view.OnScrolled(-1, -1);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void StreamingWithoutARealizedList_StillUpdatesRowsImmediately()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, string.Empty);
        var view = new ChatMessagesView { Conversation = conversation };
        var content = (TextMessageContent)message.Contents[0];
        using var recorder = new PropertyRecorder(view.Items[0]);

        content.Append("a");
        content.Append("b");
        content.Append("c");

        Assert.Equal("abc", content.Text);
        Assert.Equal(3, recorder.CountOf(nameof(ChatContentItem.Content)));
    }

    private static void AssertFlags(
        ChatContentItem item,
        bool first,
        bool last,
        bool firstFrom,
        bool lastFrom)
    {
        Assert.Equal(first, item.IsFirstInMessage);
        Assert.Equal(last, item.IsLastInMessage);
        Assert.Equal(firstFrom, item.IsFirstFromParticipant);
        Assert.Equal(lastFrom, item.IsLastFromParticipant);
    }

    /// <summary>A conversation that also allows the mid-list edits a transport with history would make.</summary>
    private sealed class InsertableConversation : ObservableChatConversation
    {
        public void Insert(int index, ConversationMessage message) => MessageList.Insert(index, message);

        public void Move(int from, int to) => MessageList.Move(from, to);
    }
}
