// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Specialized;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// An app loads a recorded or imported conversation, shows it, and continues it live.
/// </summary>
public class RestoreFromMessagesTests
{
    [Fact]
    public async Task RestoreAsync_Messages_RendersTurnsWithoutInvokingAnything()
    {
        var invocations = new List<string>();
        var client = new DelegatingStreamingChatClient();
        using var context = CreateContext(client, invocations);

        await context.RestoreAsync(CreateRecordedConversation());

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Empty(invocations);
        Assert.Collection(
            context.Turns,
            turn =>
            {
                Assert.Equal("user-1", turn.Id);
                Assert.Equal(2, turn.RequestBlocks.Count);
                Assert.Equal(
                    "Show me the weather",
                    Assert.Single(turn.RequestBlocks.OfType<TextContentBlock>()).RawText);
                Assert.Single(turn.RequestBlocks.OfType<MediaContentBlock>());
                Assert.All(turn.RequestBlocks, block => Assert.Equal(ChatRole.User, block.Role));
                Assert.Collection(
                    turn.ResponseBlocks,
                    block =>
                    {
                        var weather = Assert.IsType<FunctionInvocationContentBlock>(block);
                        Assert.Equal("call-weather", weather.Id);
                        Assert.Equal("Sunny", weather.Result?.Result);
                        Assert.Equal(BlockLifecycleState.Inactive, weather.LifecycleState);
                    },
                    block =>
                    {
                        var map = Assert.IsType<UIActionBlock>(block);
                        Assert.Equal("call-map", map.Id);
                        Assert.True(map.IsComplete);
                        Assert.Equal("Shown", map.Result?.Result);
                    },
                    block => Assert.Equal("It is sunny.", Assert.IsType<TextContentBlock>(block).RawText));
            },
            turn =>
            {
                Assert.Equal("Thanks", Assert.IsType<TextContentBlock>(Assert.Single(turn.RequestBlocks)).RawText);
                Assert.Equal(
                    "You're welcome.",
                    Assert.IsType<TextContentBlock>(Assert.Single(turn.ResponseBlocks)).RawText);
            });
        Assert.DoesNotContain(
            context.Turns.SelectMany(turn => turn.RequestBlocks.Concat(turn.ResponseBlocks)),
            block => block.Role == ChatRole.System);
    }

    [Fact]
    public async Task RestoreAsync_Messages_ThenSend_ContinuesWithTheWholeConversation()
    {
        var invocations = new List<string>();
        var requests = new List<List<ChatMessage>>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            requests.Add([.. messages]);
            return ResponseEmitters.EmitTextResponse("Cloudy tomorrow.", cancellationToken);
        });
        using var context = CreateContext(client, invocations);
        var recorded = CreateRecordedConversation();

        await context.RestoreAsync(recorded);
        await context.SendMessageAsync("And tomorrow?");

        var request = Assert.Single(requests);
        Assert.Equal(recorded.Count + 1, request.Count);
        for (var i = 0; i < recorded.Count; i++)
            Assert.Same(recorded[i], request[i]);
        Assert.Equal("And tomorrow?", request[^1].Text);
        Assert.Empty(invocations);
        Assert.Equal(3, context.Turns.Count);
        Assert.Equal(
            "Cloudy tomorrow.",
            Assert.IsType<TextContentBlock>(Assert.Single(context.Turns[^1].ResponseBlocks)).RawText);
    }

    [Fact]
    public async Task RestoreAsync_Messages_ShowsUnansweredCallButDoesNotSendIt()
    {
        var requests = new List<List<ChatMessage>>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            requests.Add([.. messages]);
            return ResponseEmitters.EmitTextResponse("Hello", cancellationToken);
        });
        using var context = new AgentContext(new UIAgent(client));

        await context.RestoreAsync(
        [
            new ChatMessage(ChatRole.User, "Delete it") { MessageId = "user-1" },
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "DeleteFile")]),
        ]);
        await context.SendMessageAsync("Hi");

        var call = Assert.IsType<FunctionInvocationContentBlock>(
            Assert.Single(context.Turns[0].ResponseBlocks));
        Assert.False(call.HasResult);
        Assert.Collection(
            Assert.Single(requests),
            message => Assert.Equal("Delete it", message.Text),
            message => Assert.Equal("Hi", message.Text));
    }

    [Fact]
    public async Task RestoreAsync_Messages_RecordedApproval_ShowsOneBlockAndIsNotExecutedAgain()
    {
        var deleted = new List<string>();
        var tool = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            (string fileName) =>
            {
                deleted.Add(fileName);
                return $"Deleted {fileName}";
            },
            "DeleteFile",
            "Deletes a file."));
        var call = new FunctionCallContent(
            "call-1",
            "DeleteFile",
            new Dictionary<string, object?> { ["fileName"] = "a.txt" });
        var request = new ToolApprovalRequestContent("approval-1", call);
        var inner = new DelegatingStreamingChatClient();
        inner.SetHandler((messages, options, cancellationToken) =>
            ResponseEmitters.EmitTextResponse("Anything else?", cancellationToken));
        using var context = new AgentContext(new UIAgent(
            new FunctionInvokingChatClient(inner),
            options => options.ChatOptions = new ChatOptions { Tools = [tool] }));

        await context.RestoreAsync(
        [
            new ChatMessage(ChatRole.User, "Delete a.txt") { MessageId = "user-1" },
            new ChatMessage(ChatRole.Assistant, [request]) { MessageId = "assistant-1" },
            new ChatMessage(ChatRole.User, [request.CreateResponse(true)]) { MessageId = "user-2" },
            new ChatMessage(
                ChatRole.Assistant,
                [new FunctionCallContent("call-1", "DeleteFile", call.Arguments) { InformationalOnly = true }])
            {
                MessageId = "assistant-2",
            },
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "Deleted a.txt")])
            {
                MessageId = "tool-1",
            },
            new ChatMessage(ChatRole.Assistant, "Done.") { MessageId = "assistant-3" },
        ]);

        var turn = Assert.Single(context.Turns);
        var approval = Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Equal("Deleted a.txt", approval.Result?.Result);
        Assert.DoesNotContain(turn.ResponseBlocks, block => block is FunctionInvocationContentBlock);
        Assert.Equal("Done.", Assert.Single(turn.ResponseBlocks.OfType<TextContentBlock>()).RawText);

        await context.SendMessageAsync("Thanks");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Empty(deleted);
        Assert.Equal(2, context.Turns.Count);
    }

    [Fact]
    public async Task RestoreAsync_Messages_LeadingAssistantMessage_FormsTurnWithoutRequest()
    {
        using var context = new AgentContext(new UIAgent(new DelegatingStreamingChatClient()));

        await context.RestoreAsync(
        [
            new ChatMessage(ChatRole.Assistant, "How can I help?"),
            new ChatMessage(ChatRole.User, "Hello"),
            new ChatMessage(ChatRole.Assistant, "Hi"),
        ]);

        Assert.Collection(
            context.Turns,
            turn =>
            {
                Assert.Empty(turn.RequestBlocks);
                Assert.Equal(
                    "How can I help?",
                    Assert.IsType<TextContentBlock>(Assert.Single(turn.ResponseBlocks)).RawText);
            },
            turn =>
            {
                Assert.Equal("Hello", Assert.IsType<TextContentBlock>(Assert.Single(turn.RequestBlocks)).RawText);
                Assert.Equal("Hi", Assert.IsType<TextContentBlock>(Assert.Single(turn.ResponseBlocks)).RawText);
            });
    }

    [Fact]
    public async Task RestoreAsync_Messages_ConsecutiveMessagesWithoutIds_StaySeparate()
    {
        using var agent = new UIAgent(new DelegatingStreamingChatClient());

        var blocks = await agent.RestoreAsync(
        [
            new ChatMessage(ChatRole.User, "Hello"),
            new ChatMessage(ChatRole.Assistant, "First"),
            new ChatMessage(ChatRole.Assistant, "Second"),
        ]);

        Assert.Equal(
            ["Hello", "First", "Second"],
            blocks.OfType<TextContentBlock>().Select(block => block.RawText));
    }

    [Fact]
    public async Task RestoreAsync_Messages_WithThread_Throws()
    {
        using var context = new AgentContext(new UIAgent(
            new DelegatingStreamingChatClient(),
            options => options.Thread = new InMemoryConversationThread("thread-1")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.RestoreAsync([new ChatMessage(ChatRole.User, "Hello")]));
        Assert.Empty(context.Turns);
    }

    [Fact]
    public async Task RestoreAsync_Messages_RaisesOneResetOnceAllTurnsAreLoaded()
    {
        var invocations = new List<string>();
        using var context = CreateContext(new DelegatingStreamingChatClient(), invocations);
        var raised = new List<(NotifyCollectionChangedAction Action, (string Id, int RequestBlocks, int ResponseBlocks)[] Turns)>();
        var blockAddedCount = 0;
        var statusChangedCount = 0;
        ChangeEvents.Observe(context.Turns).CollectionChanged += (_, e) => raised.Add(
            (e.Action, context.Turns.Select(turn => (turn.Id, turn.RequestBlocks.Count, turn.ResponseBlocks.Count)).ToArray()));
        using var blockRegistration = context.OnBlockAdded((_, _) => blockAddedCount++);
        using var statusRegistration = context.OnStatusChanged(_ => statusChangedCount++);

        await context.RestoreAsync(CreateRecordedConversation());

        var reset = Assert.Single(raised);
        Assert.Equal(NotifyCollectionChangedAction.Reset, reset.Action);
        Assert.Equal(
            context.Turns.Select(turn => (turn.Id, turn.RequestBlocks.Count, turn.ResponseBlocks.Count)),
            reset.Turns);
        Assert.Equal(2, reset.Turns.Length);
        Assert.Equal((2, 3), (reset.Turns[0].RequestBlocks, reset.Turns[0].ResponseBlocks));
        Assert.Equal(0, blockAddedCount);
        Assert.Equal(0, statusChangedCount);
        Assert.Empty(invocations);
    }

    [Fact]
    public async Task RestoreAsync_Messages_RequiresAnEmptyContext()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
            ResponseEmitters.EmitTextResponse("Hello", cancellationToken));
        using var context = new AgentContext(new UIAgent(client));
        await context.SendMessageAsync("Hi");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.RestoreAsync([new ChatMessage(ChatRole.User, "Hello")]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => context.RestoreAsync(null!));
    }

    [Fact]
    public async Task UIAgentRestoreAsync_Messages_ReplacesTheHistory()
    {
        var requests = new List<List<ChatMessage>>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            requests.Add([.. messages]);
            return ResponseEmitters.EmitTextResponse("Reply", cancellationToken);
        });
        using var agent = new UIAgent(client);
        await foreach (var _ in agent.SendMessageAsync(new ChatMessage(ChatRole.User, "Before")))
        {
        }

        await agent.RestoreAsync([new ChatMessage(ChatRole.User, "Imported")]);
        await foreach (var _ in agent.SendMessageAsync(new ChatMessage(ChatRole.User, "After")))
        {
        }

        Assert.Equal(["Imported", "After"], requests[^1].Select(message => message.Text));
    }

    private static List<ChatMessage> CreateRecordedConversation() =>
    [
        new ChatMessage(ChatRole.System, "You are a weather assistant."),
        new ChatMessage(
            ChatRole.User,
            [
                new TextContent("Show me the weather"),
                new DataContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png"),
            ])
        {
            MessageId = "user-1",
        },
        new ChatMessage(
            ChatRole.Assistant,
            [
                new FunctionCallContent(
                    "call-weather",
                    "GetWeather",
                    new Dictionary<string, object?> { ["city"] = "Seattle" })
                {
                    InformationalOnly = true,
                },
            ]),
        new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-weather", "Sunny")]),
        new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-map", "ShowMap")]),
        new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-map", "Shown")]),
        new ChatMessage(ChatRole.Assistant, "It is sunny."),
        new ChatMessage(ChatRole.User, "Thanks"),
        new ChatMessage(ChatRole.Assistant, "You're welcome."),
    ];

    private static AgentContext CreateContext(DelegatingStreamingChatClient client, List<string> invocations)
    {
        var getWeather = AIFunctionFactory.Create(
            (string city) =>
            {
                invocations.Add("GetWeather");
                return "Rainy";
            },
            "GetWeather",
            "Gets the weather.");
        var showMap = AIFunctionFactory.Create(
            () =>
            {
                invocations.Add("ShowMap");
                return "Shown";
            },
            "ShowMap",
            "Shows a map.");
        return new AgentContext(new UIAgent(
            new FunctionInvokingChatClient(client),
            options =>
            {
                options.ChatOptions = new ChatOptions { Tools = [getWeather] };
                options.RegisterUIAction(showMap);
            }));
    }
}
