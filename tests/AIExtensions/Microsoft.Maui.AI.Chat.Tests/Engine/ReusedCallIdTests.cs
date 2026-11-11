// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// Some providers number their calls per response, so a call id comes back in a later round of the same
/// turn for a different call.
/// </summary>
public class ReusedCallIdTests
{
    [Fact]
    public async Task UIActionCallIdReusedInTheNextRound_RunsBothActionsAndAnswersEachCall()
    {
        var shown = new List<string>();
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, ShowMapCall("0", "Paris")));
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, ShowMapCall("0", "Rome")));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = new AgentContext(new UIAgent(model, options =>
            options.RegisterUIAction(AIFunctionFactory.Create(
                (string city) =>
                {
                    shown.Add(city);
                    return $"Map of {city}";
                },
                "ShowMap",
                "Shows a map."))));

        await context.SendMessageAsync("Show Paris, then Rome");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["Paris", "Rome"], shown);
        var actions = Assert.Single(context.Turns).ResponseBlocks.OfType<UIActionBlock>().ToList();
        Assert.Equal(2, actions.Count);
        Assert.Equal("Map of Paris", actions[0].Result?.Result?.ToString());
        Assert.Equal("Map of Rome", actions[1].Result?.Result?.ToString());
        Assert.Equal(
            ["call:0", "result:Map of Paris", "call:0", "result:Map of Rome"],
            model.Requests[2]
                .SelectMany(message => message.Contents)
                .Select(content => content switch
                {
                    FunctionCallContent => "call:0",
                    FunctionResultContent result => "result:" + result.Result,
                    _ => null,
                })
                .OfType<string>());
    }

    [Fact]
    public async Task ServerToolCallIdReusedByTheModel_ShowsEachCallWithItsOwnResult()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitToolCallResponse("0", "GetWeather", ct: ct));
        model.Enqueue(ct => ResponseEmitters.EmitToolCallResponse("0", "GetNews", ct: ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        var tools = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(() => "Sunny", "GetWeather"),
                AIFunctionFactory.Create(() => "Headlines", "GetNews"),
            ],
        };
        using var context = new AgentContext(new UIAgent(new FunctionInvokingChatClient(model), tools));

        await context.SendMessageAsync("Weather and news");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        var calls = Assert.Single(context.Turns).ResponseBlocks.OfType<FunctionInvocationContentBlock>().ToList();
        Assert.Equal(["GetWeather", "GetNews"], calls.Select(call => call.ToolName));
        Assert.Equal(["Sunny", "Headlines"], calls.Select(call => call.Result?.Result?.ToString()));
    }

    private static FunctionCallContent ShowMapCall(string callId, string city)
        => new(callId, "ShowMap", new Dictionary<string, object?> { ["city"] = city });
}
