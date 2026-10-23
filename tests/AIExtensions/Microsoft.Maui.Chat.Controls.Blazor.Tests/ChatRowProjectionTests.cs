// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Chat.Controls.Blazor.Tests;

/// <summary>
/// Verifies that <see cref="ChatRowProjection"/> produces the same grouping semantics the
/// native <c>ChatContentItem</c> does, so the two projection paths cannot drift.
/// </summary>
public class ChatRowProjectionTests
{
    [Fact]
    public void Project_EmptyConversation_ReturnsEmptyList()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local);

        var rows = ChatRowProjection.Project(conversation);

        Assert.Empty(rows);
    }

    [Fact]
    public void Project_Null_ReturnsEmptyList()
    {
        var rows = ChatRowProjection.Project(conversation: null);

        Assert.Empty(rows);
    }

    [Fact]
    public void Project_SingleMessage_SingleContent_MarksBothEnds()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local);
        conversation.AddMessage(new ConversationMessage(local, "Hello"));

        var rows = ChatRowProjection.Project(conversation);

        var row = Assert.Single(rows);
        Assert.True(row.IsOutgoing);
        Assert.True(row.IsFirstInMessage);
        Assert.True(row.IsLastInMessage);
        Assert.True(row.IsFirstFromParticipant);
        Assert.True(row.IsLastFromParticipant);
    }

    [Fact]
    public void Project_MultipleContentsSameMessage_MarksFirstAndLast()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local);
        var message = new ConversationMessage(local);
        message.Contents.Add(new TextMessageContent("first"));
        message.Contents.Add(new TextMessageContent("middle"));
        message.Contents.Add(new TextMessageContent("last"));
        conversation.AddMessage(message);

        var rows = ChatRowProjection.Project(conversation);

        Assert.Equal(3, rows.Count);
        Assert.True(rows[0].IsFirstInMessage);
        Assert.False(rows[0].IsLastInMessage);
        Assert.True(rows[0].IsFirstFromParticipant);
        Assert.False(rows[0].IsLastFromParticipant);

        Assert.False(rows[1].IsFirstInMessage);
        Assert.False(rows[1].IsLastInMessage);
        Assert.False(rows[1].IsFirstFromParticipant);
        Assert.False(rows[1].IsLastFromParticipant);

        Assert.False(rows[2].IsFirstInMessage);
        Assert.True(rows[2].IsLastInMessage);
        Assert.False(rows[2].IsFirstFromParticipant);
        Assert.True(rows[2].IsLastFromParticipant);
    }

    [Fact]
    public void Project_ConsecutiveMessagesFromSameParticipant_GroupThem()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local);
        conversation.AddMessage(new ConversationMessage(local, "one"));
        conversation.AddMessage(new ConversationMessage(local, "two"));
        conversation.AddMessage(new ConversationMessage(local, "three"));

        var rows = ChatRowProjection.Project(conversation);

        Assert.Equal(3, rows.Count);
        Assert.True(rows[0].IsFirstFromParticipant);
        Assert.False(rows[0].IsLastFromParticipant);

        Assert.False(rows[1].IsFirstFromParticipant);
        Assert.False(rows[1].IsLastFromParticipant);

        Assert.False(rows[2].IsFirstFromParticipant);
        Assert.True(rows[2].IsLastFromParticipant);
    }

    [Fact]
    public void Project_ParticipantChangeBreaksGroup()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        var remote = new ChatParticipant("them", "Them", ChatParticipantKind.Remote);
        var conversation = new ObservableChatConversation(local);
        conversation.Participants.Add(remote);
        conversation.AddMessage(new ConversationMessage(local, "hi"));
        conversation.AddMessage(new ConversationMessage(remote, "hello"));

        var rows = ChatRowProjection.Project(conversation);

        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].IsOutgoing);
        Assert.True(rows[0].IsLastFromParticipant);

        Assert.False(rows[1].IsOutgoing);
        Assert.True(rows[1].IsFirstFromParticipant);
    }

    [Fact]
    public void Project_LocalParticipantOverride_IsOutgoingFollowsConversation()
    {
        var alice = new ChatParticipant("a", "Alice", ChatParticipantKind.Remote);
        var bob = new ChatParticipant("b", "Bob", ChatParticipantKind.Remote);
        var conversation = new ObservableChatConversation(alice);
        conversation.Participants.Add(bob);

        conversation.AddMessage(new ConversationMessage(alice, "a1"));
        conversation.AddMessage(new ConversationMessage(bob, "b1"));

        var rows = ChatRowProjection.Project(conversation);

        Assert.True(rows[0].IsOutgoing);
        Assert.False(rows[1].IsOutgoing);
    }

    [Fact]
    public void Project_SkipsMessagesWithNoContent()
    {
        var local = new ChatParticipant("me", "Me", ChatParticipantKind.Local);
        var conversation = new ObservableChatConversation(local);
        conversation.AddMessage(new ConversationMessage(local));
        conversation.AddMessage(new ConversationMessage(local, "visible"));

        var rows = ChatRowProjection.Project(conversation);

        var only = Assert.Single(rows);
        Assert.Equal("visible", Assert.IsType<TextMessageContent>(only.Content).Text);
    }
}
