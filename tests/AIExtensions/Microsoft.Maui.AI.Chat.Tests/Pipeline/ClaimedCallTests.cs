// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

/// <summary>
/// A handler of the app's own that claims function calls into a block of its own shows those calls: the turn's
/// call registry routes their later content to it, as it does for a call block.
/// </summary>
public class ClaimedCallTests
{
    [Fact]
    public async Task ResultInALaterRound_ReachesTheHandlerThatClaimedTheCall()
    {
        var (options, calls) = CreateOptions();
        var firstRound = new BlockMappingPipeline(options, calls: calls);
        var list = Assert.IsType<CallListBlock>(Assert.Single(
            await ProcessAsync(firstRound, Update(ChatRole.Assistant, Call("c1"), Call("c2")))));
        firstRound.Finalize();

        var secondRound = new BlockMappingPipeline(options, calls: calls);
        var result = new FunctionResultContent("c2", "done");

        Assert.Empty(await ProcessAsync(secondRound, Update(ChatRole.Tool, result)));
        Assert.Same(result, list.Find("c2")?.Result);
        Assert.Null(list.Find("c1")?.Result);
    }

    [Fact]
    public async Task ReplayedCall_IsNotShownAgain()
    {
        var (options, calls) = CreateOptions();
        var firstRound = new BlockMappingPipeline(options, calls: calls);
        var list = Assert.IsType<CallListBlock>(Assert.Single(
            await ProcessAsync(firstRound, Update(ChatRole.Assistant, Call("c1")))));
        firstRound.Finalize();

        var secondRound = new BlockMappingPipeline(options, calls: calls);
        var result = new FunctionResultContent("c1", "done");

        Assert.Empty(await ProcessAsync(secondRound, Update(ChatRole.Assistant, Call("c1"), result)));
        Assert.Same(result, Assert.Single(list.Rows).Result);
    }

    [Fact]
    public async Task CallIdReusedAfterItsResult_IsANewCall()
    {
        var (options, calls) = CreateOptions();
        var firstRound = new BlockMappingPipeline(options, calls: calls);
        var first = Assert.IsType<CallListBlock>(Assert.Single(
            await ProcessAsync(firstRound, Update(ChatRole.Assistant, Call("0")))));
        Assert.Empty(await ProcessAsync(firstRound, Update(ChatRole.Tool, new FunctionResultContent("0", "first"))));
        firstRound.Finalize();

        var secondRound = new BlockMappingPipeline(options, calls: calls);
        var second = Assert.IsType<CallListBlock>(Assert.Single(
            await ProcessAsync(secondRound, Update(ChatRole.Assistant, Call("0")))));
        var result = new FunctionResultContent("0", "second");
        Assert.Empty(await ProcessAsync(secondRound, Update(ChatRole.Tool, result)));

        Assert.NotSame(first, second);
        Assert.Equal("first", Assert.Single(first.Rows).Result?.Result);
        Assert.Same(result, Assert.Single(second.Rows).Result);
    }

    private static (UIAgentOptions Options, FunctionCallRegistry Calls) CreateOptions()
    {
        var options = new UIAgentOptions();
        options.AddBlockHandler(new CallListHandler());
        return (options, new FunctionCallRegistry());
    }

    private static FunctionCallContent Call(string callId) => new(callId, "Calculate");

    private static ChatResponseUpdate Update(ChatRole role, params AIContent[] contents)
        => new() { Role = role, MessageId = Guid.NewGuid().ToString("N"), Contents = [.. contents] };

    private static async Task<List<ContentBlock>> ProcessAsync(BlockMappingPipeline pipeline, ChatResponseUpdate update)
    {
        var blocks = new List<ContentBlock>();
        await foreach (var block in pipeline.Process(update))
            blocks.Add(block);
        return blocks;
    }

    private sealed class CallListBlock : ContentBlock
    {
        public List<CallRow> Rows { get; } = [];

        public CallRow? Find(string callId) => Rows.Find(row => row.CallId == callId);
    }

    private sealed class CallRow(string callId)
    {
        public string CallId { get; } = callId;

        public FunctionResultContent? Result { get; set; }
    }

    /// <summary>Folds the calls of an update, and their results, into one list with a row per call.</summary>
    private sealed class CallListHandler : ContentBlockHandler<CallListBlock>
    {
        public override BlockMappingResult<CallListBlock> Handle(BlockMappingContext context, CallListBlock state)
        {
            var changed = false;
            foreach (var content in context.UnhandledContents)
            {
                switch (content)
                {
                    case FunctionCallContent { Name: "Calculate" } call:
                        context.MarkHandled(call);
                        if (state.Find(call.CallId) is null)
                            state.Rows.Add(new CallRow(call.CallId));
                        changed = true;
                        break;
                    case FunctionResultContent result when state.Find(result.CallId) is { } row:
                        context.MarkHandled(result);
                        row.Result = result;
                        changed = true;
                        break;
                }
            }

            if (!changed)
                return BlockMappingResult<CallListBlock>.Pass();

            if (!string.IsNullOrEmpty(state.Id))
                return BlockMappingResult<CallListBlock>.Update(state);

            state.Id = context.Update.MessageId ?? Guid.NewGuid().ToString("N");
            return BlockMappingResult<CallListBlock>.Emit(state, state);
        }
    }
}
