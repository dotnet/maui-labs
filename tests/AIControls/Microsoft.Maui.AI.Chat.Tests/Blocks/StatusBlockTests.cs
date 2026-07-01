// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Blocks;

/// <summary>
/// The engine never fabricates transient "status" blocks (thinking / error). Failures and progress are
/// surfaced via <see cref="ConversationStatus"/> and <see cref="AgentContext.Error"/> only, so the turns
/// stay a clean projection of the real message thread. Rendering those as UI items is the job of the
/// Controls layer's MessageListView.
/// </summary>
public class AgentContextStatusBlockTests
{
    [Fact]
    public async Task Streaming_EmitsOnlyRealContentBlocks()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) => ResponseEmitters.EmitTextResponse("Hello", ct));

        var context = new AgentContext(new UIAgent(client));
        var added = new List<ContentBlock>();
        context.RegisterOnBlockAdded((_, block) => added.Add(block));

        await context.SendMessageAsync("Hi");

        // Only real content is emitted — no transient "thinking" placeholder block.
        Assert.NotEmpty(added);
        Assert.All(added, b => Assert.IsType<TextContentBlock>(b));

        var turn = context.Turns[^1];
        Assert.Contains(turn.ResponseBlocks, b => b is TextContentBlock);
    }

    [Fact]
    public async Task FailedTurn_SetsErrorStatus_WithoutAddingBlocks()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
            ResponseEmitters.EmitErrorAfterTokens([], new Exception("boom"), ct));

        var context = new AgentContext(new UIAgent(client));
        var added = new List<ContentBlock>();
        context.RegisterOnBlockAdded((_, block) => added.Add(block));

        await context.SendMessageAsync("Hi");

        // The failure is surfaced via status + Error, not as a block in the turn.
        Assert.Equal(ConversationStatus.Error, context.Status);
        Assert.NotNull(context.Error);
        Assert.Contains("boom", context.Error!.Message);

        var turn = context.Turns[^1];
        Assert.Empty(turn.ResponseBlocks);
        // Only the user's request block was emitted — no error/thinking block.
        Assert.All(added, b => Assert.IsType<TextContentBlock>(b));
    }
}
