// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// A complete (non-streaming) response, or a restored conversation, delivers each message as one update;
/// its blocks still follow the order of the message's contents.
/// </summary>
public class ContentOrderTests
{
    [Fact]
    public async Task CompleteResponse_ShowsEachMessageInTheOrderOfItsContents()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitUpdates(CreateResponse().ToChatResponseUpdates(), ct));
        using var context = new AgentContext(new UIAgent(model));

        await context.SendMessageAsync(CreateQuestion());

        Assert.Equal(ConversationStatus.Idle, context.Status);
        AssertContentOrder(Assert.Single(context.Turns));
    }

    [Fact]
    public async Task RestoredConversation_ShowsEachMessageInTheOrderOfItsContents()
    {
        using var context = new AgentContext(new UIAgent(new ScriptedChatClient()));

        await context.RestoreAsync([CreateQuestion(), .. CreateResponse().Messages]);

        AssertContentOrder(Assert.Single(context.Turns));
    }

    private static void AssertContentOrder(ConversationTurn turn)
    {
        Assert.Collection(
            turn.RequestBlocks,
            block => Assert.IsType<TextContentBlock>(block),
            block => Assert.IsType<MediaContentBlock>(block));
        Assert.Collection(
            turn.ResponseBlocks,
            block => Assert.Equal("Planning", Assert.IsType<ReasoningContentBlock>(block).Text),
            block => Assert.Equal("Let me calculate that.", Assert.IsType<TextContentBlock>(block).RawText),
            block => Assert.True(Assert.IsType<FunctionInvocationContentBlock>(block).HasResult),
            block => Assert.Equal("It is 84.", Assert.IsType<TextContentBlock>(block).RawText));
    }

    private static ChatMessage CreateQuestion()
        => new(ChatRole.User, [new TextContent("What is 12 × 7?"), new DataContent(new byte[] { 1, 2, 3 }, "image/png")]);

    private static ChatResponse CreateResponse()
        => new(
        [
            new ChatMessage(
                ChatRole.Assistant,
                [
                    new TextReasoningContent("Planning"),
                    new TextContent("Let me calculate that."),
                    new FunctionCallContent(
                        "call-1",
                        "calculate",
                        new Dictionary<string, object?> { ["left"] = 12, ["right"] = 7 }),
                ]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", 84)]),
            new ChatMessage(ChatRole.Assistant, "It is 84."),
        ]);
}
