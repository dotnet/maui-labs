// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

public class UIActionTests
{
    [Fact]
    public async Task UIAction_AutoInvokesWithoutAwaitingInput_AndResumesModel()
    {
        var invocationCount = 0;
        var action = AIFunctionFactory.Create(
            () =>
            {
                invocationCount++;
                return "Seattle, WA";
            },
            "GetLocation",
            "Gets the current location");
        var client = new DelegatingStreamingChatClient();
        IReadOnlyList<ChatMessage>? continuationMessages = null;
        var callCount = 0;
        client.SetHandler((messages, options, cancellationToken) =>
        {
            callCount++;
            if (callCount == 1)
                return EmitUIActionCall("location-1", "GetLocation", cancellationToken);

            continuationMessages = messages.ToArray();
            return ResponseEmitters.EmitTextResponse("You are in Seattle.", cancellationToken);
        });
        var agent = new UIAgent(client, options => options.RegisterUIAction(action));
        var context = new AgentContext(agent);
        var statuses = new List<ConversationStatus>();
        context.RegisterOnStatusChanged(status => statuses.Add(status));

        await context.SendMessageAsync("Where am I?");

        Assert.Equal(1, invocationCount);
        Assert.DoesNotContain(ConversationStatus.AwaitingInput, statuses);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        var actionBlock = Assert.Single(context.Turns[0].ResponseBlocks.OfType<UIActionBlock>());
        Assert.True(actionBlock.IsComplete);
        Assert.Equal("GetLocation", actionBlock.ToolName);

        Assert.NotNull(continuationMessages);
        var result = Assert.Single(
            continuationMessages!
                .Last(message => message.Role == ChatRole.Tool)
                .Contents
                .OfType<FunctionResultContent>());
        Assert.Equal("location-1", result.CallId);
        Assert.Equal("Seattle, WA", result.Result?.ToString());
    }

    [Fact]
    public async Task UIAction_WithArguments_InvokesOnceAndPreservesBlockArguments()
    {
        var invocationCount = 0;
        var action = AIFunctionFactory.Create(
            (string product, int quantity) =>
            {
                invocationCount++;
                return $"Added {quantity}x {product}";
            },
            "AddToCart",
            "Adds an item");
        var client = CreateTwoRoundClient(
            EmitUIActionCall(
                "cart-1",
                "AddToCart",
                arguments: new Dictionary<string, object?>
                {
                    ["product"] = "Trowel",
                    ["quantity"] = 2,
                }));
        var context = new AgentContext(
            new UIAgent(client, options => options.RegisterUIAction(action)));

        await context.SendMessageAsync("Add it");

        var block = Assert.Single(context.Turns[0].ResponseBlocks.OfType<UIActionBlock>());
        Assert.Equal(1, invocationCount);
        Assert.Equal("Trowel", block.Arguments!["product"]);
        Assert.Equal(2, block.Arguments["quantity"]);

        var firstResult = await block.InvokeAsync();
        var secondResult = await block.InvokeAsync();
        Assert.Same(firstResult, secondResult);
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public async Task UIAction_DeclarationIsAddedWithoutMutatingConfiguredOptions()
    {
        var action = AIFunctionFactory.Create(() => "ok", "ClientTool", "Client tool");
        var backend = AIFunctionFactory.Create(() => "server", "ServerTool", "Server tool");
        var configured = new ChatOptions { Tools = [backend] };
        ChatOptions? captured = null;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            captured = options;
            return ResponseEmitters.EmitTextResponse("Done", cancellationToken);
        });
        var context = new AgentContext(new UIAgent(client, options =>
        {
            options.ChatOptions = configured;
            options.RegisterUIAction(action);
        }));

        await context.SendMessageAsync("Go");

        Assert.NotNull(captured);
        Assert.NotSame(configured, captured);
        Assert.Single(configured.Tools!);
        Assert.Equal(2, captured.Tools!.Count);
        Assert.Contains(captured.Tools, tool => tool.Name == "ServerTool" && tool is AIFunction);
        Assert.Contains(
            captured.Tools,
            tool => tool.Name == "ClientTool"
                && tool is AIFunctionDeclaration
                && tool is not AIFunction);
    }

    [Fact]
    public async Task UIAction_ReceivesConfiguredServices()
    {
        var services = new TestServiceProvider();
        var action = new CapturingFunction();
        var client = CreateTwoRoundClient(EmitUIActionCall("capture-1", action.Name));
        var context = new AgentContext(new UIAgent(client, options =>
        {
            options.Services = services;
            options.RegisterUIAction(action);
        }));

        await context.SendMessageAsync("Capture services");

        Assert.Same(services, action.CapturedServices);
    }

    [Fact]
    public async Task UIAction_NameConflictsWithBackendTool_FailsFast()
    {
        var action = AIFunctionFactory.Create(() => "client", "Duplicate", "Client");
        var backend = AIFunctionFactory.Create(() => "server", "Duplicate", "Server");
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
            ResponseEmitters.EmitTextResponse("unused", cancellationToken));
        var agent = new UIAgent(client, options =>
        {
            options.ChatOptions = new ChatOptions { Tools = [backend] };
            options.RegisterUIAction(action);
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => EnumerateAsync(agent.SendMessageAsync(
                new ChatMessage(ChatRole.User, "Go"))));

        Assert.Contains("conflicts", exception.Message);
    }

    [Fact]
    public void RegisterUIAction_DuplicateName_Throws()
    {
        var options = new UIAgentOptions();
        options.RegisterUIAction(AIFunctionFactory.Create(() => "a", "Same", "First"));

        Assert.Throws<ArgumentException>(() =>
            options.RegisterUIAction(AIFunctionFactory.Create(() => "b", "Same", "Second")));
    }

    [Fact]
    public async Task UIAction_EmptyCallId_AssignsFallbackId()
    {
        var action = AIFunctionFactory.Create(() => "ok", "ClientTool", "Client tool");
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
            EmitUIActionCall(string.Empty, "ClientTool", cancellationToken));
        var agent = new UIAgent(client, options => options.RegisterUIAction(action));

        var blocks = new List<ContentBlock>();
        await foreach (var item in agent.SendMessageAsync(
            new ChatMessage(ChatRole.User, "Go")))
        {
            blocks.Add(item);
        }

        var block = Assert.Single(blocks.OfType<UIActionBlock>());
        Assert.False(string.IsNullOrWhiteSpace(block.Id));
    }

    [Fact]
    public async Task MixedBackendAndUIActions_BothResultsResumeInOneToolMessage()
    {
        var backend = AIFunctionFactory.Create(
            (string city) => $"Sunny in {city}",
            "GetWeather",
            "Gets weather");
        var uiAction = AIFunctionFactory.Create(
            () => "Portland",
            "GetLocation",
            "Gets location");
        IReadOnlyList<ChatMessage>? continuationMessages = null;
        var callCount = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return ResponseEmitters.EmitMultipleToolCallResponse(
                    cancellationToken,
                    new FunctionCallContent(
                        "weather-1",
                        "GetWeather",
                        new Dictionary<string, object?> { ["city"] = "Portland" }),
                    new FunctionCallContent("location-1", "GetLocation"));
            }

            continuationMessages = messages.ToArray();
            return ResponseEmitters.EmitTextResponse("Done", cancellationToken);
        });
        var context = new AgentContext(new UIAgent(client, options =>
        {
            options.ChatOptions = new ChatOptions { Tools = [backend] };
            options.RegisterUIAction(uiAction);
        }));

        await context.SendMessageAsync("Weather here?");

        var turn = Assert.Single(context.Turns);
        Assert.True(Assert.Single(turn.ResponseBlocks.OfType<UIActionBlock>()).HasResult);
        Assert.True(Assert.Single(
            turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>()).HasResult);
        var toolMessage = continuationMessages!.Last(message => message.Role == ChatRole.Tool);
        var results = toolMessage.Contents.OfType<FunctionResultContent>().ToArray();
        Assert.Equal(2, results.Length);
        Assert.Contains(results, result => result.CallId == "weather-1");
        Assert.Contains(results, result => result.CallId == "location-1");
    }

    [Fact]
    public async Task RestoreAsync_UIActionIsDisplayOnly()
    {
        var thread = new InMemoryConversationThread("thread-1");
        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "Locate me"));
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-1",
            Contents = [new FunctionCallContent("location-1", "GetLocation")],
        });
        thread.CompleteTurn();
        var invocationCount = 0;
        var action = AIFunctionFactory.Create(
            () =>
            {
                invocationCount++;
                return "Seattle";
            },
            "GetLocation",
            "Gets location");
        var client = new DelegatingStreamingChatClient();
        var context = new AgentContext(new UIAgent(client, options =>
        {
            options.Thread = thread;
            options.RegisterUIAction(action);
        }));

        await context.RestoreAsync();

        var block = Assert.Single(context.Turns[0].ResponseBlocks.OfType<UIActionBlock>());
        Assert.False(block.IsComplete);
        Assert.Equal(0, invocationCount);
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    [Fact]
    public async Task RestoreAsync_CompletedUIActionRestoresPersistedResult()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var action = AIFunctionFactory.Create(
            () => "Seattle",
            "GetLocation",
            "Gets location");
        var sendClient = CreateTwoRoundClient(
            EmitUIActionCall("location-1", "GetLocation"));
        var sendContext = new AgentContext(new UIAgent(sendClient, options =>
        {
            options.Thread = thread;
            options.RegisterUIAction(action);
        }));
        await sendContext.SendMessageAsync("Locate me");

        var restoreClient = new DelegatingStreamingChatClient();
        var invocationCount = 0;
        var restoreAction = AIFunctionFactory.Create(
            () =>
            {
                invocationCount++;
                return "should not run";
            },
            "GetLocation",
            "Gets location");
        var restoreContext = new AgentContext(new UIAgent(restoreClient, options =>
        {
            options.Thread = thread;
            options.RegisterUIAction(restoreAction);
        }));

        await restoreContext.RestoreAsync();

        var block = Assert.Single(
            restoreContext.Turns[0].ResponseBlocks.OfType<UIActionBlock>());
        Assert.True(block.IsComplete);
        Assert.Equal("Seattle", block.Result!.Result?.ToString());
        Assert.Equal(0, invocationCount);
    }

    private static DelegatingStreamingChatClient CreateTwoRoundClient(
        IAsyncEnumerable<ChatResponseUpdate> firstResponse)
    {
        var client = new DelegatingStreamingChatClient();
        var callCount = 0;
        client.SetHandler((messages, options, cancellationToken) =>
        {
            callCount++;
            return callCount == 1
                ? firstResponse
                : ResponseEmitters.EmitTextResponse("Done", cancellationToken);
        });
        return client;
    }

    private static async Task EnumerateAsync(IAsyncEnumerable<ContentBlock> blocks)
    {
        await foreach (var _ in blocks)
        {
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitUIActionCall(
        string callId,
        string name,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        IDictionary<string, object?>? arguments = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-action",
            FinishReason = ChatFinishReason.ToolCalls,
            Contents = [new FunctionCallContent(callId, name, arguments)],
        };
        await Task.CompletedTask;
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class CapturingFunction : AIFunction
    {
        private static readonly JsonElement EmptySchema =
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { },
                additionalProperties = false,
            });

        public override string Name => "CaptureServices";
        public override string Description => "Captures the configured service provider";
        public override JsonElement JsonSchema => EmptySchema;
        public override JsonElement? ReturnJsonSchema => null;
        internal IServiceProvider? CapturedServices { get; private set; }

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            CapturedServices = arguments.Services;
            return ValueTask.FromResult<object?>("captured");
        }
    }
}
