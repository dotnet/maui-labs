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

    [Fact]
    public async Task CallReusingTheIdOfAClaimedCall_IsANewCall_OnceAResultForTheClaimedCallPassedBy()
    {
        // The handler shows calls but not their results; a result still answers its call, so a provider that numbers
        // its calls per response can use the id again for a call that another handler shows.
        var (options, calls) = CreateOptions(claimsResults: false);
        var pipeline = new BlockMappingPipeline(options, calls: calls);
        var list = Assert.IsType<CallListBlock>(Assert.Single(
            await ProcessAsync(pipeline, Update(ChatRole.Assistant, Call("0")))));
        Assert.Empty(await ProcessAsync(pipeline, Update(ChatRole.Tool, new FunctionResultContent("0", "found"))));

        var other = new FunctionCallContent("0", "SetTheme");
        var block = Assert.IsType<FunctionInvocationContentBlock>(Assert.Single(
            await ProcessAsync(pipeline, Update(ChatRole.Assistant, other))));

        Assert.Same(other, block.Call);
        Assert.Single(list.Rows);
    }

    [Fact]
    public async Task CallReusingTheIdOfAClaimedCall_IsANewCall_WhenItsResultComesInTheSameUpdate()
    {
        var (options, calls) = CreateOptions(claimsResults: false);
        var pipeline = new BlockMappingPipeline(options, calls: calls);
        Assert.IsType<CallListBlock>(Assert.Single(
            await ProcessAsync(pipeline, Update(ChatRole.Assistant, Call("0"), new FunctionResultContent("0", "found")))));

        var other = new FunctionCallContent("0", "SetTheme");
        var block = Assert.IsType<FunctionInvocationContentBlock>(Assert.Single(
            await ProcessAsync(pipeline, Update(ChatRole.Assistant, other))));

        Assert.Same(other, block.Call);
    }

    [Fact]
    public async Task ReplayFollowedByItsResult_AnswersTheClaimedCall()
    {
        // A replay of a call, such as an approved call that FunctionInvokingChatClient sends again before its result, is
        // the same call; the result after it answers that call even when no handler shows the result.
        var (options, calls) = CreateOptions(claimsResults: false);
        var firstRound = new BlockMappingPipeline(options, calls: calls);
        Assert.IsType<CallListBlock>(Assert.Single(await ProcessAsync(firstRound, Update(ChatRole.Assistant, Call("0")))));
        firstRound.Finalize();

        var secondRound = new BlockMappingPipeline(options, calls: calls);
        Assert.Empty(await ProcessAsync(
            secondRound,
            Update(ChatRole.Assistant, Call("0"), new FunctionResultContent("0", "found"))));
        var other = new FunctionCallContent("0", "SetTheme");
        var block = Assert.IsType<FunctionInvocationContentBlock>(Assert.Single(
            await ProcessAsync(secondRound, Update(ChatRole.Assistant, other))));

        Assert.Same(other, block.Call);
    }

    [Fact]
    public async Task ResultBeforeACallThatReusesItsId_DoesNotAnswerThatCall()
    {
        // The result answers the claimed call before it; the call after it is a new call that still waits.
        var (options, calls) = CreateOptions(claimsResults: false, rowPerCall: true);
        var pipeline = new BlockMappingPipeline(options, calls: calls);
        var list = Assert.IsType<CallListBlock>(Assert.Single(
            await ProcessAsync(pipeline, Update(ChatRole.Assistant, Call("0")))));
        var reused = Call("0");

        Assert.Empty(await ProcessAsync(pipeline, Update(ChatRole.Assistant, new FunctionResultContent("0", "found"), reused)));

        Assert.Equal(2, list.Rows.Count);
        Assert.True(calls.TryGet("0", out var registration));
        Assert.Same(reused, registration.Call);
        Assert.False(registration.HasResult);
    }

    [Fact]
    public async Task CallIdReusedWhileTheBlockThatClaimedItIsActive_IsANewCallOfThatBlock()
    {
        // A block with a row per call claims a reused id while it is still streaming; the later result for the new
        // call reaches its row instead of being taken for the first call's.
        var (options, calls) = CreateOptions(rowPerCall: true);
        var firstRound = new BlockMappingPipeline(options, calls: calls);
        var list = Assert.IsType<CallListBlock>(Assert.Single(
            await ProcessAsync(firstRound, Update(ChatRole.Assistant, Call("0")))));
        Assert.Empty(await ProcessAsync(firstRound, Update(ChatRole.Tool, new FunctionResultContent("0", "first"))));
        Assert.Empty(await ProcessAsync(firstRound, Update(ChatRole.Assistant, Call("0"))));
        firstRound.Finalize();

        var secondRound = new BlockMappingPipeline(options, calls: calls);
        var result = new FunctionResultContent("0", "second");
        Assert.Empty(await ProcessAsync(secondRound, Update(ChatRole.Tool, result)));

        Assert.Collection(
            list.Rows,
            row => Assert.Equal("first", row.Result?.Result),
            row => Assert.Same(result, row.Result));
    }

    private static (UIAgentOptions Options, FunctionCallRegistry Calls) CreateOptions(
        bool claimsResults = true,
        bool rowPerCall = false)
    {
        var options = new UIAgentOptions();
        options.AddBlockHandler(new CallListHandler(claimsResults, rowPerCall));
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

        /// <summary>Finds the first row of the call id that still waits for its result.</summary>
        public CallRow? FindWaiting(string callId) => Rows.Find(row => row.CallId == callId && row.Result is null);
    }

    private sealed class CallRow(FunctionCallContent call)
    {
        public FunctionCallContent Call { get; } = call;

        public string CallId => Call.CallId;

        public FunctionResultContent? Result { get; set; }
    }

    /// <summary>
    /// Folds the calls of an update, and their results, into one list with a row per call id, or with
    /// <paramref name="rowPerCall"/> a row per call even when a provider reuses its id. With
    /// <paramref name="claimsResults"/> false it shows the calls but leaves their results to others.
    /// </summary>
    private sealed class CallListHandler(bool claimsResults, bool rowPerCall) : ContentBlockHandler<CallListBlock>
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
                        if (rowPerCall
                            ? !state.Rows.Exists(row => ReferenceEquals(row.Call, call))
                            : state.Find(call.CallId) is null)
                        {
                            state.Rows.Add(new CallRow(call));
                        }

                        changed = true;
                        break;
                    case FunctionResultContent result
                        when claimsResults
                            && (rowPerCall ? state.FindWaiting(result.CallId) : state.Find(result.CallId)) is { } row:
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
