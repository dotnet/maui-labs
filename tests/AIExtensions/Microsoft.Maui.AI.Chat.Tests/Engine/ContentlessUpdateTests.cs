// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>An update without contents, such as one that only reports metadata, does not end a message.</summary>
public class ContentlessUpdateTests
{
    [Fact]
    public async Task MetadataOnlyUpdateInTheMiddleOfAMessage_KeepsOneBlockAndOneHistoryMessage()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitUpdates(
        [
            new ChatResponseUpdate(ChatRole.Assistant, "First ") { MessageId = "message", ModelId = "actual-model" },
            new ChatResponseUpdate { MessageId = "message", ModelId = "actual-model" },
            new ChatResponseUpdate(ChatRole.Assistant, "second") { MessageId = "message", ModelId = "actual-model" },
        ], ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Next", ct));
        using var context = new AgentContext(new UIAgent(model));

        await context.SendMessageAsync("Question");
        await context.SendMessageAsync("Again");

        var turn = context.Turns[0];
        Assert.Equal("First second", Assert.IsType<TextContentBlock>(Assert.Single(turn.ResponseBlocks)).RawText);
        Assert.Equal("actual-model", turn.ModelId);
        Assert.Equal(
            "First second",
            Assert.Single(model.Requests[1], message => message.Role == ChatRole.Assistant).Text);
    }

    [Fact]
    public async Task ReasoningSummaryWithItsEncryptedContentAfterEmptyUpdates_ShowsOneBlock()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitUpdates(
        [
            new ChatResponseUpdate(null, [new TextReasoningContent("**Planning**\n\n")]),
            new ChatResponseUpdate(null, [new TextReasoningContent("Multiply the numbers.")]),
            new ChatResponseUpdate(),
            new ChatResponseUpdate(),
            new ChatResponseUpdate(null, [new TextReasoningContent(string.Empty) { ProtectedData = "encrypted" }]),
            new ChatResponseUpdate(ChatRole.Assistant, "It is 84.") { MessageId = "message" },
        ], ct));
        using var context = new AgentContext(new UIAgent(model));

        await context.SendMessageAsync("What is 12 × 7?");

        var blocks = Assert.Single(context.Turns).ResponseBlocks;
        var reasoning = Assert.IsType<ReasoningContentBlock>(blocks[0]);
        Assert.Equal("**Planning**\n\nMultiply the numbers.", reasoning.Text);
        Assert.Equal("encrypted", reasoning.ProtectedData);
        Assert.False(reasoning.IsEncrypted);
        Assert.Equal("It is 84.", Assert.IsType<TextContentBlock>(blocks[1]).RawText);
        Assert.Equal(2, blocks.Count);
    }
}
