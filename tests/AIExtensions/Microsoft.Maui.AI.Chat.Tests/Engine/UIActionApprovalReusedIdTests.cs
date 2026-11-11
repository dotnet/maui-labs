// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// The client answers the approval requests that <see cref="FunctionInvokingChatClient"/> creates for UI
/// actions with the action's result. Providers that number their calls per response reuse call ids, so
/// each request must be matched with the result that follows it, not with any result that has its call id.
/// </summary>
public class UIActionApprovalReusedIdTests
{
    [Fact]
    public async Task UIActionInAnApprovalBatch_ThenItsCallIdAgainInTheNextRound_CompletesTheTurn()
    {
        var scenario = new Scenario(showMapRequiresApproval: false);
        scenario.Model.Enqueue(ct => EmitCalls(ct, ShowMap("0", "Paris"), DeleteFile("1")));
        scenario.Model.Enqueue(ct => EmitCalls(ct, ShowMap("0", "Rome")));
        scenario.Model.Enqueue(ct => EmitText(ct, "Done."));
        using var context = scenario.CreateContext();

        await context.SendMessageAsync("Delete a.txt and show Paris, then Rome");

        Assert.Null(context.Error);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["Paris", "Rome"], scenario.Shown);
        Assert.Equal(["a.txt"], scenario.Deleted);
        Assert.Equal(3, scenario.Model.Requests.Count);
        ToolHistory.AssertValidWithReusedIds(scenario.Model.Requests[2]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UIActionInAnApprovalBatch_ThenAServerToolWithItsCallIdInALaterTurn_KeepsWorking(bool restore)
    {
        var thread = restore ? new InMemoryConversationThread("thread-1") : null;
        var scenario = new Scenario(showMapRequiresApproval: false, thread);
        scenario.Model.Enqueue(ct => EmitCalls(ct, ShowMap("0", "Paris"), DeleteFile("1")));
        scenario.Model.Enqueue(ct => EmitText(ct, "Done."));
        scenario.Model.Enqueue(ct => EmitCalls(ct, GetWeather("0")));
        scenario.Model.Enqueue(ct => EmitText(ct, "Sunny."));
        scenario.Model.Enqueue(ct => EmitText(ct, "You're welcome."));
        var context = scenario.CreateContext();
        await context.SendMessageAsync("Delete a.txt and show Paris");
        await context.SendMessageAsync("And the weather?");
        if (restore)
        {
            context.Dispose();
            context = scenario.CreateContext();
            await context.RestoreAsync();
        }

        await context.SendMessageAsync("Thanks");

        Assert.Null(context.Error);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(5, scenario.Model.Requests.Count);
        ToolHistory.AssertValidWithReusedIds(scenario.Model.Requests[4]);
        Assert.Equal(["Paris"], scenario.Shown);
        Assert.Equal(["a.txt"], scenario.Deleted);
        Assert.Equal(1, scenario.Forecasts);
        context.Dispose();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task ApprovalRequiredUIAction_ThenAServerToolWithItsCallId_SendsEachResultAfterItsCall(
        bool approve,
        bool restore)
    {
        var thread = restore ? new InMemoryConversationThread("thread-1") : null;
        var scenario = new Scenario(showMapRequiresApproval: true, thread) { RejectShowMap = !approve };
        scenario.Model.Enqueue(ct => EmitCalls(ct, ShowMap("0", "Paris")));
        scenario.Model.Enqueue(ct => EmitText(ct, "Done."));
        scenario.Model.Enqueue(ct => EmitCalls(ct, GetWeather("0")));
        scenario.Model.Enqueue(ct => EmitText(ct, "Sunny."));
        scenario.Model.Enqueue(ct => EmitText(ct, "You're welcome."));
        var context = scenario.CreateContext();
        await context.SendMessageAsync("Show Paris");
        await context.SendMessageAsync("And the weather?");
        if (restore)
        {
            context.Dispose();
            context = scenario.CreateContext();
            await context.RestoreAsync();
        }

        await context.SendMessageAsync("Thanks");

        Assert.Null(context.Error);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(approve ? ["Paris"] : [], scenario.Shown);
        Assert.Equal(1, scenario.Forecasts);
        Assert.Equal(5, scenario.Model.Requests.Count);
        ToolHistory.AssertValidWithReusedIds(scenario.Model.Requests[4]);
        Assert.Equal(
            [approve ? "Map of Paris" : "Tool call invocation rejected. No", "Sunny"],
            FindResults(scenario.Model.Requests[4], "0"));
        context.Dispose();
    }

    [Fact]
    public async Task StatefulThread_UIActionInAnApprovalBatch_SendsOnlyResultsAndContinues()
    {
        var scenario = new Scenario(showMapRequiresApproval: false, new InMemoryConversationThread("thread-1"))
        {
            ConversationId = "conversation-1",
        };
        scenario.Model.Enqueue(ct => scenario.EmitCalls(ct, ShowMap("0", "Paris"), DeleteFile("1")));
        scenario.Model.Enqueue(ct => scenario.EmitCalls(ct, ShowMap("0", "Rome")));
        scenario.Model.Enqueue(ct => scenario.EmitText(ct, "Done."));
        scenario.Model.Enqueue(ct => scenario.EmitText(ct, "Again."));
        using var context = scenario.CreateContext();

        await context.SendMessageAsync("Delete a.txt and show Paris, then Rome");
        await context.SendMessageAsync("Again");

        Assert.Null(context.Error);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["Paris", "Rome"], scenario.Shown);
        Assert.Equal(["a.txt"], scenario.Deleted);
        Assert.Equal(4, scenario.Model.Requests.Count);
        Assert.Equal(["Map of Paris", "Deleted a.txt"], ResultsOf(scenario.Model.Requests[1]));
        Assert.Equal(["Map of Rome"], ResultsOf(scenario.Model.Requests[2]));
        Assert.Equal(["Again"], scenario.Model.Requests[3].Select(message => message.Text));
        Assert.All(scenario.Model.Requests, AssertNoPendingApprovals);
    }

    [Fact]
    public async Task StatefulThread_ApprovedUIAction_SendsItsResultWithoutTheApprovalResponse()
    {
        var scenario = new Scenario(showMapRequiresApproval: true, new InMemoryConversationThread("thread-1"))
        {
            ConversationId = "conversation-1",
        };
        scenario.Model.Enqueue(ct => scenario.EmitCalls(ct, ShowMap("0", "Paris")));
        scenario.Model.Enqueue(ct => scenario.EmitText(ct, "Done."));
        using var context = scenario.CreateContext();

        await context.SendMessageAsync("Show Paris");

        Assert.Null(context.Error);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["Paris"], scenario.Shown);
        Assert.Equal(2, scenario.Model.Requests.Count);
        Assert.Equal(["Map of Paris"], ResultsOf(scenario.Model.Requests[1]));
        Assert.All(scenario.Model.Requests, AssertNoPendingApprovals);
    }

    private static void AssertNoPendingApprovals(IReadOnlyList<ChatMessage> request)
        => Assert.DoesNotContain(
            request.SelectMany(message => message.Contents),
            content => content is ToolApprovalRequestContent { ToolCall: FunctionCallContent { InformationalOnly: false } }
                or ToolApprovalResponseContent { ToolCall: FunctionCallContent { InformationalOnly: false } });

    private static List<string?> ResultsOf(IReadOnlyList<ChatMessage> request)
        => request
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Select(result => result.Result?.ToString())
            .ToList();

    private static IEnumerable<string?> FindResults(IReadOnlyList<ChatMessage> request, string callId)
        => request
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Where(result => result.CallId == callId)
            .Select(result => result.Result?.ToString());

    private static FunctionCallContent ShowMap(string callId, string city)
        => new(callId, "ShowMap", new Dictionary<string, object?> { ["city"] = city });

    private static FunctionCallContent DeleteFile(string callId)
        => new(callId, "DeleteFile", new Dictionary<string, object?> { ["fileName"] = "a.txt" });

    private static FunctionCallContent GetWeather(string callId)
        => new(callId, "GetWeather");

    private static IAsyncEnumerable<ChatResponseUpdate> EmitCalls(
        CancellationToken cancellationToken,
        params FunctionCallContent[] calls)
        => Emit(cancellationToken, conversationId: null, [.. calls]);

    private static IAsyncEnumerable<ChatResponseUpdate> EmitText(CancellationToken cancellationToken, string text)
        => Emit(cancellationToken, conversationId: null, [new TextContent(text)]);

    private static async IAsyncEnumerable<ChatResponseUpdate> Emit(
        [EnumeratorCancellation] CancellationToken cancellationToken,
        string? conversationId,
        List<AIContent> contents)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = Guid.NewGuid().ToString("N"),
            ConversationId = conversationId,
            Contents = contents,
            FinishReason = contents.Any(content => content is FunctionCallContent)
                ? ChatFinishReason.ToolCalls
                : ChatFinishReason.Stop,
        };
        await Task.CompletedTask;
    }

    private sealed class Scenario(bool showMapRequiresApproval, InMemoryConversationThread? thread = null)
    {
        internal ScriptedChatClient Model { get; } = new();

        internal List<string> Deleted { get; } = [];

        internal List<string> Shown { get; } = [];

        internal int Forecasts { get; private set; }

        internal string? ConversationId { get; init; }

        internal bool RejectShowMap { get; init; }

        internal IAsyncEnumerable<ChatResponseUpdate> EmitCalls(
            CancellationToken cancellationToken,
            params FunctionCallContent[] calls)
            => Emit(cancellationToken, ConversationId, [.. calls]);

        internal IAsyncEnumerable<ChatResponseUpdate> EmitText(CancellationToken cancellationToken, string text)
            => Emit(cancellationToken, ConversationId, [new TextContent(text)]);

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
            var getWeather = AIFunctionFactory.Create(
                () =>
                {
                    Forecasts++;
                    return "Sunny";
                },
                "GetWeather",
                "Gets the weather.");
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
                    options.ChatOptions = new ChatOptions { Tools = [deleteFile, getWeather] };
                    options.RegisterUIAction(showMap);
                    options.Thread = thread;
                }));
            context.RegisterOnStatusChanged(status =>
            {
                if (status != ConversationStatus.AwaitingInput)
                    return;

                foreach (var approval in context.Turns[^1].ResponseBlocks.OfType<ToolApprovalBlock>())
                {
                    if (RejectShowMap && approval.ToolName == "ShowMap")
                        approval.Reject("No");
                    else
                        approval.Approve();
                }
            });
            return context;
        }
    }
}
