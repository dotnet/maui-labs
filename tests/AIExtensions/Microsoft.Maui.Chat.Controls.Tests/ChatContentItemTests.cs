using Microsoft.Maui.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.Chat.Controls.Tests;

/// <summary>Covers <see cref="ChatContentItem"/>: the projected row and its grouping flags.</summary>
public class ChatContentItemTests
{
    [Fact]
    public void Constructor_ExposesMessageContentAndParticipant()
    {
        var participant = ChatFactory.Remote();
        var content = new TextMessageContent("hi");
        var message = new ConversationMessage(participant);
        message.Contents.Add(content);

        var item = new ChatContentItem(message, content);

        Assert.Same(message, item.Message);
        Assert.Same(content, item.Content);
        Assert.Same(participant, item.Participant);
        Assert.Equal(message.CreatedAt, item.Timestamp);
        Assert.Null(item.Conversation);
    }

    [Fact]
    public void Constructor_WithNulls_Throws()
    {
        var message = new ConversationMessage(ChatFactory.Remote());

        Assert.Throws<ArgumentNullException>(() => new ChatContentItem(null!, new TextMessageContent()));
        Assert.Throws<ArgumentNullException>(() => new ChatContentItem(message, null!));
    }

    [Fact]
    public void Constructor_WithoutAppearance_UsesTheSharedDefault()
    {
        var item = ChatFactory.Item(ChatFactory.Remote());

        Assert.Same(ChatAppearance.Default, item.Appearance);
    }

    [Fact]
    public void Appearance_SetToNull_FallsBackToTheSharedDefault()
    {
        var item = ChatFactory.Item(ChatFactory.Remote(), appearance: new ChatAppearance());

        item.Appearance = null!;

        Assert.Same(ChatAppearance.Default, item.Appearance);
    }

    [Fact]
    public void Appearance_Change_Notifies()
    {
        var item = ChatFactory.Item(ChatFactory.Remote());
        using var recorder = new PropertyRecorder(item);

        item.Appearance = new ChatAppearance();

        Assert.Equal(1, recorder.CountOf(nameof(ChatContentItem.Appearance)));
    }

    [Fact]
    public void Appearance_SetToTheSameInstance_DoesNotNotify()
    {
        var appearance = new ChatAppearance();
        var item = ChatFactory.Item(ChatFactory.Remote(), appearance: appearance);
        using var recorder = new PropertyRecorder(item);

        item.Appearance = appearance;

        Assert.Equal(0, recorder.CountOf(nameof(ChatContentItem.Appearance)));
    }

    [Fact]
    public void DefaultFlags_TreatTheRowAsAStandaloneMessage()
    {
        var item = ChatFactory.Item(ChatFactory.Remote());

        Assert.True(item.IsFirstInMessage);
        Assert.True(item.IsLastInMessage);
        Assert.True(item.IsFirstFromParticipant);
        Assert.True(item.IsLastFromParticipant);
        Assert.False(item.IsOutgoing);
        Assert.True(item.IsIncoming);
    }

    [Fact]
    public void IsOutgoing_WithoutConversation_FollowsTheParticipantKind()
    {
        Assert.True(ChatFactory.Item(ChatFactory.Local()).IsOutgoing);
        Assert.False(ChatFactory.Item(ChatFactory.Agent()).IsOutgoing);
    }

    [Fact]
    public void IsOutgoing_WithConversation_FollowsTheLocalParticipant()
    {
        var conversation = ChatFactory.Conversation(out var local, out var remote);

        Assert.True(ChatFactory.Item(local, conversation: conversation).IsOutgoing);
        Assert.False(ChatFactory.Item(remote, conversation: conversation).IsOutgoing);
    }

    [Fact]
    public void IsOutgoingFor_MatchesOnParticipantId()
    {
        var conversation = ChatFactory.Conversation(out var local, out _);
        var sameIdentity = new ChatParticipant(local.Id, "Me on another device");

        Assert.True(ChatContentItem.IsOutgoingFor(conversation, sameIdentity));
        Assert.False(ChatContentItem.IsOutgoingFor(conversation, null));
        Assert.False(ChatContentItem.IsOutgoingFor(null, ChatFactory.Remote()));
        Assert.True(ChatContentItem.IsOutgoingFor(null, ChatFactory.Local()));
    }

    [Fact]
    public void UpdateFlags_NotifiesOnlyWhatChanged()
    {
        var item = ChatFactory.Item(ChatFactory.Remote());
        using var recorder = new PropertyRecorder(item);

        item.UpdateFlags(true, true, false, false, false);

        Assert.True(item.IsOutgoing);
        Assert.False(item.IsIncoming);
        Assert.True(item.IsFirstInMessage);
        Assert.False(item.IsLastInMessage);
        Assert.False(item.IsFirstFromParticipant);
        Assert.False(item.IsLastFromParticipant);

        Assert.Contains(nameof(ChatContentItem.IsOutgoing), recorder.Names);
        Assert.Contains(nameof(ChatContentItem.IsIncoming), recorder.Names);
        Assert.DoesNotContain(nameof(ChatContentItem.IsFirstInMessage), recorder.Names);
    }

    [Fact]
    public void UpdateFlags_WithNoChange_DoesNotNotify()
    {
        var item = ChatFactory.Item(ChatFactory.Remote());
        item.UpdateFlags(false, true, true, true, true);
        using var recorder = new PropertyRecorder(item);

        item.UpdateFlags(false, true, true, true, true);

        Assert.Empty(recorder.Names);
    }

    [Fact]
    public void NotifyContentUpdated_RaisesContent()
    {
        var item = ChatFactory.Item(ChatFactory.Remote());
        using var recorder = new PropertyRecorder(item);

        item.NotifyContentUpdated();

        Assert.Equal([nameof(ChatContentItem.Content)], recorder.Names);
    }

    [Fact]
    public void NotifyMessageUpdated_RaisesMessage()
    {
        var item = ChatFactory.Item(ChatFactory.Remote());
        using var recorder = new PropertyRecorder(item);

        item.NotifyMessageUpdated();

        Assert.Equal([nameof(ChatContentItem.Message)], recorder.Names);
    }
}
