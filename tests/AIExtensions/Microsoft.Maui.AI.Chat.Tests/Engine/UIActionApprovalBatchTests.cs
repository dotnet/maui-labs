// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// When one call of a response needs approval, <see cref="FunctionInvokingChatClient"/> turns every call
/// of that response into an approval request, including calls to UI actions, which it cannot invoke.
/// </summary>
public class UIActionApprovalBatchTests
{
    [Theory]
    [InlineData(UIActionInvocationMode.Automatic)]
    [InlineData(UIActionInvocationMode.Manual)]
    public async Task UIActionBatchedWithApprovalRequiredTool_RunsAsUIActionAndApprovalStillWorks(
        UIActionInvocationMode mode)
    {
        var scenario = new Scenario(showMapRequiresApproval: false, mode);
        scenario.Model.Enqueue(ct => EmitCalls(
            ct,
            new FunctionCallContent("delete-1", "DeleteFile", new Dictionary<string, object?> { ["fileName"] = "a.txt" }),
            new FunctionCallContent("map-1", "ShowMap", new Dictionary<string, object?> { ["city"] = "Paris" })));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Again.", ct));
        using var context = scenario.CreateContext();

        await context.SendMessageAsync("Delete a.txt and show Paris");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["a.txt"], scenario.Deleted);
        Assert.Equal(["Paris"], scenario.Shown);
        var turn = Assert.Single(context.Turns);
        var approval = Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal("DeleteFile", approval.ToolName);
        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Equal("Deleted a.txt", approval.Result?.Result?.ToString());
        var action = Assert.Single(turn.ResponseBlocks.OfType<UIActionBlock>());
        Assert.Equal("ShowMap", action.ToolName);
        Assert.Equal("Map of Paris", action.Result?.Result?.ToString());
        Assert.Equal("Done.", Assert.Single(turn.ResponseBlocks.OfType<TextContentBlock>()).RawText);

        Assert.Equal(2, scenario.Model.Requests.Count);
        ToolHistory.AssertValid(scenario.Model.Requests[1]);
        Assert.Equal("Map of Paris", ToolHistory.FindResult(scenario.Model.Requests[1], "map-1"));

        await context.SendMessageAsync("Again");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(3, scenario.Model.Requests.Count);
        ToolHistory.AssertValid(scenario.Model.Requests[2]);
        Assert.Equal(["Paris"], scenario.Shown);
        Assert.Equal(["a.txt"], scenario.Deleted);
    }

    [Fact]
    public async Task UIActionBatchedWithApprovalRequiredTool_RestoredFromThread_ContinuesWithValidHistory()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var scenario = new Scenario(showMapRequiresApproval: false, UIActionInvocationMode.Automatic, thread);
        scenario.Model.Enqueue(ct => EmitCalls(
            ct,
            new FunctionCallContent("delete-1", "DeleteFile", new Dictionary<string, object?> { ["fileName"] = "a.txt" }),
            new FunctionCallContent("map-1", "ShowMap", new Dictionary<string, object?> { ["city"] = "Paris" })));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using (var live = scenario.CreateContext())
            await live.SendMessageAsync("Delete a.txt and show Paris");

        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Again.", ct));
        using var restored = scenario.CreateContext();
        await restored.RestoreAsync();

        var turn = Assert.Single(restored.Turns);
        var approval = Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        var action = Assert.Single(turn.ResponseBlocks.OfType<UIActionBlock>());
        Assert.Equal("Map of Paris", action.Result?.Result?.ToString());

        await restored.SendMessageAsync("Again");

        Assert.Equal(ConversationStatus.Idle, restored.Status);
        ToolHistory.AssertValid(scenario.Model.Requests[^1]);
        Assert.Equal(["Paris"], scenario.Shown);
        Assert.Equal(["a.txt"], scenario.Deleted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApprovalRequiredUIAction_ShowsApprovalAndRunsTheActionOnlyWhenApproved(bool approve)
    {
        var scenario = new Scenario(showMapRequiresApproval: true, UIActionInvocationMode.Automatic)
        {
            ApproveShowMap = approve,
        };
        scenario.Model.Enqueue(ct => EmitCalls(
            ct,
            new FunctionCallContent("map-1", "ShowMap", new Dictionary<string, object?> { ["city"] = "Paris" })));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Again.", ct));
        using var context = scenario.CreateContext();

        await context.SendMessageAsync("Show Paris");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        var turn = Assert.Single(context.Turns);
        var approval = Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal("ShowMap", approval.ToolName);
        Assert.Equal(approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected, approval.Status);
        var expected = approve ? "Map of Paris" : RejectedCall.Result("Not now");
        Assert.Equal(expected, approval.Result?.Result?.ToString());
        Assert.Equal(approve ? ["Paris"] : [], scenario.Shown);
        Assert.Equal(
            turn.ResponseBlocks.Count,
            turn.ResponseBlocks.Select(block => block.Id).Distinct().Count());

        ToolHistory.AssertValid(scenario.Model.Requests[1]);
        Assert.Equal(expected, ToolHistory.FindResult(scenario.Model.Requests[1], "map-1"));

        await context.SendMessageAsync("Again");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        ToolHistory.AssertValid(scenario.Model.Requests[2]);
        Assert.Equal(approve ? ["Paris"] : [], scenario.Shown);
    }

    [Fact]
    public async Task ApprovalRequiredManualUIAction_RunsWhenApproved()
    {
        var scenario = new Scenario(showMapRequiresApproval: true, UIActionInvocationMode.Manual);
        scenario.Model.Enqueue(ct => EmitCalls(
            ct,
            new FunctionCallContent("map-1", "ShowMap", new Dictionary<string, object?> { ["city"] = "Paris" })));
        scenario.Model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = scenario.CreateContext();

        await context.SendMessageAsync("Show Paris");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["Paris"], scenario.Shown);
        var turn = Assert.Single(context.Turns);
        var approval = Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Equal("Map of Paris", approval.Result?.Result?.ToString());
        Assert.DoesNotContain(turn.ResponseBlocks, block => block is UIActionBlock);
        ToolHistory.AssertValid(scenario.Model.Requests[1]);
        Assert.Equal("Map of Paris", ToolHistory.FindResult(scenario.Model.Requests[1], "map-1"));
    }

    [Fact]
    public async Task ApprovalRequiredUIAction_WithoutFunctionInvocation_Throws()
    {
        var model = new ScriptedChatClient();
        var showMap = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            (string city) => $"Map of {city}",
            "ShowMap",
            "Shows a map."));
        using var context = new AgentContext(new UIAgent(
            model,
            options => options.RegisterUIAction(showMap)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SendMessageAsync("Show Paris"));

        Assert.Contains("ShowMap", error.Message, StringComparison.Ordinal);
        Assert.Empty(context.Turns);
        Assert.Empty(model.Requests);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitCalls(
        [EnumeratorCancellation] CancellationToken cancellationToken,
        params FunctionCallContent[] calls)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-calls",
            Contents = [.. calls],
            FinishReason = ChatFinishReason.ToolCalls,
        };
        await Task.CompletedTask;
    }

    private sealed class Scenario(
        bool showMapRequiresApproval,
        UIActionInvocationMode mode,
        InMemoryConversationThread? thread = null)
    {
        internal ScriptedChatClient Model { get; } = new();

        internal List<string> Deleted { get; } = [];

        internal List<string> Shown { get; } = [];

        internal bool ApproveShowMap { get; init; } = true;

        internal AgentContext CreateContext()
        {
            AIFunction deleteFile = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
                (string fileName) =>
                {
                    Deleted.Add(fileName);
                    return $"Deleted {fileName}";
                },
                "DeleteFile",
                "Deletes a file."));
            AIFunction showMap = AIFunctionFactory.Create(
                (string city) =>
                {
                    Shown.Add(city);
                    return $"Map of {city}";
                },
                "ShowMap",
                "Shows a map.");
            if (showMapRequiresApproval)
                showMap = new ApprovalRequiredAIFunction(showMap);

            var context = new AgentContext(new UIAgent(
                new FunctionInvokingChatClient(Model),
                options =>
                {
                    options.ChatOptions = new ChatOptions { Tools = [deleteFile] };
                    options.RegisterUIAction(showMap, mode);
                    options.Thread = thread;
                }));
            context.OnStatusChanged(status =>
            {
                if (status != ConversationStatus.AwaitingInput)
                    return;

                var blocks = context.Turns[^1].ResponseBlocks;
                foreach (var approval in blocks.OfType<ToolApprovalBlock>())
                {
                    if (approval.ToolName == "ShowMap" && !ApproveShowMap)
                        approval.Reject("Not now");
                    else
                        approval.Approve();
                }

                foreach (var action in blocks.OfType<UIActionBlock>())
                {
                    if (action.Mode == UIActionInvocationMode.Manual)
                        _ = action.InvokeAsync();
                }
            });
            return context;
        }
    }
}
