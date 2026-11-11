// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// An app changes tools, tool mode, response format or model between sends.
/// </summary>
public class ChatOptionsPerSendTests
{
    [Fact]
    public async Task ChangingChatOptionsBetweenSends_AppliesToTheNextSendAndKeepsHistory()
    {
        var search = AIFunctionFactory.Create(() => "found", "Search", "Searches.").AsDeclarationOnly();
        var chatOptions = new ChatOptions
        {
            ModelId = "model-a",
            Tools = [search],
            ToolMode = ChatToolMode.Auto,
        };
        var requests = new List<(List<ChatMessage> Messages, ChatOptions? Options)>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            requests.Add(([.. messages], options?.Clone()));
            return ResponseEmitters.EmitTextResponse($"Reply {requests.Count}", cancellationToken);
        });
        using var context = new AgentContext(new UIAgent(client, chatOptions));

        await context.SendMessageAsync("First");
        chatOptions.ModelId = "model-b";
        chatOptions.Tools = null;
        chatOptions.ToolMode = ChatToolMode.None;
        chatOptions.ResponseFormat = ChatResponseFormat.Json;
        await context.SendMessageAsync("Second");

        Assert.Equal(2, requests.Count);
        var first = requests[0].Options;
        Assert.Equal("model-a", first?.ModelId);
        Assert.Equal("Search", Assert.Single(first?.Tools ?? []).Name);
        Assert.Same(ChatToolMode.Auto, first?.ToolMode);
        Assert.Null(first?.ResponseFormat);

        var second = requests[1].Options;
        Assert.Equal("model-b", second?.ModelId);
        Assert.Null(second?.Tools);
        Assert.Same(ChatToolMode.None, second?.ToolMode);
        Assert.IsType<ChatResponseFormatJson>(second?.ResponseFormat);
        Assert.Equal(
            ["First", "Reply 1", "Second"],
            requests[1].Messages.Select(message => message.Text));
    }

    [Fact]
    public async Task ChangingChatOptionsDuringASend_AppliesFromTheNextRequest()
    {
        var chatOptions = new ChatOptions { ModelId = "model-a" };
        var models = new List<string?>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            models.Add(options?.ModelId);
            if (models.Count == 1)
            {
                // The app changes the model while the first turn is still running.
                chatOptions.ModelId = "model-b";
                return EmitUIActionCallAsync(cancellationToken);
            }

            return ResponseEmitters.EmitTextResponse("Done", cancellationToken);
        });
        using var context = new AgentContext(new UIAgent(
            client,
            options =>
            {
                options.ChatOptions = chatOptions;
                options.RegisterUIAction(AIFunctionFactory.Create(() => "shown", "ShowMap", "Shows a map."));
            }));

        await context.SendMessageAsync("First");
        await context.SendMessageAsync("Second");

        // The UI action's continuation round is the next request.
        Assert.Equal(["model-a", "model-b", "model-b"], models);
    }

    [Fact]
    public async Task ConfigureOptionsMiddleware_ComputesOptionsForEveryRequest()
    {
        var model = "model-a";
        var models = new List<string?>();
        var inner = new DelegatingStreamingChatClient();
        inner.SetHandler((messages, options, cancellationToken) =>
        {
            models.Add(options?.ModelId);
            return ResponseEmitters.EmitTextResponse("Reply", cancellationToken);
        });
        var client = inner.AsBuilder()
            .ConfigureOptions(options => options.ModelId = model)
            .Build();
        using var context = new AgentContext(new UIAgent(client));

        await context.SendMessageAsync("First");
        model = "model-b";
        await context.SendMessageAsync("Second");

        Assert.Equal(["model-a", "model-b"], models);
    }

    [Fact]
    public async Task TurningInvocableToolsOnWithoutFunctionInvocation_FailsTheNextSend()
    {
        var chatOptions = new ChatOptions();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
            ResponseEmitters.EmitTextResponse("Reply", cancellationToken));
        using var context = new AgentContext(new UIAgent(client, chatOptions));
        await context.SendMessageAsync("First");

        chatOptions.Tools = [AIFunctionFactory.Create(() => "found", "Search", "Searches.")];

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SendMessageAsync("Second"));
        Assert.Single(context.Turns);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitUIActionCallAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-1",
            Contents = [new FunctionCallContent("call-1", "ShowMap")],
        };
        await Task.CompletedTask;
    }
}
