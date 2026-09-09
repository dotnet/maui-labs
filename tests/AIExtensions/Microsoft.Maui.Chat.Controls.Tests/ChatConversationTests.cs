using Microsoft.Maui.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.Chat.Controls.Tests;

/// <summary>
/// Covers the <see cref="ChatConversation"/> contract: ordered synchronous change publication,
/// subscription lifetime, and the send gate.
/// </summary>
public class ChatConversationTests
{
    [Fact]
    public void NewConversation_IsIdleAndEmpty()
    {
        var conversation = new ObservableChatConversation();

        Assert.Equal(ChatConversationStatus.Idle, conversation.Status);
        Assert.Empty(conversation.Messages);
        Assert.Empty(conversation.Participants);
        Assert.Empty(conversation.TypingParticipants);
        Assert.Null(conversation.LocalParticipant);
    }

    [Fact]
    public void Constructor_WithLocalParticipant_RegistersIt()
    {
        var local = ChatFactory.Local();
        var conversation = new ObservableChatConversation(local);

        Assert.Same(local, conversation.LocalParticipant);
        Assert.Contains(local, conversation.Participants);
    }

    [Fact]
    public void Constructor_WithNullLocalParticipant_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new ObservableChatConversation(null!));

    [Fact]
    public void Subscribe_WithNullCallback_Throws()
    {
        var conversation = ChatFactory.Conversation();

        Assert.Throws<ArgumentNullException>(() => conversation.Subscribe(null!));
    }

    [Fact]
    public void AddMessage_PublishesMessageAddedWithIndex()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        using var recorder = new ChangeRecorder(conversation);

        var first = conversation.AddMessage(local, "one");
        var second = conversation.AddMessage(local, "two");

        Assert.Equal(
            [ChatConversationChangeKind.MessageAdded, ChatConversationChangeKind.MessageAdded],
            recorder.Kinds);
        Assert.Same(first, recorder.Changes[0].Message);
        Assert.Equal(0, recorder.Changes[0].Index);
        Assert.Same(second, recorder.Changes[1].Message);
        Assert.Equal(1, recorder.Changes[1].Index);
    }

    [Fact]
    public void RemoveMessage_PublishesMessageRemovedWithIndex()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        conversation.AddMessage(local, "one");
        var second = conversation.AddMessage(local, "two");
        using var recorder = new ChangeRecorder(conversation);

        var removed = conversation.RemoveMessage(second);

        Assert.True(removed);
        var change = Assert.Single(recorder.Changes);
        Assert.Equal(ChatConversationChangeKind.MessageRemoved, change.Kind);
        Assert.Same(second, change.Message);
        Assert.Equal(1, change.Index);
    }

    [Fact]
    public void RemoveMessage_NotPresent_ReturnsFalseAndPublishesNothing()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        using var recorder = new ChangeRecorder(conversation);

        Assert.False(conversation.RemoveMessage(new ConversationMessage(local)));
        Assert.Empty(recorder.Changes);
    }

    [Fact]
    public void AddContent_PublishesContentAddedWithIndex()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "one");
        using var recorder = new ChangeRecorder(conversation);

        var content = conversation.AddContent(message, ChatFactory.Image());

        var change = Assert.Single(recorder.Changes);
        Assert.Equal(ChatConversationChangeKind.ContentAdded, change.Kind);
        Assert.Same(message, change.Message);
        Assert.Same(content, change.Content);
        Assert.Equal(1, change.Index);
    }

    [Fact]
    public void RemovingContent_PublishesContentRemoved()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "one");
        var content = message.Contents[0];
        using var recorder = new ChangeRecorder(conversation);

        message.Contents.Remove(content);

        var change = Assert.Single(recorder.Changes);
        Assert.Equal(ChatConversationChangeKind.ContentRemoved, change.Kind);
        Assert.Same(content, change.Content);
        Assert.Equal(0, change.Index);
    }

    [Fact]
    public void ClearingContent_PublishesMessageChanged()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "one");
        using var recorder = new ChangeRecorder(conversation);

        message.Contents.Clear();

        Assert.Equal([ChatConversationChangeKind.MessageChanged], recorder.Kinds);
    }

    [Fact]
    public void MutatingContentInPlace_PublishesContentChanged()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "He");
        var content = (TextMessageContent)message.Contents[0];
        using var recorder = new ChangeRecorder(conversation);

        content.Append("llo");

        var change = Assert.Single(recorder.Changes);
        Assert.Equal(ChatConversationChangeKind.ContentChanged, change.Kind);
        Assert.Same(content, change.Content);
        Assert.Same(message, change.Message);
    }

    [Fact]
    public void MutatingContentAddedLater_IsAlsoTracked()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "one");
        var streamed = conversation.AddContent(message, new TextMessageContent());
        using var recorder = new ChangeRecorder(conversation);

        streamed.Append("token");

        Assert.Equal([ChatConversationChangeKind.ContentChanged], recorder.Kinds);
    }

    [Fact]
    public void MutatingContentOfRemovedMessage_PublishesNothing()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "one");
        var content = (TextMessageContent)message.Contents[0];
        conversation.RemoveMessage(message);
        using var recorder = new ChangeRecorder(conversation);

        content.Append("more");

        Assert.Empty(recorder.Changes);
    }

    [Fact]
    public void MessageStatusChange_PublishesMessageChanged()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "one");
        using var recorder = new ChangeRecorder(conversation);

        message.Status = ConversationMessageStatus.Delivered;

        var change = Assert.Single(recorder.Changes);
        Assert.Equal(ChatConversationChangeKind.MessageChanged, change.Kind);
        Assert.Same(message, change.Message);
    }

    [Fact]
    public void SetStatus_PublishesStatusChangedCarryingTheNewStatus()
    {
        var conversation = ChatFactory.Conversation();
        using var recorder = new ChangeRecorder(conversation);

        conversation.SetStatus(ChatConversationStatus.Busy);

        var change = Assert.Single(recorder.Changes);
        Assert.Equal(ChatConversationChangeKind.StatusChanged, change.Kind);
        Assert.Equal(ChatConversationStatus.Busy, change.Status);
        Assert.Equal(ChatConversationStatus.Busy, conversation.Status);
    }

    [Fact]
    public void SetStatus_ToSameValue_PublishesNothing()
    {
        var conversation = ChatFactory.Conversation();
        conversation.SetStatus(ChatConversationStatus.Busy);
        using var recorder = new ChangeRecorder(conversation);

        conversation.SetStatus(ChatConversationStatus.Busy);

        Assert.Empty(recorder.Changes);
    }

    [Fact]
    public void Reset_ClearsMessagesAndPublishesReset()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        conversation.AddMessage(local, "one");
        conversation.AddMessage(local, "two");
        conversation.TypingParticipants.Add(local);
        conversation.SetStatus(ChatConversationStatus.Error);
        using var recorder = new ChangeRecorder(conversation);

        conversation.Reset();

        Assert.Empty(conversation.Messages);
        Assert.Empty(conversation.TypingParticipants);
        Assert.Equal(ChatConversationStatus.Idle, conversation.Status);
        Assert.Contains(ChatConversationChangeKind.Reset, recorder.Kinds);
        Assert.Contains(ChatConversationChangeKind.StatusChanged, recorder.Kinds);
    }

    [Fact]
    public void Reset_KeepsParticipants()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        conversation.AddMessage(local, "one");

        conversation.Reset();

        Assert.Equal(2, conversation.Participants.Count);
        Assert.Same(local, conversation.LocalParticipant);
    }

    [Fact]
    public void AfterReset_OldMessagesNoLongerPublish()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "one");
        conversation.Reset();
        using var recorder = new ChangeRecorder(conversation);

        message.Status = ConversationMessageStatus.Read;
        ((TextMessageContent)message.Contents[0]).Append("!");

        Assert.Empty(recorder.Changes);
    }

    [Fact]
    public void Changes_AreDeliveredSynchronouslyInOrder()
    {
        var conversation = ChatFactory.Conversation(out var local, out var remote);
        var order = new List<string>();
        using var first = conversation.Subscribe(_ => order.Add("first"));
        using var second = conversation.Subscribe(_ => order.Add("second"));

        conversation.AddMessage(local, "one");
        order.Add("after-add");

        Assert.Equal(["first", "second", "after-add"], order);
    }

    [Fact]
    public void Dispose_StopsDelivery()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var count = 0;
        var subscription = conversation.Subscribe(_ => count++);

        conversation.AddMessage(local, "one");
        subscription.Dispose();
        conversation.AddMessage(local, "two");

        Assert.Equal(1, count);
    }

    [Fact]
    public void Dispose_Twice_IsSafe()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var subscription = conversation.Subscribe(_ => { });

        subscription.Dispose();
        subscription.Dispose();

        conversation.AddMessage(local, "one");
    }

    [Fact]
    public void Dispose_OfOneSubscriber_KeepsTheOthers()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var kept = 0;
        var dropped = 0;
        var first = conversation.Subscribe(_ => dropped++);
        using var second = conversation.Subscribe(_ => kept++);

        first.Dispose();
        conversation.AddMessage(local, "one");

        Assert.Equal(0, dropped);
        Assert.Equal(1, kept);
    }

    [Fact]
    public void DisposingDuringDelivery_TakesEffectImmediately()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var secondCalls = 0;
        IDisposable? second = null;

        using var first = conversation.Subscribe(_ => second?.Dispose());
        second = conversation.Subscribe(_ => secondCalls++);

        conversation.AddMessage(local, "one");

        Assert.Equal(0, secondCalls);
    }

    [Fact]
    public void SubscribingDuringDelivery_DoesNotBreakTheCurrentDispatch()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var lateCalls = 0;
        IDisposable? late = null;

        using var first = conversation.Subscribe(_ => late ??= conversation.Subscribe(_ => lateCalls++));

        conversation.AddMessage(local, "one");
        Assert.Equal(0, lateCalls);

        conversation.AddMessage(local, "two");
        Assert.Equal(1, lateCalls);

        late?.Dispose();
    }

    [Fact]
    public void CanSend_RejectsNullAndEmptyDrafts()
    {
        var conversation = ChatFactory.Conversation();

        Assert.False(conversation.CanSend(null));
        Assert.False(conversation.CanSend(new ChatDraft("   ")));
        Assert.True(conversation.CanSend(new ChatDraft("hello")));
    }

    [Fact]
    public void CanSend_WhileBusy_IsFalse()
    {
        var conversation = ChatFactory.Conversation();
        conversation.SetStatus(ChatConversationStatus.Busy);

        Assert.False(conversation.CanSend(new ChatDraft("hello")));
    }

    [Fact]
    public async Task SendAsync_WithNullDraft_Throws()
    {
        var conversation = ChatFactory.Conversation();

        await Assert.ThrowsAsync<ArgumentNullException>(() => conversation.SendAsync(null!));
    }

    [Fact]
    public async Task SendAsync_WithRejectedDraft_DoesNotReachSendCore()
    {
        var conversation = ChatFactory.Conversation();
        var handlerCalls = 0;
        conversation.SendHandler = (_, _, _) =>
        {
            handlerCalls++;
            return Task.FromResult(true);
        };

        var accepted = await conversation.SendAsync(new ChatDraft("   "));

        Assert.False(accepted);
        Assert.Equal(0, handlerCalls);
    }

    [Fact]
    public async Task SendAsync_DefaultBehaviour_AppendsLocalMessage()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);

        var accepted = await conversation.SendAsync(new ChatDraft("hello", [ChatFactory.Attachment()]));

        Assert.True(accepted);
        var message = Assert.Single(conversation.Messages);
        Assert.Same(local, message.Participant);
        Assert.Equal(ConversationMessageStatus.Sent, message.Status);
        Assert.Equal(2, message.Contents.Count);
    }

    [Fact]
    public async Task SendAsync_WithoutLocalParticipant_IsRejected()
    {
        var conversation = new ObservableChatConversation();

        Assert.False(await conversation.SendAsync(new ChatDraft("hello")));
        Assert.Empty(conversation.Messages);
    }

    [Fact]
    public async Task SendAsync_WithHandler_UsesIt()
    {
        var conversation = ChatFactory.Conversation();
        ChatDraft? seen = null;
        conversation.SendHandler = (owner, draft, _) =>
        {
            seen = draft;
            Assert.Same(conversation, owner);
            return Task.FromResult(false);
        };

        var accepted = await conversation.SendAsync(new ChatDraft("hello"));

        Assert.False(accepted);
        Assert.Equal("hello", seen?.Text);
        Assert.Empty(conversation.Messages);
    }

    [Fact]
    public async Task SendAsync_PassesTheCancellationToken()
    {
        var conversation = ChatFactory.Conversation();
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        conversation.SendHandler = (_, _, token) =>
        {
            seen = token;
            return Task.FromResult(true);
        };

        await conversation.SendAsync(new ChatDraft("hello"), cts.Token);

        Assert.Equal(cts.Token, seen);
    }

    [Fact]
    public void NotifyContentChanged_PublishesContentChanged()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "one");
        using var recorder = new ChangeRecorder(conversation);

        conversation.NotifyContentChanged(message, message.Contents[0]);

        Assert.Equal([ChatConversationChangeKind.ContentChanged], recorder.Kinds);
    }

    [Fact]
    public void NotifyContentChanged_WithNulls_Throws()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "one");

        Assert.Throws<ArgumentNullException>(() => conversation.NotifyContentChanged(null!, message.Contents[0]));
        Assert.Throws<ArgumentNullException>(() => conversation.NotifyContentChanged(message, null!));
    }

    [Fact]
    public void AddMessage_WithNulls_Throws()
    {
        var conversation = ChatFactory.Conversation();

        Assert.Throws<ArgumentNullException>(() => conversation.AddMessage(null!));
        Assert.Throws<ArgumentNullException>(() => conversation.AddMessage(null!, "hi"));
        Assert.Throws<ArgumentNullException>(() => conversation.RemoveMessage(null!));
    }

    [Fact]
    public void AddContent_WithNulls_Throws()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var message = conversation.AddMessage(local, "one");

        Assert.Throws<ArgumentNullException>(() => conversation.AddContent(null!, new TextMessageContent()));
        Assert.Throws<ArgumentNullException>(() => conversation.AddContent<MessageContent>(message, null!));
    }

    [Fact]
    public void Messages_AreExposedAsAReadOnlyObservableCollection()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        using var recorder = new CollectionRecorder(conversation.Messages);

        conversation.AddMessage(local, "one");

        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Add], recorder.Actions);
    }

    [Fact]
    public void TypingParticipants_AreMutable()
    {
        var conversation = ChatFactory.Conversation(out _, out var remote);

        conversation.TypingParticipants.Add(remote);

        Assert.Same(remote, Assert.Single(conversation.TypingParticipants));
    }

    [Fact]
    public void CustomConversation_PublishesFromItsOwnMutations()
    {
        var conversation = new TestConversation();
        var local = ChatFactory.Local();
        conversation.LocalParticipant = local;
        using var recorder = new ChangeRecorder(conversation);

        conversation.Append(new ConversationMessage(local, "hi"));

        Assert.Equal([ChatConversationChangeKind.MessageAdded], recorder.Kinds);
    }

    [Fact]
    public async Task CustomConversation_SendCoreIsOnlyCalledForAcceptedDrafts()
    {
        var conversation = new TestConversation();

        Assert.False(await conversation.SendAsync(new ChatDraft(null)));
        Assert.Equal(0, conversation.SendCalls);

        Assert.True(await conversation.SendAsync(new ChatDraft("hi")));
        Assert.Equal(1, conversation.SendCalls);
    }

    private sealed class TestConversation : ChatConversation
    {
        public int SendCalls { get; private set; }

        public void Append(ConversationMessage message) => MessageList.Add(message);

        public void Publish(ChatConversationChange change) => RaiseChange(change);

        protected override Task<bool> SendCoreAsync(ChatDraft draft, CancellationToken cancellationToken)
        {
            SendCalls++;
            return Task.FromResult(true);
        }
    }

    [Fact]
    public void RaiseChange_IsAvailableToSubclasses()
    {
        var conversation = new TestConversation();
        using var recorder = new ChangeRecorder(conversation);

        conversation.Publish(ChatConversationChange.Reset());

        Assert.Equal([ChatConversationChangeKind.Reset], recorder.Kinds);
    }

    [Fact]
    public void ChangeFactories_CarryTheRightPayload()
    {
        var participant = ChatFactory.Remote();
        var message = new ConversationMessage(participant, "hi");
        var content = message.Contents[0];

        var added = ChatConversationChange.MessageAdded(message, 3);
        Assert.Equal(ChatConversationChangeKind.MessageAdded, added.Kind);
        Assert.Equal(3, added.Index);
        Assert.Null(added.Content);

        var contentAdded = ChatConversationChange.ContentAdded(message, content, 1);
        Assert.Equal(ChatConversationChangeKind.ContentAdded, contentAdded.Kind);
        Assert.Same(content, contentAdded.Content);
        Assert.Equal(1, contentAdded.Index);

        var contentRemoved = ChatConversationChange.ContentRemoved(message, content, 0);
        Assert.Equal(ChatConversationChangeKind.ContentRemoved, contentRemoved.Kind);

        var changed = ChatConversationChange.MessageChanged(message);
        Assert.Equal(-1, changed.Index);

        var contentChanged = ChatConversationChange.ContentChanged(message, content);
        Assert.Equal(-1, contentChanged.Index);

        var removed = ChatConversationChange.MessageRemoved(message, 2);
        Assert.Equal(ChatConversationChangeKind.MessageRemoved, removed.Kind);

        var status = ChatConversationChange.StatusChanged(ChatConversationStatus.AwaitingInput);
        Assert.Equal(ChatConversationStatus.AwaitingInput, status.Status);
        Assert.Null(status.Message);

        Assert.Equal(ChatConversationChangeKind.Reset, ChatConversationChange.Reset().Kind);
        Assert.Equal(ChatConversationChangeKind.Reset, default(ChatConversationChange).Kind);
    }
}
