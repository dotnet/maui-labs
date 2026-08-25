// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Chat.Controls.Blazor.Tests;

/// <summary>Verifies <see cref="ChatMessageRowModel"/> semantics.</summary>
public class ChatMessageRowModelTests
{
    [Fact]
    public void Key_CombinesMessageAndContentIds()
    {
        var participant = new ChatParticipant("p", "P");
        var message = new ConversationMessage(participant, id: "m1", createdAt: null);
        var content = new TextMessageContent("hi", id: "c1");
        var row = new ChatMessageRowModel(
            message, content, IsOutgoing: false,
            IsFirstInMessage: true, IsLastInMessage: true,
            IsFirstFromParticipant: true, IsLastFromParticipant: true,
            ContentVersion: 0);

        Assert.Equal("m1::c1", row.Key);
    }

    [Fact]
    public void IsIncoming_InvertsIsOutgoing()
    {
        var participant = new ChatParticipant("p", "P");
        var message = new ConversationMessage(participant);
        var content = new TextMessageContent("hi");
        var incoming = new ChatMessageRowModel(
            message, content, IsOutgoing: false,
            IsFirstInMessage: true, IsLastInMessage: true,
            IsFirstFromParticipant: true, IsLastFromParticipant: true,
            ContentVersion: 0);
        var outgoing = incoming with { IsOutgoing = true };

        Assert.True(incoming.IsIncoming);
        Assert.False(outgoing.IsIncoming);
    }

    [Fact]
    public void Participant_And_Timestamp_ProjectFromMessage()
    {
        var participant = new ChatParticipant("p", "P");
        var when = DateTimeOffset.Now.AddMinutes(-3);
        var message = new ConversationMessage(participant, id: null, createdAt: when);
        var content = new TextMessageContent("hi");
        var row = new ChatMessageRowModel(
            message, content, IsOutgoing: false,
            IsFirstInMessage: true, IsLastInMessage: true,
            IsFirstFromParticipant: true, IsLastFromParticipant: true,
            ContentVersion: 0);

        Assert.Same(participant, row.Participant);
        Assert.Equal(when, row.Timestamp);
    }
}
