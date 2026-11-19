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

    [Fact]
    public async Task LiveReasoning_RecordsStartedAtCompletedAtAndDurationWhileRestoredReasoningLeavesThemNull()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var thread = new InMemoryConversationThread("thread-1");
        var model = new ScriptedChatClient();
        model.Enqueue(ct => StreamTimedReasoning(clock, ct));
        using (var live = new AgentContext(new UIAgent(model, options =>
        {
            options.TimeProvider = clock;
            options.Thread = thread;
        })))
        {
            await live.SendMessageAsync("Question");

            var block = Assert.Single(Assert.Single(live.Turns).ResponseBlocks.OfType<ReasoningContentBlock>());
            Assert.Equal(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero), block.StartedAt);
            Assert.Equal(new DateTimeOffset(2026, 11, 18, 12, 0, 4, TimeSpan.Zero), block.CompletedAt);
            Assert.Equal(TimeSpan.FromSeconds(4), block.Duration);
        }

        using var restored = new AgentContext(new UIAgent(model, options =>
        {
            options.TimeProvider = clock;
            options.Thread = thread;
        }));
        await restored.RestoreAsync();

        var restoredBlock = Assert.Single(Assert.Single(restored.Turns).ResponseBlocks.OfType<ReasoningContentBlock>());
        Assert.Equal("Step 1 Step 2", restoredBlock.Text);
        Assert.Null(restoredBlock.StartedAt);
        Assert.Null(restoredBlock.CompletedAt);
        Assert.Null(restoredBlock.Duration);
    }

    [Fact]
    public void TimeProvider_Null_Throws()
    {
        var options = new UIAgentOptions();
        Assert.Throws<ArgumentNullException>(() => options.TimeProvider = null!);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamTimedReasoning(
        ManualTimeProvider clock,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return Reasoning("Step 1 ", messageId: "msg-1");
        clock.Advance(TimeSpan.FromSeconds(2));
        yield return Reasoning("Step 2", messageId: "msg-1");
        clock.Advance(TimeSpan.FromSeconds(2));
        yield return new ChatResponseUpdate(ChatRole.Assistant, "Answer") { MessageId = "msg-1" };
        await Task.CompletedTask;
    }

    private static ChatResponseUpdate Reasoning(string text, string? protectedData = null, string? messageId = null)
        => new(ChatRole.Assistant, [new TextReasoningContent(text) { ProtectedData = protectedData }])
        {
            MessageId = messageId,
        };

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
