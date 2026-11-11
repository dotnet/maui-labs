// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// The engine drives a turn's follow-up rounds, which send the results of UI actions and approvals, the way
/// <see cref="FunctionInvokingChatClient"/> drives its iterations: a required tool mode applies to the first
/// request only, so the model can answer the results instead of calling a tool again.
/// </summary>
public class FollowUpRoundTests
{
    [Fact]
    public async Task RequiredToolMode_AppliesOnlyToTheFirstRoundOfEachTurn()
    {
        var toolModes = new List<ChatToolMode?>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            toolModes.Add(options?.ToolMode);

            // The follow-up round sends the action's result; the model then answers.
            return messages.Last().Role == ChatRole.Tool
                ? ResponseEmitters.EmitTextResponse("Done.", cancellationToken)
                : ResponseEmitters.EmitMultipleToolCallResponse(cancellationToken, SetTheme($"theme-{toolModes.Count}"));
        });
        var chatOptions = new ChatOptions { ToolMode = ChatToolMode.RequireAny };
        using var context = new AgentContext(new UIAgent(new FunctionInvokingChatClient(client), options =>
        {
            options.ChatOptions = chatOptions;
            options.RegisterUIAction(CreateSetTheme(), UIActionInvocationMode.Automatic);
        }));

        await context.SendMessageAsync("Go dark");
        await context.SendMessageAsync("Go light");

        Assert.Equal([ChatToolMode.RequireAny, null, ChatToolMode.RequireAny, null], toolModes);
        Assert.Equal(ChatToolMode.RequireAny, chatOptions.ToolMode);
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    [Fact]
    public async Task RequiredToolMode_DoesNotApplyToTheRoundThatSendsAnApproval()
    {
        var toolModes = new List<ChatToolMode?>();
        var deleted = new List<string>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            toolModes.Add(options?.ToolMode);
            return toolModes.Count == 1
                ? ResponseEmitters.EmitToolCallResponse(
                    "delete-1",
                    "DeleteFile",
                    new Dictionary<string, object?> { ["fileName"] = "a.txt" },
                    cancellationToken)
                : ResponseEmitters.EmitTextResponse("Deleted.", cancellationToken);
        });
        var deleteFile = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            (string fileName) =>
            {
                deleted.Add(fileName);
                return $"Deleted {fileName}";
            },
            "DeleteFile",
            "Deletes a file."));
        using var context = new AgentContext(new UIAgent(new FunctionInvokingChatClient(client), options =>
            options.ChatOptions = new ChatOptions { Tools = [deleteFile], ToolMode = ChatToolMode.RequireAny }));
        context.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentContext.Status) && context.Status == ConversationStatus.AwaitingInput)
                context.Turns[^1].ResponseBlocks.OfType<ToolApprovalBlock>().Single().Approve();
        };

        await context.SendMessageAsync("Delete a.txt");

        Assert.Equal(["a.txt"], deleted);
        Assert.Equal([ChatToolMode.RequireAny, null], toolModes);
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    [Fact]
    public async Task ModelThatKeepsCallingAnAction_FailsTheTurnOnceItUsedItsRounds()
    {
        var requests = 0;
        var themes = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            // Without a limit the model would call the action forever; end it after ten requests.
            return ++requests < 10
                ? ResponseEmitters.EmitMultipleToolCallResponse(cancellationToken, SetTheme($"theme-{requests}"))
                : ResponseEmitters.EmitTextResponse("Done.", cancellationToken);
        });
        using var context = new AgentContext(new UIAgent(new FunctionInvokingChatClient(client), options =>
        {
            options.RegisterUIAction(CreateSetTheme(() => themes++), UIActionInvocationMode.Automatic);
            options.MaximumRoundsPerTurn = 3;
        }));

        await context.SendMessageAsync("Go dark");

        // The third round's action never runs: no round is left to send its result.
        Assert.Equal(3, requests);
        Assert.Equal(2, themes);
        Assert.Equal(ConversationStatus.Error, context.Status);
        var error = Assert.IsType<InvalidOperationException>(context.Error);
        Assert.Contains("within 3 rounds", error.Message);
        Assert.Contains(nameof(UIAgentOptions.MaximumRoundsPerTurn), error.Message);
        var turn = Assert.Single(context.Turns);
        Assert.True(turn.IsFailed);
        var lastAction = turn.ResponseBlocks.OfType<UIActionBlock>().Last();
        Assert.False(lastAction.IsComplete);
        Assert.True(lastAction.IsAbandoned);
    }

    [Fact]
    public async Task TurnThatFailedOnItsRounds_CanBeRetriedWithAllOfThemAgain()
    {
        var requests = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
            ++requests is 1 or 2 or 3
                ? ResponseEmitters.EmitMultipleToolCallResponse(cancellationToken, SetTheme($"theme-{requests}"))
                : ResponseEmitters.EmitTextResponse("Done.", cancellationToken));
        using var context = new AgentContext(new UIAgent(new FunctionInvokingChatClient(client), options =>
        {
            options.RegisterUIAction(CreateSetTheme(), UIActionInvocationMode.Automatic);
            options.MaximumRoundsPerTurn = 2;
        }));
        await context.SendMessageAsync("Go dark");
        Assert.Equal(ConversationStatus.Error, context.Status);

        // The retry counts its rounds from the start: one more action, then the answer.
        await context.RetryAsync();

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(4, requests);
        Assert.False(Assert.Single(context.Turns).IsFailed);
    }

    [Fact]
    public void MaximumRoundsPerTurn_DefaultsToFortyAndMustBePositive()
    {
        var options = new UIAgentOptions();

        Assert.Equal(40, options.MaximumRoundsPerTurn);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaximumRoundsPerTurn = 0);
        options.MaximumRoundsPerTurn = 1;
        Assert.Equal(1, options.MaximumRoundsPerTurn);
    }

    private static FunctionCallContent SetTheme(string callId)
        => new(callId, "SetTheme", new Dictionary<string, object?> { ["theme"] = "dark" });

    private static AIFunction CreateSetTheme(Action? invoked = null)
        => AIFunctionFactory.Create(
            (string theme) =>
            {
                invoked?.Invoke();
                return $"Theme set to {theme}";
            },
            "SetTheme",
            "Sets the theme.");
}
