// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.AI.Chat.Tests.TestHelpers;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests;

public class RecordingLoaderTests
{
    [Theory]
    [InlineData("Step01_GettingStartedTest.PostRun_MultiTurn_SynthesizesAssistantMessages.recording.json", 2)]
    [InlineData("Step02_BackendToolsTest.PostRun_WithBackendToolCall_InvokesToolAndStreamsResult.recording.json", 1)]
    [InlineData("Step04_HumanInLoopTest.PostRun_WithApprovalRequired_ApprovesAndExecutesTool.recording.json", 1)]
    [InlineData("Step05_StateManagementTest.PostRun_WithState_EmitsStateSnapshotAndSummary.recording.json", 2)]
    public void Load_DeserializesExpectedTurnCount(string fileName, int expectedTurns)
    {
        var turns = RecordingLoader.Load(fileName);
        Assert.Equal(expectedTurns, turns.Interactions.Count);
        Assert.All(
            turns.Interactions,
            interaction => Assert.NotEmpty(interaction.Updates));
    }

    [Fact]
    public async Task CreateReplayClient_YieldsTurnsInOrder()
    {
        var client = RecordingLoader.CreateReplayClient(
            "Step01_GettingStartedTest.PostRun_MultiTurn_SynthesizesAssistantMessages.recording.json");

        var first = await client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "test")]).ToListAsync();
        var second = await client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "test again")]).ToListAsync();

        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        client.Session.AssertFullyReplayed();
    }
}
