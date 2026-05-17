using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests;

public class ChatSessionTests
{
    [Fact]
    public async Task SendAsync_AddsUserAndAssistantMessages()
    {
        var client = new SequenceChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Hello!")])]);

        var session = new ChatSession([], client);
        await session.SendAsync("Hi");

        Assert.Equal(2, session.Messages.Count);
        Assert.Equal(ContentRole.User, session.Messages[0].Role);
        Assert.Equal(ContentRole.Assistant, session.Messages[1].Role);
        Assert.Equal("Hello!", ((TextContent)session.Messages[1].Content).Text);
    }

    [Fact]
    public async Task SendAsync_TrimsWhitespace()
    {
        var client = new SequenceChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("OK")])]);

        var session = new ChatSession([], client);
        await session.SendAsync("  hello  ");

        Assert.Equal("hello", ((TextContent)session.Messages[0].Content).Text);
    }

    [Fact]
    public async Task SendAsync_IgnoresEmptyMessages()
    {
        var client = new SequenceChatClient();
        var session = new ChatSession([], client);

        await session.SendAsync("");
        await session.SendAsync("   ");

        Assert.Empty(session.Messages);
    }

    [Fact]
    public async Task SendAsync_SetsAndClearsIsBusy()
    {
        var busyStates = new List<bool>();
        var client = new SequenceChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Hi")])]);

        var session = new ChatSession([], client);
        session.Changed += (_, args) =>
        {
            if (args.Kind == ChatSessionChangeKind.StateChanged)
                busyStates.Add(session.IsBusy);
        };

        await session.SendAsync("Hello");

        Assert.Contains(true, busyStates);
        Assert.Contains(false, busyStates);
        Assert.False(session.IsBusy);
    }

    [Fact]
    public async Task SendAsync_StreamingAccumulatesText()
    {
        var client = new SequenceChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Hello")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(" World")]),
        ]);

        var session = new ChatSession([], client);
        await session.SendAsync("Hi");

        var assistant = session.Messages.Single(m => m.Role == ContentRole.Assistant);
        Assert.Equal("Hello World", ((TextContent)assistant.Content).Text);
    }

    [Fact]
    public async Task SendAsync_RaisesMessageAddedAndUpdatedEvents()
    {
        var changes = new List<ChatSessionChangeKind>();
        var client = new SequenceChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Hello")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(" World")]),
        ]);

        var session = new ChatSession([], client);
        session.Changed += (_, args) => changes.Add(args.Kind);

        await session.SendAsync("Hi");

        Assert.Contains(ChatSessionChangeKind.MessageAdded, changes);
        Assert.Contains(ChatSessionChangeKind.MessageUpdated, changes);
    }

    [Fact]
    public async Task SendAsync_AddsFunctionCallAndResultEntries()
    {
        var client = new SequenceChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant,
            [
                new FunctionCallContent("call-1", "get_weather",
                    new Dictionary<string, object?> { ["city"] = "Seattle" }),
            ]),
            new ChatResponseUpdate(ChatRole.Assistant,
            [
                new FunctionResultContent("call-1", "Sunny 72°F"),
            ]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("It's sunny!")]),
        ]);

        var session = new ChatSession([], client);
        await session.SendAsync("What's the weather?");

        Assert.Contains(session.Messages, m => m.Role == ContentRole.Tool && m.Content is FunctionCallContent);
        Assert.Contains(session.Messages, m => m.Role == ContentRole.Tool && m.Content is FunctionResultContent);
        Assert.Contains(session.Messages, m => m.Content is TextContent { Text: "It's sunny!" });
    }

    [Fact]
    public async Task SendAsync_ResolvesToolNameForResults()
    {
        var client = new SequenceChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant,
            [
                new FunctionCallContent("call-1", "get_weather"),
            ]),
            new ChatResponseUpdate(ChatRole.Assistant,
            [
                new FunctionResultContent("call-1", "Sunny"),
            ]),
        ]);

        var session = new ChatSession([], client);
        await session.SendAsync("Weather");

        var result = session.Messages.Single(m => m.Content is FunctionResultContent);
        Assert.Equal("get_weather", result.ToolName);
    }

    [Fact]
    public async Task SendAsync_HandlesErrorGracefully()
    {
        var client = new CallbackChatClient(_ =>
            throw new InvalidOperationException("Test error"));

        var session = new ChatSession([], client);
        await session.SendAsync("Fail");

        Assert.Contains(session.Messages, m => m.Role == ContentRole.Error);
        var error = session.Messages.Single(m => m.Role == ContentRole.Error);
        Assert.Equal("Test error", ((ErrorContent)error.Content).Message);
    }

    [Fact]
    public async Task SendAsync_NoResponse_AddsPlaceholder()
    {
        var client = new SequenceChatClient(Array.Empty<ChatResponseUpdate[]>());
        var session = new ChatSession([], client);

        await session.SendAsync("Hello?");

        // Should have user + (no response) messages
        Assert.Equal(2, session.Messages.Count);
    }

    [Fact]
    public async Task SendAsync_IncludesSystemPromptInHistory()
    {
        var client = new SequenceChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("OK")])]);

        var session = new ChatSession([], client)
        {
            SystemPrompt = "You are a helpful assistant."
        };

        await session.SendAsync("Hi");

        Assert.Contains(client.ReceivedMessages[0],
            m => m.Role == ChatRole.System && m.Text == "You are a helpful assistant.");
    }

    [Fact]
    public async Task SendAsync_OmitsSystemPromptWhenNull()
    {
        var client = new SequenceChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("OK")])]);

        var session = new ChatSession([], client);
        await session.SendAsync("Hi");

        Assert.DoesNotContain(client.ReceivedMessages[0], m => m.Role == ChatRole.System);
    }

    [Fact]
    public async Task ChatEntry_HasTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var client = new SequenceChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("OK")])]);

        var session = new ChatSession([], client);
        await session.SendAsync("Hi");

        var after = DateTimeOffset.UtcNow;
        foreach (var message in session.Messages)
        {
            Assert.InRange(message.Timestamp, before, after);
        }
    }

    [Fact]
    public void Clear_ResetsAllState()
    {
        var client = new SequenceChatClient();
        var session = new ChatSession([], client);

        var changes = new List<ChatSessionChangeKind>();
        session.Changed += (_, args) => changes.Add(args.Kind);

        session.Clear();

        Assert.Empty(session.Messages);
        Assert.False(session.IsBusy);
        Assert.False(session.HasPendingApprovals);
        Assert.Contains(ChatSessionChangeKind.Reset, changes);
    }

    [Fact]
    public async Task CancelAsync_StopsActiveRequest()
    {
        var tcs = new TaskCompletionSource();
        var client = new CallbackChatClient(_ =>
        {
            tcs.SetResult();
            // Return empty to simulate a slow response — the cancellation
            // will come from CancelAsync before the loop finishes
            return [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("OK")])];
        });

        var session = new ChatSession([], client);

        // Start in background
        var sendTask = session.SendAsync("Hello");
        await tcs.Task; // Wait until client is called
        await session.CancelAsync();
        await sendTask; // Should complete without throwing

        Assert.False(session.IsBusy);
    }
}

public class ChatSessionApprovalTests
{
    [Fact]
    public async Task ApprovalWorkflow_FullCycle()
    {
        var request = new ToolApprovalRequestContent(
            "approval-1",
            new FunctionCallContent("call-1", "add_plant",
                new Dictionary<string, object?> { ["nickname"] = "Fern" }));

        var client = new SequenceChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Let me check.")])],
            [new ChatResponseUpdate(ChatRole.Assistant, [request])],
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Added Fern.")])]);

        var session = new ChatSession([], client);
        await session.SendAsync("Add a fern");

        Assert.Equal(2, session.Messages.Count);
        Assert.Equal(ContentRole.Assistant, session.Messages[1].Role);

        await session.SendAsync("Continue");
        Assert.True(session.HasPendingApprovals);
        Assert.Single(session.PendingApprovals);
        Assert.Equal("add_plant", session.PendingApprovals.Single().ToolName);

        await session.SubmitApprovalAsync(request.CreateResponse(approved: true));

        Assert.False(session.HasPendingApprovals);
        Assert.Equal(ToolApprovalState.Approved,
            session.Messages.Single(m => m.Role == ContentRole.Approval).ApprovalState);
        Assert.Contains(session.Messages, m => m.Content is TextContent { Text: "Added Fern." });
    }

    [Fact]
    public async Task EditedApproval_PreservesArguments()
    {
        var originalCall = new FunctionCallContent("call-1", "add_plant",
            new Dictionary<string, object?> { ["nickname"] = "Old Name" });
        var request = new ToolApprovalRequestContent("approval-1", originalCall);

        var client = new SequenceChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, [request])],
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Updated.")])]);

        var session = new ChatSession([], client);
        await session.SendAsync("Add a plant");

        var editedResponse = new ToolApprovalResponseContent(
            request.RequestId,
            approved: true,
            new FunctionCallContent(originalCall.CallId, originalCall.Name,
                new Dictionary<string, object?> { ["nickname"] = "New Name" }));

        await session.SubmitApprovalAsync(editedResponse);

        var replayedResponse = Assert.IsType<ToolApprovalResponseContent>(
            client.ReceivedMessages[1]
                .SelectMany(m => m.Contents)
                .Single(c => c is ToolApprovalResponseContent));

        var replayedCall = Assert.IsType<FunctionCallContent>(replayedResponse.ToolCall);
        Assert.Equal("New Name", replayedCall.Arguments?["nickname"]?.ToString());
    }

    [Fact]
    public async Task EditedApproval_RejectsIdentityChanges()
    {
        var originalCall = new FunctionCallContent("call-1", "add_plant",
            new Dictionary<string, object?> { ["nickname"] = "Original" });
        var request = new ToolApprovalRequestContent("approval-1", originalCall);

        var client = new SequenceChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, [request])]);

        var session = new ChatSession([], client);
        await session.SendAsync("Add a plant");

        var invalidResponse = new ToolApprovalResponseContent(
            request.RequestId,
            approved: true,
            new FunctionCallContent(originalCall.CallId, "remove_plant",
                new Dictionary<string, object?> { ["nickname"] = "Original" }));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SubmitApprovalAsync(invalidResponse));

        Assert.True(session.HasPendingApprovals);
    }

    [Fact]
    public async Task NewMessage_AutoRejectsPendingApprovals()
    {
        var request = new ToolApprovalRequestContent(
            "approval-1",
            new FunctionCallContent("call-1", "add_plant",
                new Dictionary<string, object?> { ["nickname"] = "Fern" }));

        var client = new SequenceChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, [request])],
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Moving on.")])]);

        var session = new ChatSession([], client);
        await session.SendAsync("Add a fern");

        Assert.True(session.HasPendingApprovals);

        await session.SendAsync("Never mind");

        Assert.False(session.HasPendingApprovals);
        Assert.Equal(ToolApprovalState.Rejected,
            session.Messages.Single(m => m.Role == ContentRole.Approval).ApprovalState);
    }

    [Fact]
    public async Task MultipleSessions_IndependentApprovals()
    {
        var request1 = new ToolApprovalRequestContent("approval-1",
            new FunctionCallContent("call-1", "add_plant",
                new Dictionary<string, object?> { ["nickname"] = "Fern" }));
        var request2 = new ToolApprovalRequestContent("approval-2",
            new FunctionCallContent("call-2", "add_plant",
                new Dictionary<string, object?> { ["nickname"] = "Palm" }));

        var session1 = new ChatSession([],
            new SequenceChatClient([new ChatResponseUpdate(ChatRole.Assistant, [request1])]));
        var session2 = new ChatSession([],
            new SequenceChatClient([new ChatResponseUpdate(ChatRole.Assistant, [request2])]));

        await session1.SendAsync("Add a fern");
        await session2.SendAsync("Add a palm");

        Assert.True(session1.HasPendingApprovals);
        Assert.True(session2.HasPendingApprovals);

        session1.Clear();

        Assert.False(session1.HasPendingApprovals);
        Assert.True(session2.HasPendingApprovals);
    }

    [Fact]
    public async Task ApproveAndFollowUp_DoesNotCrash()
    {
        var plantsDb = new List<string>();

        var addPlantTool = AIFunctionFactory.Create(
            (string nickname) => { plantsDb.Add(nickname); return $"Added {nickname}"; },
            new AIFunctionFactoryOptions { Name = "add_plant" });
        var addPlantApproval = new ApprovalRequiredAIFunction(addPlantTool);

        var getPlantsTool = AIFunctionFactory.Create(
            () => plantsDb.Count > 0 ? string.Join(", ", plantsDb) : "No plants yet",
            new AIFunctionFactoryOptions { Name = "get_plants" });

        var tools = new AITool[] { addPlantApproval, getPlantsTool };

        int innerCallCount = 0;
        var innerClient = new CallbackChatClient(_ =>
        {
            innerCallCount++;
            return innerCallCount switch
            {
                1 => [new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-1", "add_plant",
                        new Dictionary<string, object?> { ["nickname"] = "Fern" })])],
                2 => [new ChatResponseUpdate(ChatRole.Assistant, [
                    new TextContent("Added Fern to your garden.")])],
                3 => [new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-2", "get_plants")])],
                4 => [new ChatResponseUpdate(ChatRole.Assistant, [
                    new TextContent("You have: Fern")])],
                _ => [new ChatResponseUpdate(ChatRole.Assistant, [
                    new TextContent("...")])],
            };
        });

        using var pipeline = new ChatClientBuilder(innerClient)
            .UseFunctionInvocation()
            .Build();

        var session = new ChatSession(tools, pipeline);

        await session.SendAsync("Add a fern plant");
        Assert.True(session.HasPendingApprovals);

        var pending = session.PendingApprovals.Single();
        var request = (ToolApprovalRequestContent)pending.Content;
        await session.SubmitApprovalAsync(request.CreateResponse(approved: true));

        Assert.False(session.HasPendingApprovals);
        Assert.Contains("Fern", plantsDb);

        await session.SendAsync("What plants do I have?");

        Assert.Contains(session.Messages,
            m => m.Content is TextContent { Text: "You have: Fern" });
    }
}
