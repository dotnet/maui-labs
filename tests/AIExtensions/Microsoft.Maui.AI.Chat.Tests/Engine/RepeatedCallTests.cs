// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

public class RepeatedCallTests
{
    [Fact]
    public async Task CompleteResponseWithRepeatedToolContent_ShowsOneCallWithItsResult()
    {
        var call = new FunctionCallContent("call", "calculate");
        var result = new FunctionResultContent("call", 4);
        var response = new ChatResponse(
        [
            new ChatMessage(ChatRole.Assistant, [new TextContent("Before"), call, call]),
            new ChatMessage(ChatRole.Tool, [result, result]),
            new ChatMessage(ChatRole.Assistant, [call, new TextContent("After")]),
        ]);
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitUpdates(response.ToChatResponseUpdates(), ct));
        using var context = new AgentContext(new UIAgent(model));

        await context.SendMessageAsync("Question");

        var blocks = Assert.Single(context.Turns).ResponseBlocks;
        Assert.True(Assert.Single(blocks.OfType<FunctionInvocationContentBlock>()).HasResult);
        Assert.Equal(["Before", "After"], blocks.OfType<TextContentBlock>().Select(block => block.RawText));
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }
}
