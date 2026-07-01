// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Blocks;

public class ThinkingContentBlockTests
{
    [Fact]
    public void DefaultText_IsThinking()
    {
        Assert.Equal("Thinking…", new ThinkingContentBlock().Text);
    }

    [Fact]
    public void CustomText_IsPreserved()
    {
        Assert.Equal("Working…", new ThinkingContentBlock("Working…").Text);
    }
}

public class AgentContextStatusBlockTests
{
    [Fact]
    public async Task Streaming_ShowsThenRemovesThinkingBlock()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) => ResponseEmitters.EmitTextResponse("Hello", ct));

        var context = new AgentContext(new UIAgent(client));
        var sawThinking = false;
        context.RegisterOnBlockAdded((_, block) =>
        {
            if (block is ThinkingContentBlock)
                sawThinking = true;
        });

        await context.SendMessageAsync("Hi");

        // A thinking block was shown during streaming...
        Assert.True(sawThinking);
        // ...but is gone once content arrived.
        var turn = context.Turns[^1];
        Assert.DoesNotContain(turn.ResponseBlocks, b => b is ThinkingContentBlock);
        Assert.Contains(turn.ResponseBlocks, b => b is TextContentBlock);
    }

    [Fact]
    public async Task Streaming_RaisesBlockRemoved_ForThinking()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) => ResponseEmitters.EmitTextResponse("Hello", ct));

        var context = new AgentContext(new UIAgent(client));
        var removed = new List<ContentBlock>();
        context.RegisterOnBlockRemoved((_, block) => removed.Add(block));

        await context.SendMessageAsync("Hi");

        // The transient thinking block is removed via the block-removed callback,
        // not by mutating a "dismissed" flag the UI has to filter on.
        Assert.Contains(removed, b => b is ThinkingContentBlock);
    }

    [Fact]
    public async Task FailedTurn_AddsErrorContentBlock()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
            ResponseEmitters.EmitErrorAfterTokens([], new Exception("boom"), ct));

        var context = new AgentContext(new UIAgent(client));

        await context.SendMessageAsync("Hi");

        Assert.Equal(ConversationStatus.Error, context.Status);
        var turn = context.Turns[^1];
        var error = turn.ResponseBlocks.OfType<ErrorContentBlock>().Single();
        Assert.Contains("boom", error.Message);
        // The transient thinking block is cleaned up on failure.
        Assert.DoesNotContain(turn.ResponseBlocks, b => b is ThinkingContentBlock);
    }
}
