// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// A handler for a UI action's name, such as a typed card or a <c>[ToolBlock]</c>, shows the call inside
/// the action instead of replacing it, so the action still runs.
/// </summary>
public class UIActionCustomBlockTests
{
    [Fact]
    public async Task CustomHandlerForUIActionName_ActionRunsAndTheCardShowsInsideIt()
    {
        var shown = new List<string>();
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, ShowMapCall()));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = new AgentContext(new UIAgent(model, options =>
        {
            options.AddBlockHandler(new MapCardHandler());
            options.RegisterUIAction(CreateShowMap(shown));
        }));

        await context.SendMessageAsync("Show Paris");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["Paris"], shown);
        var turn = Assert.Single(context.Turns);
        Assert.DoesNotContain(turn.ResponseBlocks, block => block is MapCard);
        var action = Assert.Single(turn.ResponseBlocks.OfType<UIActionBlock>());
        Assert.Equal("map-1", action.Id);
        var card = Assert.IsType<MapCard>(action.InnerBlock);
        Assert.Equal("Paris", card.City);
        Assert.Equal("Map of Paris", card.Caption);
        Assert.Equal("Map of Paris", action.Result?.Result?.ToString());
        Assert.Equal(2, model.Requests.Count);
        ToolHistory.AssertValid(model.Requests[1]);
        Assert.Equal("Map of Paris", ToolHistory.FindResult(model.Requests[1], "map-1"));
    }

    [Fact]
    public async Task CustomHandlerForUIActionName_RestoredActionShowsTheCardWithItsResult()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, ShowMapCall()));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        void Configure(UIAgentOptions options)
        {
            options.AddBlockHandler(new MapCardHandler());
            options.RegisterUIAction(CreateShowMap([]));
            options.Thread = thread;
        }

        using (var live = new AgentContext(new UIAgent(model, Configure)))
            await live.SendMessageAsync("Show Paris");

        using var restored = new AgentContext(new UIAgent(model, Configure));
        await restored.RestoreAsync();

        var action = Assert.Single(Assert.Single(restored.Turns).ResponseBlocks.OfType<UIActionBlock>());
        var card = Assert.IsType<MapCard>(action.InnerBlock);
        Assert.Equal("Paris", card.City);
        Assert.Equal("Map of Paris", card.Caption);
    }

    [Fact]
    public async Task GeneratedToolBlockForUIActionName_ActionRunsAndMapsTheTypedResult()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(
            ct,
            new FunctionCallContent("route-1", "show_route", new Dictionary<string, object?> { ["to"] = "Rome" })));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        var routes = new List<string>();
        using var context = new AgentContext(new UIAgent(model, options =>
        {
            options.AddGeneratedToolBlocks();
            options.RegisterUIAction(AIFunctionFactory.Create(
                (string to) =>
                {
                    routes.Add(to);
                    return $"Route to {to}";
                },
                "show_route",
                "Shows a route."));
        }));

        await context.SendMessageAsync("Route to Rome");

        Assert.Equal(["Rome"], routes);
        var action = Assert.Single(Assert.Single(context.Turns).ResponseBlocks.OfType<UIActionBlock>());
        var block = Assert.IsType<GeneratedRouteBlock>(action.InnerBlock);
        Assert.Equal("Rome", block.Destination);
        Assert.Equal("Route to Rome", block.Route);
        ToolHistory.AssertValid(model.Requests[1]);
    }

    [Fact]
    public async Task CustomHandlerForApprovalRequiredUIAction_ApprovalShowsTheCardAndTheActionRuns()
    {
        var shown = new List<string>();
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, ShowMapCall()));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = new AgentContext(new UIAgent(new FunctionInvokingChatClient(model), options =>
        {
            options.AddBlockHandler(new MapCardHandler());
            options.RegisterUIAction(new ApprovalRequiredAIFunction(CreateShowMap(shown)));
        }));
        context.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                context.Turns[^1].ResponseBlocks.OfType<ToolApprovalBlock>().Single().Approve();
        });

        await context.SendMessageAsync("Show Paris");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["Paris"], shown);
        var approval = Assert.Single(Assert.Single(context.Turns).ResponseBlocks.OfType<ToolApprovalBlock>());
        var card = Assert.IsType<MapCard>(approval.InnerBlock);
        Assert.Equal("Map of Paris", card.Caption);
        ToolHistory.AssertValid(model.Requests[1]);
    }

    [Fact]
    public async Task CustomHandlerForManualUIAction_InvokingTheActionMapsItsResultIntoTheCard()
    {
        var shown = new List<string>();
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, ShowMapCall()));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = new AgentContext(new UIAgent(model, options =>
        {
            options.AddBlockHandler(new MapCardHandler());
            options.RegisterUIAction(CreateShowMap(shown), UIActionInvocationMode.Manual);
        }));
        MapCard? pendingCard = null;
        context.OnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;

            var action = context.Turns[^1].ResponseBlocks.OfType<UIActionBlock>().Single();
            pendingCard = Assert.IsType<MapCard>(action.InnerBlock);
            Assert.Null(pendingCard.Caption);
            _ = action.InvokeAsync();
        });

        await context.SendMessageAsync("Show Paris");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["Paris"], shown);
        Assert.NotNull(pendingCard);
        Assert.Equal("Map of Paris", pendingCard.Caption);
        Assert.Equal("Map of Paris", ToolHistory.FindResult(model.Requests[1], "map-1"));
    }

    [Fact]
    public async Task HandlerForUIActionNameThatIsNotAFunctionBlock_ActionShowsThePlainCall()
    {
        var shown = new List<string>();
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, ShowMapCall()));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = new AgentContext(new UIAgent(model, options =>
        {
            options.AddBlockHandler(new BannerHandler());
            options.RegisterUIAction(CreateShowMap(shown));
        }));

        await context.SendMessageAsync("Show Paris");

        Assert.Equal(["Paris"], shown);
        var turn = Assert.Single(context.Turns);
        Assert.DoesNotContain(turn.ResponseBlocks, block => block is Banner);
        var action = Assert.Single(turn.ResponseBlocks.OfType<UIActionBlock>());
        var inner = Assert.IsType<FunctionInvocationContentBlock>(action.InnerBlock);
        Assert.Equal(ChatRole.Assistant, inner.Role);
        Assert.Equal("Map of Paris", inner.Result?.Result?.ToString());
        ToolHistory.AssertValid(model.Requests[1]);
    }

    [Fact]
    public async Task CardThatFailsToShowTheResult_TheModelStillGetsTheResult()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitMultipleToolCallResponse(ct, ShowMapCall()));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Done.", ct));
        using var context = new AgentContext(new UIAgent(model, options =>
        {
            options.AddBlockHandler(new MapCardHandler(failOnResult: true));
            options.RegisterUIAction(CreateShowMap([]));
        }));

        await context.SendMessageAsync("Show Paris");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        var action = Assert.Single(Assert.Single(context.Turns).ResponseBlocks.OfType<UIActionBlock>());
        Assert.Equal("Map of Paris", Assert.IsAssignableFrom<FunctionInvocationContentBlock>(action.InnerBlock).Result?.Result?.ToString());
        Assert.Equal("Map of Paris", ToolHistory.FindResult(model.Requests[1], "map-1"));
    }

    private static FunctionCallContent ShowMapCall()
        => new("map-1", "ShowMap", new Dictionary<string, object?> { ["city"] = "Paris" });

    private static AIFunction CreateShowMap(List<string> shown)
        => AIFunctionFactory.Create(
            (string city) =>
            {
                shown.Add(city);
                return $"Map of {city}";
            },
            "ShowMap",
            "Shows a map.");

    private sealed class MapCard : FunctionInvocationContentBlock
    {
        public string City { get; set; } = string.Empty;

        public string? Caption { get; set; }
    }

    private sealed class MapCardHandler(bool failOnResult = false) : ContentBlockHandler<MapCard>
    {
        public override BlockMappingResult<MapCard> Handle(BlockMappingContext context, MapCard state)
        {
            if (state.Call is null)
            {
                foreach (var content in context.UnhandledContents)
                {
                    if (content is FunctionCallContent { Name: "ShowMap" } call)
                    {
                        context.MarkHandled(call);
                        state.Call = call;
                        state.City = call.Arguments is { } arguments
                            && arguments.TryGetValue("city", out var city)
                                ? city?.ToString() ?? string.Empty
                                : string.Empty;
                        return BlockMappingResult<MapCard>.Emit(state, state);
                    }
                }

                return BlockMappingResult<MapCard>.Pass();
            }

            foreach (var content in context.UnhandledContents)
            {
                if (content is FunctionResultContent result && result.CallId == state.Call.CallId)
                {
                    if (failOnResult)
                        throw new InvalidOperationException("The card cannot show this result.");

                    context.MarkHandled(result);
                    state.Result = result;
                    state.Caption = result.Result?.ToString();
                    return BlockMappingResult<MapCard>.Complete();
                }
            }

            return BlockMappingResult<MapCard>.Pass();
        }
    }

    private sealed class Banner : ContentBlock
    {
    }

    private sealed class BannerHandler : ContentBlockHandler<Banner>
    {
        public override BlockMappingResult<Banner> Handle(BlockMappingContext context, Banner state)
        {
            foreach (var content in context.UnhandledContents)
            {
                if (content is FunctionCallContent { Name: "ShowMap" } call)
                {
                    context.MarkHandled(call);
                    state.Id = call.CallId;
                    return BlockMappingResult<Banner>.Emit(state, state);
                }
            }

            return BlockMappingResult<Banner>.Pass();
        }
    }
}

[ToolBlock("show_route")]
public sealed partial class GeneratedRouteBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "to")]
    public string Destination { get; set; } = string.Empty;

    [ToolResult]
    public string? Route { get; set; }
}
