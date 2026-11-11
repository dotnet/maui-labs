using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

#pragma warning disable MEAI001 // Tool approval content is experimental in the installed SDK.
public sealed class ChatReplayDriverTests
{
    [Fact]
    public async Task NextTurn_ReplaysOneUserTurnAtATimeWithoutTheLiveClient()
    {
        using var model = new ScriptedChatClient();
        using var harness = new PlaygroundChatHarness(model);
        await harness.LoadAsync("multi-turn-tools.json");
        var started = new List<int>();
        harness.Driver.InteractionStarting += (_, interaction) => started.Add(interaction.Sequence);

        var first = await harness.Driver.ReplayAsync(playAll: false);

        Assert.Equal(1, first.Turns);
        Assert.Equal(1, harness.Recording.ReplayPosition);
        Assert.Single(harness.Session.Turns);

        var second = await harness.Driver.ReplayAsync(playAll: false);

        // The next turn continues the replayed conversation instead of starting a new one.
        Assert.Equal(1, second.Turns);
        Assert.Equal(0, second.RestoredInteractions);
        Assert.Equal(2, harness.Recording.ReplayPosition);
        Assert.Equal(2, harness.Session.Turns.Count);
        Assert.Equal([0, 1], started);
        Assert.Empty(model.Requests);
        Assert.False(harness.Driver.IsReplaying);
        Assert.False(harness.Router.IsReplaying);

        var finished = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Driver.ReplayAsync(playAll: false));
        Assert.Equal("No more turns to replay. Press Play all to start again.", finished.Message);
    }

    [Fact]
    public async Task PlayAll_StartsAgainFromAClearedConversation()
    {
        using var harness = new PlaygroundChatHarness();
        await harness.LoadAsync("multi-turn-tools.json");
        await harness.Driver.ReplayAsync(playAll: false);
        var added = 0;
        harness.Session.RegisterOnTurnAdded(_ => added++);

        var result = await harness.Driver.ReplayAsync(playAll: true);

        Assert.Equal(2, result.Turns);
        Assert.Equal(2, added);
        Assert.Equal(2, harness.Session.Turns.Count);
        Assert.Equal(2, harness.Recording.ReplayPosition);

        // Restart with a different recording replaces the conversation.
        await harness.LoadAsync("no-tools.json");
        await harness.Driver.ReplayAsync(playAll: true);
        Assert.Equal("Reply with exactly the word cobalt.", UserText(Assert.Single(harness.Session.Turns)));
    }

    [Fact]
    public async Task Replay_WithoutARecording_SaysThereIsNothingToReplay()
    {
        using var harness = new PlaygroundChatHarness();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Driver.ReplayAsync(playAll: true));

        Assert.Equal("There is no chat to replay.", error.Message);
        Assert.False(harness.Router.IsReplaying);
    }

    [Fact]
    public async Task RecordedApprovals_AreRejectedAndApprovedAsRecorded()
    {
        using var model = new ScriptedChatClient();
        using var harness = new PlaygroundChatHarness(model);
        await harness.LoadAsync("approval-reject-then-approve.json");
        var statuses = new List<ConversationStatus>();
        harness.Session.RegisterOnStatusChanged(statuses.Add);

        var result = await harness.Driver.ReplayAsync(playAll: true);

        Assert.Equal(1, result.Turns);
        Assert.Empty(result.UnansweredApprovals);
        Assert.Equal(3, harness.Recording.ReplayPosition);
        Assert.Empty(model.Requests);
        Assert.Equal(2, statuses.Count(status => status == ConversationStatus.AwaitingInput));
        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        var turn = Assert.Single(harness.Session.Turns);
        Assert.False(turn.IsStopped);
        var approvals = turn.ResponseBlocks.OfType<ToolApprovalBlock>().ToArray();
        Assert.Equal([ApprovalStatus.Rejected, ApprovalStatus.Approved], approvals.Select(approval => approval.Status));
        Assert.Equal("12 \u00D7 7", CalculatorDisplay.FormatExpression(approvals[1].Arguments, CultureInfo.InvariantCulture));
        Assert.Equal("84", CalculatorDisplay.FormatResult(approvals[1].Result, CultureInfo.InvariantCulture));
        Assert.Equal("12 \u00D7 7 = 84.", Assert.Single(turn.ResponseBlocks.OfType<TextContentBlock>()).RawText);

        // Each continuation carried the recorded decision.
        Assert.Equal([false, true], harness.ReplayRequests.Requests.Skip(1)
            .Select(request => Assert.Single(request[^1].Contents.OfType<ToolApprovalResponseContent>()).Approved));
    }

    [Fact]
    public async Task RecordedRejection_SendsItsReason()
    {
        using var harness = new PlaygroundChatHarness();
        var call = CalculatorCall("call-1");
        var approvalRequest = new ToolApprovalRequestContent("approval-1", call);
        var question = new ChatMessage(ChatRole.User, "What is 12 \u00D7 7?");
        Record(harness, [question], new ChatResponse(new ChatMessage(ChatRole.Assistant, [approvalRequest])));
        Record(harness,
            [
                question,
                new ChatMessage(ChatRole.Assistant, [approvalRequest]),
                new ChatMessage(ChatRole.User, [approvalRequest.CreateResponse(approved: false, reason: "Not now")]),
            ],
            new ChatResponse(
            [
                new ChatMessage(ChatRole.Assistant, [call]),
                new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "Tool call invocation rejected. Not now")]),
                new ChatMessage(ChatRole.Assistant, "Okay, I will not calculate it."),
            ]));

        await harness.Driver.ReplayAsync(playAll: true);

        var approval = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Rejected, approval.Status);
        var sent = Assert.Single(harness.ReplayRequests.Requests[1][^1].Contents.OfType<ToolApprovalResponseContent>());
        Assert.False(sent.Approved);
        Assert.Equal("Not now", sent.Reason);
        Assert.Equal("Okay, I will not calculate it.",
            Assert.Single(harness.LastTurn.ResponseBlocks.OfType<TextContentBlock>()).RawText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApprovalWithoutARecordedAnswer_StopsTheTurnInsteadOfWaiting(bool anotherTurnFollows)
    {
        using var harness = new PlaygroundChatHarness();
        var question = new ChatMessage(ChatRole.User, "What is 12 \u00D7 7?");
        Record(harness, [question], new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new ToolApprovalRequestContent("approval-1", CalculatorCall("call-1"))])));
        if (anotherTurnFollows)
        {
            Record(harness, [new ChatMessage(ChatRole.User, "Never mind. Hello!")],
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello!")));
        }

        var result = await harness.Driver.ReplayAsync(playAll: true).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["calculate"], result.UnansweredApprovals);
        Assert.Equal(anotherTurnFollows ? 2 : 1, result.Turns);
        Assert.Equal(anotherTurnFollows ? 2 : 1, harness.Recording.ReplayPosition);
        var stopped = harness.Session.Turns[0];
        Assert.True(stopped.IsStopped);
        Assert.Equal(ApprovalStatus.Pending, Assert.Single(stopped.ResponseBlocks.OfType<ToolApprovalBlock>()).Status);
        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        if (anotherTurnFollows)
            Assert.Equal("Hello!", Assert.Single(harness.LastTurn.ResponseBlocks.OfType<TextContentBlock>()).RawText);
    }

    [Fact]
    public async Task InteractionThatIsNotAUserTurn_IsRestoredAsHistory()
    {
        using var model = new ScriptedChatClient();
        using var harness = new PlaygroundChatHarness(model);
        ChatMessage[] conversation =
        [
            new(ChatRole.User, "What time is it?"),
            new(ChatRole.Assistant, [new FunctionCallContent("call-1", "get_current_local_datetime")]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "10:30")]),
        ];

        // A client that runs tools itself sends their results; that request has no user message to send again.
        Record(harness, conversation, new ChatResponse(new ChatMessage(ChatRole.Assistant, "It is 10:30.")));
        Record(harness,
            [.. conversation, new(ChatRole.Assistant, "It is 10:30."), new(ChatRole.User, "Thanks!")],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "You're welcome.")));

        var result = await harness.Driver.ReplayAsync(playAll: true);

        Assert.Equal(1, result.RestoredInteractions);
        Assert.Equal(1, result.Turns);
        Assert.Equal(2, harness.Recording.ReplayPosition);
        Assert.Empty(model.Requests);
        Assert.Collection(harness.Session.Turns,
            restored =>
            {
                Assert.Equal("What time is it?", UserText(restored));
                Assert.Collection(restored.ResponseBlocks,
                    block => Assert.True(Assert.IsType<FunctionInvocationContentBlock>(block).HasResult),
                    block => Assert.Equal("It is 10:30.", Assert.IsType<TextContentBlock>(block).RawText));
            },
            replayed =>
            {
                Assert.Equal("Thanks!", UserText(replayed));
                Assert.Equal("You're welcome.", Assert.IsType<TextContentBlock>(Assert.Single(replayed.ResponseBlocks)).RawText);
            });
    }

    [Fact]
    public async Task StoppedReplay_LeavesTheInterruptedInteractionToReplayAgain()
    {
        using var harness = new PlaygroundChatHarness(new ScriptedChatClient());
        await harness.LoadAsync("one-tool.json");
        using var stop = new CancellationTokenSource();
        using (harness.Session.RegisterOnBlockAdded((turn, block) =>
        {
            if (turn.ResponseBlocks.Contains(block))
                stop.Cancel();
        }))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => harness.Driver.ReplayAsync(playAll: true, stop.Token));
        }

        Assert.Equal(0, harness.Recording.ReplayPosition);
        Assert.True(harness.LastTurn.IsStopped);
        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        Assert.False(harness.Driver.IsReplaying);
        Assert.False(harness.Router.IsReplaying);

        var again = await harness.Driver.ReplayAsync(playAll: false);

        Assert.Equal(1, again.Turns);
        Assert.Equal(1, harness.Recording.ReplayPosition);
        Assert.False(Assert.Single(harness.Session.Turns).IsStopped);
    }

    [Fact]
    public async Task FailedReplayTurn_ThrowsTheEngineError()
    {
        using var harness = new PlaygroundChatHarness();
        await harness.LoadAsync("no-tools.json");
        using var registration = harness.Session.RegisterOnBlockAdded((turn, block) =>
        {
            if (turn.ResponseBlocks.Contains(block))
                harness.Recording.NewRecording();
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Driver.ReplayAsync(playAll: true));

        Assert.Equal("Replay state changed before the interaction completed.", error.Message);
        Assert.Same(harness.Session.Error, error.InnerException);
        Assert.Equal(ConversationStatus.Error, harness.Session.Status);
        Assert.False(harness.Driver.IsReplaying);
        Assert.False(harness.Router.IsReplaying);
    }

    [Fact]
    public async Task ReplayWhileReplaying_IsRejected()
    {
        using var harness = new PlaygroundChatHarness();
        await harness.LoadAsync("approval-reject-then-approve.json");
        Task? nested = null;
        using var registration = harness.Session.RegisterOnStatusChanged(status =>
        {
            if (status == ConversationStatus.Streaming)
                nested ??= harness.Driver.ReplayAsync(playAll: true);
        });

        await harness.Driver.ReplayAsync(playAll: true);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => nested!);
        Assert.Equal("A replay is already running.", error.Message);
        Assert.Equal(3, harness.Recording.ReplayPosition);
    }

    [Fact]
    public void IsUserTurn_AcceptsWhatAUserTypesOrAttaches()
    {
        Assert.True(ChatReplayDriver.IsUserTurn(new ChatMessage(ChatRole.User, "Hello")));
        Assert.True(ChatReplayDriver.IsUserTurn(new ChatMessage(ChatRole.User,
            [new TextContent("Hello"), new DataContent(new byte[] { 1 }, "image/png"), new UriContent("https://example.com/a.png", "image/png")])));
    }

    [Fact]
    public void IsUserTurn_RejectsContinuationsAndOtherRoles()
    {
        var approval = new ToolApprovalRequestContent("approval-1", CalculatorCall("call-1"));

        Assert.False(ChatReplayDriver.IsUserTurn(new ChatMessage(ChatRole.User, [approval.CreateResponse(approved: true)])));
        Assert.False(ChatReplayDriver.IsUserTurn(new ChatMessage(ChatRole.User, [new FunctionResultContent("call-1", 4)])));
        Assert.False(ChatReplayDriver.IsUserTurn(new ChatMessage(ChatRole.User, [new TextContent("Hi"), new FunctionResultContent("call-1", 4)])));
        Assert.False(ChatReplayDriver.IsUserTurn(new ChatMessage(ChatRole.User, [])));
        Assert.False(ChatReplayDriver.IsUserTurn(new ChatMessage(ChatRole.Assistant, "Hello")));
        Assert.False(ChatReplayDriver.IsUserTurn(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", 4)])));
    }

    internal static FunctionCallContent CalculatorCall(string callId) =>
        new(callId, "calculate", new Dictionary<string, object?> { ["left"] = 12, ["operation"] = "multiply", ["right"] = 7 });

    internal static string UserText(ConversationTurn turn) =>
        string.Concat(turn.RequestBlocks.OfType<TextContentBlock>().Select(block => block.RawText));

    private static void Record(PlaygroundChatHarness harness, IEnumerable<ChatMessage> request, ChatResponse response) =>
        harness.Recording.AddResponse(ChatRecordingSerializer.Request(request, null), response);
}
#pragma warning restore MEAI001
