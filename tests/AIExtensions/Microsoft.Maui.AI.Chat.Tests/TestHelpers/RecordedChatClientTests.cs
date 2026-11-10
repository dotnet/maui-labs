// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests;

public class RecordedChatClientTests
{
    [Theory]
    [InlineData("Step01_GettingStartedTest.PostRun_MultiTurn_SynthesizesAssistantMessages.recording.json", 2)]
    [InlineData("Step02_BackendToolsTest.PostRun_WithBackendToolCall_InvokesToolAndStreamsResult.recording.json", 1)]
    [InlineData("Step04_HumanInLoopTest.PostRun_WithApprovalRequired_ApprovesAndExecutesTool.recording.json", 1)]
    [InlineData("Step05_StateManagementTest.PostRun_WithState_EmitsStateSnapshotAndSummary.recording.json", 2)]
    public async Task Load_ReplaysEveryRecordedInteraction(string fileName, int expectedInteractions)
    {
        var client = RecordedChatClient.Load(fileName);
        Assert.Equal(expectedInteractions, client.InteractionCount);

        for (var i = 0; i < expectedInteractions; i++)
        {
            var updates = await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")]).ToListAsync();
            Assert.NotEmpty(updates);
        }

        client.AssertFullyReplayed();
    }

    [Fact]
    public async Task GetStreamingResponseAsync_AfterLastInteraction_Throws()
    {
        var client = RecordedChatClient.Load(
            "Step04_HumanInLoopTest.PostRun_WithApprovalRequired_ApprovesAndExecutesTool.recording.json");

        var updates = await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "approve")]).ToListAsync();
        var call = Assert.IsType<FunctionCallContent>(Assert.Single(Assert.Single(updates).Contents));
        Assert.Equal("ApproveExpenseReport", call.Name);
        Assert.Equal("EXP-2024-001", call.Arguments?["expenseReportId"]);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "again")]).ToListAsync());
    }
}
