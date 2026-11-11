// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// A turn shows each function call once, whether it is invoked by
/// <see cref="FunctionInvokingChatClient"/>, approved, rejected, informational or restored.
/// </summary>
public class FunctionCallBlockTests
{
    [Fact]
    public async Task ApprovedCall_ShowsOneBlockWithItsResult()
    {
        var deleted = new List<string>();
        var thread = new InMemoryConversationThread("thread-1");
        using var context = CreateApprovalContext(deleted, thread, approve: true);

        await context.SendMessageAsync("Delete a.txt");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["a.txt"], deleted);
        var turn = Assert.Single(context.Turns);
        AssertSingleApprovalBlock(turn, ApprovalStatus.Approved, "Deleted a.txt");
        Assert.Equal("Done.", Assert.Single(turn.ResponseBlocks.OfType<TextContentBlock>()).RawText);
    }

    [Fact]
    public async Task RejectedCall_ShowsOneBlockWithTheRejection()
    {
        var deleted = new List<string>();
        using var context = CreateApprovalContext(deleted, thread: null, approve: false);

        await context.SendMessageAsync("Delete a.txt");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Empty(deleted);
        var turn = Assert.Single(context.Turns);
        var approval = AssertSingleApprovalBlock(
            turn,
            ApprovalStatus.Rejected,
            "Tool call invocation rejected. Not now");
        Assert.True(approval.Call?.InformationalOnly);
    }

    [Fact]
    public async Task RestoreAsync_AfterApprovedCall_ShowsOneBlockWithItsResult()
    {
        var deleted = new List<string>();
        var thread = new InMemoryConversationThread("thread-1");
        using (var live = CreateApprovalContext(deleted, thread, approve: true))
        {
            await live.SendMessageAsync("Delete a.txt");
        }

        using var restored = new AgentContext(new UIAgent(
            new DelegatingStreamingChatClient(),
            options => options.Thread = thread));
        await restored.RestoreAsync();

        var turn = Assert.Single(restored.Turns);
        AssertSingleApprovalBlock(turn, ApprovalStatus.Approved, "Deleted a.txt");
        Assert.Equal("Done.", Assert.Single(turn.ResponseBlocks.OfType<TextContentBlock>()).RawText);
        Assert.Equal(["a.txt"], deleted);
    }

    [Fact]
    public async Task InformationalCall_IsShownButNeverInvokedOrAnswered()
    {
        var invoked = 0;
        var action = AIFunctionFactory.Create(
            () =>
            {
                invoked++;
                return "done";
            },
            "ClientTool",
            "Client tool");
        var requests = new List<List<ChatMessage>>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            requests.Add([.. messages]);
            return requests.Count == 1
                ? EmitInformationalCallsAsync(cancellationToken)
                : ResponseEmitters.EmitTextResponse("Second", cancellationToken);
        });
        using var context = new AgentContext(new UIAgent(
            new FunctionInvokingChatClient(client),
            options => options.RegisterUIAction(action)));

        await context.SendMessageAsync("First");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Single(requests);
        var blocks = Assert.Single(context.Turns).ResponseBlocks
            .OfType<FunctionInvocationContentBlock>()
            .ToList();
        Assert.Collection(
            blocks,
            block =>
            {
                Assert.Equal("server-call", block.Id);
                Assert.Equal("found", block.Result?.Result);
                Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState);
            },
            block =>
            {
                Assert.Equal("client-call", block.Id);
                Assert.False(block.HasResult);
            });
        Assert.Equal(0, invoked);

        await context.SendMessageAsync("Again");

        var history = requests[1].SelectMany(message => message.Contents).ToList();
        Assert.Contains(history, content => content is FunctionCallContent { CallId: "server-call" });
        Assert.Contains(history, content => content is FunctionResultContent { CallId: "server-call" });
        Assert.DoesNotContain(history, content => content is FunctionCallContent { CallId: "client-call" });
    }

    private static ToolApprovalBlock AssertSingleApprovalBlock(
        ConversationTurn turn,
        ApprovalStatus status,
        string result)
    {
        var approval = Assert.Single(turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(status, approval.Status);
        Assert.Equal("call-1", approval.Id);
        Assert.Equal(result, approval.Result?.Result?.ToString());
        Assert.Equal(BlockLifecycleState.Inactive, approval.InnerBlock.LifecycleState);
        Assert.DoesNotContain(turn.ResponseBlocks, block => block is FunctionInvocationContentBlock);
        Assert.Equal(
            turn.ResponseBlocks.Count,
            turn.ResponseBlocks.Select(block => block.Id).Distinct().Count());
        return approval;
    }

    private static AgentContext CreateApprovalContext(
        List<string> deleted,
        InMemoryConversationThread? thread,
        bool approve)
    {
        var tool = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            (string fileName) =>
            {
                deleted.Add(fileName);
                return $"Deleted {fileName}";
            },
            "DeleteFile",
            "Deletes a file."));
        var calls = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => ++calls == 1
            ? ResponseEmitters.EmitToolCallResponse(
                "call-1",
                "DeleteFile",
                new Dictionary<string, object?> { ["fileName"] = "a.txt" },
                cancellationToken)
            : ResponseEmitters.EmitTextResponse("Done.", cancellationToken));
        var context = new AgentContext(new UIAgent(
            new FunctionInvokingChatClient(client),
            options =>
            {
                options.ChatOptions = new ChatOptions { Tools = [tool] };
                options.Thread = thread;
            }));
        context.OnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;

            var approval = context.Turns[^1].ResponseBlocks.OfType<ToolApprovalBlock>().Single();
            if (approve)
                approval.Approve();
            else
                approval.Reject("Not now");
        });
        return context;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitInformationalCallsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-1",
            Contents =
            [
                new FunctionCallContent("server-call", "search") { InformationalOnly = true },
                new FunctionCallContent("client-call", "ClientTool") { InformationalOnly = true },
            ],
        };
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Tool,
            MessageId = "tool-1",
            Contents = [new FunctionResultContent("server-call", "found")],
        };
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-2",
            Contents = [new TextContent("First")],
        };
        await Task.CompletedTask;
    }
}
