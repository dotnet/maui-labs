// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// The history leaves out the calls that no later result answers, with their approvals, matching calls and results in
/// history order, so a provider that numbers its calls per response can reuse an id.
/// </summary>
public class UnansweredCallsTests
{
    [Fact]
    public void CallIdReusedAfterItsResult_OnlyTheUnansweredCallIsRemoved()
    {
        var answered = new FunctionCallContent("0", "Search");
        var reused = new FunctionCallContent("0", "SetTheme");

        var kept = UnansweredCalls.Remove(
        [
            new ChatMessage(ChatRole.User, "Search, then go dark"),
            new ChatMessage(ChatRole.Assistant, [answered]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("0", "Sunny")]),
            new ChatMessage(ChatRole.Assistant, [new TextContent("Now the theme."), reused]),
        ]);

        Assert.Equal(4, kept.Count);
        Assert.Same(answered, Assert.Single(kept[1].Contents));
        Assert.Equal("Now the theme.", Assert.IsType<TextContent>(Assert.Single(kept[3].Contents)).Text);
    }

    [Fact]
    public void ApprovalAnsweredByItsCallsResult_IsKept_EvenWhenTheResultComesBeforeTheDecision()
    {
        // The client answers an approved UI action with the action's result, before the approval response.
        var call = new FunctionCallContent("0", "ShowMap");
        var request = new ToolApprovalRequestContent("ficc_0", call);
        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.User, "Show Paris"),
            new ChatMessage(ChatRole.Assistant, [request]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("0", "Map of Paris")]),
            new ChatMessage(ChatRole.User, [request.CreateResponse(approved: true)]),
        ];

        Assert.Equal(messages, UnansweredCalls.Remove(messages));
    }

    [Fact]
    public void ApprovedCallWithoutAResult_IsRemovedWithItsRequestResponseAndReplay()
    {
        // Stopped while FunctionInvokingChatClient ran the approved call, which it had sent again before its result.
        var call = new FunctionCallContent("0", "DeleteFile");
        var request = new ToolApprovalRequestContent("ficc_0", call);

        var kept = UnansweredCalls.Remove(
        [
            new ChatMessage(ChatRole.User, "Delete a.txt"),
            new ChatMessage(ChatRole.Assistant, [request]),
            new ChatMessage(ChatRole.User, [request.CreateResponse(approved: true)]),
            new ChatMessage(ChatRole.Assistant, [call]),
        ]);

        Assert.Equal("Delete a.txt", Assert.Single(kept).Text);
    }

    [Fact]
    public void ApprovalsThatReuseAnId_EachBelongToTheirOwnCall()
    {
        // FunctionInvokingChatClient derives the request id from the call id, so it repeats with the call id.
        var first = new ToolApprovalRequestContent("ficc_0", new FunctionCallContent("0", "DeleteFile"));
        var second = new ToolApprovalRequestContent("ficc_0", new FunctionCallContent("0", "DeleteFile"));
        var firstResponse = first.CreateResponse(approved: true);

        var kept = UnansweredCalls.Remove(
        [
            new ChatMessage(ChatRole.User, "Delete a.txt and b.txt"),
            new ChatMessage(ChatRole.Assistant, [first]),
            new ChatMessage(ChatRole.User, [firstResponse]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("0", "Deleted a.txt")]),
            new ChatMessage(ChatRole.Assistant, [second]),
            new ChatMessage(ChatRole.User, [second.CreateResponse(approved: true)]),
        ]);

        Assert.Equal(4, kept.Count);
        Assert.Same(first, Assert.Single(kept[1].Contents));
        Assert.Same(firstResponse, Assert.Single(kept[2].Contents));
    }
}
