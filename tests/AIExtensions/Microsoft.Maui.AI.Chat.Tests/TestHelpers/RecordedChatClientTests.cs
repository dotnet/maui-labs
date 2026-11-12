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

    [Fact]
    public async Task RecordingChatClient_WritesARecordingThatReplaysTheSameUpdates()
    {
        var createdAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        ChatResponseUpdate[] streamed =
        [
            new(ChatRole.Assistant, "Checking") { ResponseId = "r-1", MessageId = "m-1", ModelId = "model", CreatedAt = createdAt },
            new(ChatRole.Assistant, [new FunctionCallContent("call-1", "GetWeather", new Dictionary<string, object?> { ["city"] = "Seattle", ["days"] = 3 })])
            {
                ResponseId = "r-1",
                MessageId = "m-1",
                FinishReason = ChatFinishReason.ToolCalls,
            },
            new(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5, TotalTokenCount = 15 })]),
        ];
        var provider = new DelegatingStreamingChatClient();
        provider.SetHandler((_, _, _) => streamed.ToAsyncEnumerable());
        var path = Path.Combine(Path.GetTempPath(), $"recording-{Guid.NewGuid():N}.json");
        try
        {
            using (var recorder = new RecordingChatClient(provider, path))
            {
                Assert.Equal(streamed, await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Weather?")]).ToListAsync());
                await recorder.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Again?")]).ToListAsync();
            }

            var replay = RecordedChatClient.LoadFile(path);
            Assert.Equal(2, replay.InteractionCount);
            for (var i = 0; i < 2; i++)
                Assert.Equal(streamed.Select(Describe), (await replay.GetStreamingResponseAsync([]).ToListAsync()).Select(Describe));
            replay.AssertFullyReplayed();
        }
        finally
        {
            File.Delete(path);
        }

        static string Describe(ChatResponseUpdate update) =>
            $"{update.Role} {update.ResponseId} {update.MessageId} {update.ModelId} {update.CreatedAt:O} {update.FinishReason}: " +
            string.Join(", ", update.Contents.Select(content => content switch
            {
                TextContent text => $"text '{text.Text}'",
                FunctionCallContent call => $"call {call.CallId} {call.Name}({string.Join(", ", call.Arguments!.Select(argument => $"{argument.Key}={argument.Value}"))})",
                UsageContent usage => $"usage {usage.Details.InputTokenCount}/{usage.Details.OutputTokenCount}/{usage.Details.TotalTokenCount}",
                _ => content.GetType().Name,
            }));
    }

    [Fact]
    public async Task RecordingChatClient_ContentItCantReplay_Throws()
    {
        var provider = new DelegatingStreamingChatClient();
        provider.SetHandler((_, _, _) => new[] { new ChatResponseUpdate(ChatRole.Assistant, [new DataContent(new byte[] { 1 }, "image/png")]) }.ToAsyncEnumerable());
        var path = Path.Combine(Path.GetTempPath(), $"recording-{Guid.NewGuid():N}.json");
        using var recorder = new RecordingChatClient(provider, path);

        await Assert.ThrowsAsync<NotSupportedException>(() => recorder.GetStreamingResponseAsync([]).ToListAsync().AsTask());
        Assert.False(File.Exists(path));
    }
}
