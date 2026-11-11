// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.AI.Chat.Tests.TestHelpers;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Samples;

/// <summary>
/// Server tools are invoked by <see cref="FunctionInvokingChatClient"/>; the engine only renders
/// the calls and their results.
/// </summary>
public class S02_BackendToolsTest
{
    [Fact]
    public async Task BackendTool_InvokedByFunctionInvocation_ProducesInvocationBlock()
    {
        var searchCalled = false;
        var searchFunction = AIFunctionFactory.Create(
            (string location, string cuisine) =>
            {
                searchCalled = true;
                return new { Name = "Pizza Place", Rating = 4.5 };
            },
            "SearchRestaurants",
            "Search for restaurants");

        var callCount = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return ResponseEmitters.EmitToolCallResponse(
                    "call-1", "SearchRestaurants",
                    new Dictionary<string, object?> { ["location"] = "NYC", ["cuisine"] = "Italian" });
            }
            return ResponseEmitters.EmitTextResponse("I found Pizza Place rated 4.5!");
        });

        var chatOptions = new ChatOptions { Tools = [searchFunction] };
        var agent = new UIAgent(new FunctionInvokingChatClient(client), chatOptions);
        var context = new AgentContext(agent);

        await context.SendMessageAsync("Find Italian restaurants in NYC");

        Assert.True(searchCalled);
        Assert.Equal(2, callCount);

        var turn = context.Turns[0];
        var invocationBlock = turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>().Single();
        Assert.Equal("SearchRestaurants", invocationBlock.ToolName);
        Assert.True(invocationBlock.HasResult);

        var textBlock = turn.ResponseBlocks.OfType<TextContentBlock>().Single();
        Assert.Contains("Pizza Place", textBlock.RawText);
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    [Fact]
    public async Task BackendTool_MultipleToolCalls_AllExecuted()
    {
        var callLog = new List<string>();

        var getWeather = AIFunctionFactory.Create(
            (string city) =>
            {
                callLog.Add($"weather:{city}");
                return $"{city}: 72°F";
            },
            "GetWeather",
            "Get weather for a city");

        var getTime = AIFunctionFactory.Create(
            (string city) =>
            {
                callLog.Add($"time:{city}");
                return $"{city}: 2:30 PM";
            },
            "GetTime",
            "Get time for a city");

        var callCount = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return ResponseEmitters.EmitMultipleToolCallResponse(ct,
                    new FunctionCallContent("c1", "GetWeather",
                        new Dictionary<string, object?> { ["city"] = "London" }),
                    new FunctionCallContent("c2", "GetTime",
                        new Dictionary<string, object?> { ["city"] = "London" }));
            }
            return ResponseEmitters.EmitTextResponse("London: 72°F at 2:30 PM");
        });

        var chatOptions = new ChatOptions { Tools = [getWeather, getTime] };
        var agent = new UIAgent(new FunctionInvokingChatClient(client), chatOptions);
        var context = new AgentContext(agent);

        await context.SendMessageAsync("Weather and time in London?");

        Assert.Equal(2, callLog.Count);
        Assert.Contains("weather:London", callLog);
        Assert.Contains("time:London", callLog);

        var turn = context.Turns[0];
        var invocations = turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>().ToList();
        Assert.Equal(["c1", "c2"], invocations.Select(block => block.Id));
        Assert.All(invocations, b => Assert.True(b.HasResult));
    }

    [Fact]
    public async Task BackendTool_StatusRemainsStreaming_DuringInvocation()
    {
        var statuses = new List<ConversationStatus>();

        var tool = AIFunctionFactory.Create(() => "result", "DoWork", "Does work");
        var callCount = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return ResponseEmitters.EmitToolCallResponse("c1", "DoWork");
            }
            return ResponseEmitters.EmitTextResponse("Done");
        });

        var chatOptions = new ChatOptions { Tools = [tool] };
        var agent = new UIAgent(client.AsBuilder().UseFunctionInvocation().Build(), chatOptions);
        var context = new AgentContext(agent);
        context.OnStatusChanged(s => statuses.Add(s));

        await context.SendMessageAsync("Do it");

        Assert.Equal([ConversationStatus.Streaming, ConversationStatus.Idle], statuses);
    }

    [Fact]
    public async Task BackendTool_Failure_ReturnsErrorResultToModel()
    {
        var tool = AIFunctionFactory.Create(
            (Func<string>)(() => throw new InvalidOperationException("tool failed")),
            "Explode");
        var sentMessages = new List<ChatMessage[]>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            sentMessages.Add(messages.ToArray());
            return sentMessages.Count == 1
                ? ResponseEmitters.EmitToolCallResponse("call-1", "Explode", ct: cancellationToken)
                : ResponseEmitters.EmitTextResponse("recovered", cancellationToken);
        });
        var context = new AgentContext(new UIAgent(
            new FunctionInvokingChatClient(client),
            new ChatOptions { Tools = [tool] }));

        await context.SendMessageAsync("Run it");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        var block = Assert.Single(
            context.Turns[0].ResponseBlocks.OfType<FunctionInvocationContentBlock>());
        Assert.Equal("Error: Function failed.", block.Result?.Result);
        var errorResult = Assert.Single(
            sentMessages[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Equal("call-1", errorResult.CallId);
    }

    [Fact]
    public async Task AgentContext_InvocableToolWithoutFunctionInvocation_FailsBeforeStartingTurn()
    {
        var tool = AIFunctionFactory.Create(() => "result", "DoWork", "Does work");
        var callCount = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            callCount++;
            return ResponseEmitters.EmitTextResponse("unused", cancellationToken);
        });
        var context = new AgentContext(new UIAgent(client, new ChatOptions { Tools = [tool] }));
        var statuses = new List<ConversationStatus>();
        context.OnStatusChanged(statuses.Add);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SendMessageAsync("Do it"));

        Assert.Contains("DoWork", exception.Message);
        Assert.Contains("UseFunctionInvocation", exception.Message);
        Assert.Empty(context.Turns);
        Assert.Empty(statuses);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task UIAgent_InvocableToolWithoutFunctionInvocation_Fails()
    {
        var tool = AIFunctionFactory.Create(() => "result", "DoWork", "Does work");
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
            ResponseEmitters.EmitTextResponse("unused", cancellationToken));
        var agent = new UIAgent(client, new ChatOptions { Tools = [tool] });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var block in agent.SendMessageAsync(new ChatMessage(ChatRole.User, "Do it")))
            {
                _ = block;
            }
        });

        Assert.Contains("UseFunctionInvocation", exception.Message);
    }

    [Fact]
    public async Task DeclarationOnlyTool_DoesNotRequireFunctionInvocation()
    {
        var declaration = AIFunctionFactory.Create(() => "result", "DoWork", "Does work")
            .AsDeclarationOnly();
        ChatOptions? captured = null;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            captured = options;
            return ResponseEmitters.EmitTextResponse("Done", cancellationToken);
        });
        var context = new AgentContext(new UIAgent(
            client,
            new ChatOptions { Tools = [declaration] }));

        await context.SendMessageAsync("Hello");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Same(declaration, Assert.Single(captured!.Tools!));
    }
}
