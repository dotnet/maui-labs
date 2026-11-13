// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// Each reasoning item, such as one reasoning summary, gets its own block; the history keeps each item's
/// protected data.
/// </summary>
public class ReasoningItemTests
{
    [Fact]
    public async Task ProtectedReasoningItems_StaySeparateOnScreenAndInTheHistory()
    {
        const string first = "**First heading**\n\nFirst body";
        const string second = "**Second heading**\n\nSecond body";
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitUpdates(
        [
            Reasoning(first),
            Reasoning(string.Empty, "completed-first-item"),
            Reasoning(second),
            Reasoning(string.Empty, "completed-second-item"),
        ], ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Next", ct));
        using var context = new AgentContext(new UIAgent(model));

        await context.SendMessageAsync("Question");
        await context.SendMessageAsync("Again");

        var blocks = context.Turns[0].ResponseBlocks.Cast<ReasoningContentBlock>().ToList();
        Assert.Equal([first, second], blocks.Select(block => block.Text));
        Assert.Equal(["completed-first-item", "completed-second-item"], blocks.Select(block => block.ProtectedData));
        Assert.Equal(
            [(first, "completed-first-item"), (second, "completed-second-item")],
            model.Requests[1]
                .SelectMany(message => message.Contents)
                .OfType<TextReasoningContent>()
                .Select(content => (content.Text, content.ProtectedData)));
    }

    [Fact]
    public async Task ReasoningFromDifferentMessages_StaysSeparate()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitUpdates(
        [
            Reasoning("First ", messageId: "first"),
            Reasoning("thought", messageId: "first"),
            Reasoning("Second thought", messageId: "second"),
            new ChatResponseUpdate { MessageId = "third" },
        ], ct));
        using var context = new AgentContext(new UIAgent(model));

        await context.SendMessageAsync("Question");

        var blocks = Assert.Single(context.Turns).ResponseBlocks.Cast<ReasoningContentBlock>().ToList();
        Assert.Equal(["First thought", "Second thought"], blocks.Select(block => block.Text));
        Assert.All(blocks, block => Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState));
    }

    private static ChatResponseUpdate Reasoning(string text, string? protectedData = null, string? messageId = null)
        => new(ChatRole.Assistant, [new TextReasoningContent(text) { ProtectedData = protectedData }])
        {
            MessageId = messageId,
        };
}
