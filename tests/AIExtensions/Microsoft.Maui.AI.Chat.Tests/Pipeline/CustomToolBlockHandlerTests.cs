// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

/// <summary>
/// Verifies the hand-written custom tool-block pattern: a
/// <see cref="ContentBlockHandler{TState}"/> that projects raw
/// Microsoft.Extensions.AI function-call/result content into a strongly-typed
/// block (mirrors the sample's WeatherToolBlock/WeatherToolBlockHandler).
/// </summary>
public class CustomToolBlockHandlerTests
{
    private const string ToolName = "GetCurrentWeather";

    private sealed class WeatherBlock : FunctionInvocationContentBlock
    {
        public string? City { get; set; }
        public string? Location { get; set; }
        public int Temperature { get; set; }
        public string? Conditions { get; set; }
    }

    private sealed class WeatherHandler : ContentBlockHandler<WeatherBlock>
    {
        public override BlockMappingResult<WeatherBlock> Handle(
            BlockMappingContext context, WeatherBlock state)
        {
            if (state.Call is null)
            {
                foreach (var content in context.UnhandledContents)
                {
                    if (content is FunctionCallContent call && call.Name == ToolName)
                    {
                        context.MarkHandled(call);
                        state.Call = call;
                        state.Id = call.CallId;
                        if (call.Arguments is { } args && args.TryGetValue("city", out var city) && city is not null)
                        {
                            state.City = city switch
                            {
                                JsonElement je => je.GetString(),
                                string s => s,
                                _ => city.ToString(),
                            };
                        }
                        return BlockMappingResult<WeatherBlock>.Emit(state, state);
                    }
                }
            }

            foreach (var content in context.UnhandledContents)
            {
                if (content is FunctionResultContent result
                    && state.Call is not null
                    && result.CallId == state.Call.CallId)
                {
                    context.MarkHandled(result);
                    state.Result = result;

                    var json = result.Result?.ToString();
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("location", out var loc))
                            state.Location = loc.GetString();
                        if (root.TryGetProperty("temperature", out var temp))
                            state.Temperature = temp.GetInt32();
                        if (root.TryGetProperty("conditions", out var cond))
                            state.Conditions = cond.GetString();
                    }

                    return BlockMappingResult<WeatherBlock>.Complete();
                }
            }

            return BlockMappingResult<WeatherBlock>.Pass();
        }
    }

    private static BlockMappingPipeline CreatePipeline()
    {
        var options = new UIAgentOptions();
        options.AddBlockHandler(new WeatherHandler());
        return new BlockMappingPipeline(options);
    }

    private static async Task<List<ContentBlock>> CollectBlocks(
        BlockMappingPipeline pipeline, ChatResponseUpdate update)
    {
        var blocks = new List<ContentBlock>();
        await foreach (var block in pipeline.Process(update))
        {
            blocks.Add(block);
        }
        return blocks;
    }

    [Fact]
    public async Task FunctionCall_MatchingToolName_EmitsTypedBlock()
    {
        var pipeline = CreatePipeline();
        var args = new Dictionary<string, object?>
        {
            ["city"] = JsonSerializer.SerializeToElement("Tokyo")
        };
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new FunctionCallContent("call-1", ToolName, args)],
            FinishReason = ChatFinishReason.ToolCalls
        };

        var blocks = await CollectBlocks(pipeline, update);

        var block = Assert.IsType<WeatherBlock>(Assert.Single(blocks));
        Assert.Equal("Tokyo", block.City);
        Assert.Equal("call-1", block.Id);
        Assert.False(block.HasResult);
        Assert.Equal(BlockLifecycleState.Active, block.LifecycleState);
    }

    [Fact]
    public async Task FunctionResult_MatchingCallId_PopulatesTypedPropertiesAndCompletes()
    {
        var pipeline = CreatePipeline();
        var args = new Dictionary<string, object?>
        {
            ["city"] = JsonSerializer.SerializeToElement("Tokyo")
        };
        var callUpdate = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new FunctionCallContent("call-1", ToolName, args)],
            FinishReason = ChatFinishReason.ToolCalls
        };
        var block = Assert.IsType<WeatherBlock>(Assert.Single(await CollectBlocks(pipeline, callUpdate)));

        var resultJson = """{ "location": "Tokyo", "temperature": 23, "conditions": "Snowy" }""";
        var resultUpdate = new ChatResponseUpdate
        {
            Contents = [new FunctionResultContent("call-1", resultJson)]
        };
        await CollectBlocks(pipeline, resultUpdate);

        Assert.True(block.HasResult);
        Assert.Equal("Tokyo", block.Location);
        Assert.Equal(23, block.Temperature);
        Assert.Equal("Snowy", block.Conditions);
        Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState);
    }

    [Fact]
    public async Task FunctionCall_NonMatchingToolName_DoesNotEmitTypedBlock()
    {
        var pipeline = CreatePipeline();
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new FunctionCallContent("call-1", "SomeOtherTool", new Dictionary<string, object?>())],
            FinishReason = ChatFinishReason.ToolCalls
        };

        var blocks = await CollectBlocks(pipeline, update);

        Assert.DoesNotContain(blocks, b => b is WeatherBlock);
    }
}
