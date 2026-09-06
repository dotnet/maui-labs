using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

/// <summary>
/// Mirrors: Blazor.Tests/Components/BlockRendererTests.cs
/// Tests that ContentTemplateSelector correctly resolves templates for each block type,
/// and that the CopilotChatView's ContentContext properly exposes block properties.
/// </summary>
public class BlockRendererTests
{
    // ── Template resolution by block type ──

    [Fact]
    public void TextTemplate_MatchesUserRichContentBlock()
    {
        var template = new TextContentTemplate { ViewType = typeof(Label) };
        var context = BlockFactory.MakeText("User", "Hello");

        Assert.True(template.When(context));
    }

    [Fact]
    public void TextTemplate_MatchesAssistantRichContentBlock()
    {
        var template = new TextContentTemplate { ViewType = typeof(Label) };
        var context = BlockFactory.MakeText("Assistant", "Hi there");

        Assert.True(template.When(context));
    }

    [Fact]
    public void TextTemplate_WithRole_OnlyMatchesThatRole()
    {
        var userTemplate = new TextContentTemplate { Role = "User", ViewType = typeof(Label) };
        var assistantTemplate = new TextContentTemplate { Role = "Assistant", ViewType = typeof(Label) };

        var userCtx = BlockFactory.MakeText("User", "Hello");
        var assistantCtx = BlockFactory.MakeText("Assistant", "Hi");

        Assert.True(userTemplate.When(userCtx));
        Assert.False(userTemplate.When(assistantCtx));
        Assert.True(assistantTemplate.When(assistantCtx));
        Assert.False(assistantTemplate.When(userCtx));
    }

    [Fact]
    public void FunctionCallTemplate_MatchesPendingToolCall()
    {
        var template = new FunctionCallTemplate { ViewType = typeof(Label) };
        var context = BlockFactory.MakeToolCall("get_weather");

        Assert.True(template.When(context));
    }

    [Fact]
    public void FunctionCallTemplate_DoesNotMatchCompletedCall()
    {
        var template = new FunctionCallTemplate { ViewType = typeof(Label) };
        var context = BlockFactory.MakeToolResult("get_weather", "Sunny");

        Assert.False(template.When(context));
    }

    [Fact]
    public void FunctionResultTemplate_MatchesCompletedToolCall()
    {
        var template = new FunctionResultTemplate { ViewType = typeof(Label) };
        var context = BlockFactory.MakeToolResult("get_weather", "Sunny 72°F");

        Assert.True(template.When(context));
    }

    [Fact]
    public void FunctionResultTemplate_DoesNotMatchPendingCall()
    {
        var template = new FunctionResultTemplate { ViewType = typeof(Label) };
        var context = BlockFactory.MakeToolCall("get_weather");

        Assert.False(template.When(context));
    }

    [Fact]
    public void ReasoningTemplate_MatchesReasoningBlock()
    {
        var template = new ReasoningContentTemplate { ViewType = typeof(Label) };
        var context = BlockFactory.MakeReasoning("Let me think about this...");

        Assert.True(template.When(context));
    }

    [Fact]
    public void ReasoningTemplate_DoesNotMatchTextBlock()
    {
        var template = new ReasoningContentTemplate { ViewType = typeof(Label) };
        var context = BlockFactory.MakeText("Assistant", "Not reasoning");

        Assert.False(template.When(context));
    }

    [Fact]
    public void ToolApprovalTemplate_MatchesApprovalBlock()
    {
        var template = new ToolApprovalTemplate { ViewType = typeof(Label) };
        var context = BlockFactory.MakeApproval("delete_file");

        Assert.True(template.When(context));
    }

    // ── Tool-name-specific overrides ──

    [Fact]
    public void FunctionCallTemplate_WithToolName_OnlyMatchesThatTool()
    {
        var weatherTemplate = new FunctionCallTemplate { ToolName = "get_weather", ViewType = typeof(Label) };
        var calcTemplate = new FunctionCallTemplate { ToolName = "calculate", ViewType = typeof(Label) };

        var weatherCtx = BlockFactory.MakeToolCall("get_weather");
        var calcCtx = BlockFactory.MakeToolCall("calculate");

        Assert.True(weatherTemplate.When(weatherCtx));
        Assert.False(weatherTemplate.When(calcCtx));
        Assert.True(calcTemplate.When(calcCtx));
        Assert.False(calcTemplate.When(weatherCtx));
    }

    [Fact]
    public void FunctionResultTemplate_WithToolName_OnlyMatchesThatTool()
    {
        var weatherResult = new FunctionResultTemplate { ToolName = "get_weather", ViewType = typeof(Label) };

        var weatherCtx = BlockFactory.MakeToolResult("get_weather", "Sunny");
        var otherCtx = BlockFactory.MakeToolResult("calculate", "42");

        Assert.True(weatherResult.When(weatherCtx));
        Assert.False(weatherResult.When(otherCtx));
    }

    // ── Priority resolution ──

    [Fact]
    public void ToolSpecific_BeatGeneric_BeatDefault()
    {
        var defaultTemplate = new DefaultContentTemplate { ViewType = typeof(Label) };
        var genericCall = new FunctionCallTemplate { ViewType = typeof(Label) };
        var specificCall = new FunctionCallTemplate { ToolName = "get_weather", ViewType = typeof(Label) };

        var context = BlockFactory.MakeToolCall("get_weather");

        var defaultPriority = defaultTemplate.GetPriority(context);
        var genericPriority = genericCall.GetPriority(context);
        var specificPriority = specificCall.GetPriority(context);

        Assert.True(specificPriority > genericPriority, "Tool-specific should beat generic");
        Assert.True(genericPriority > defaultPriority, "Generic should beat default");
    }

    [Fact]
    public void RoleSpecific_BeatsGenericText()
    {
        var generic = new TextContentTemplate { ViewType = typeof(Label) };
        var userSpecific = new TextContentTemplate { Role = "User", ViewType = typeof(Label) };

        var context = BlockFactory.MakeText("User", "Hello");

        Assert.True(userSpecific.GetPriority(context) > generic.GetPriority(context));
    }

    // ── Inline template via ComponentBlockRenderer equivalent ──

    [Fact]
    public async Task ToolCallBlock_ExposesCallDetails_ViaContentContext()
    {
        int callCount = 0;
        var client = new TestChatClient((msgs, _, _) =>
        {
            callCount++;
            if (callCount == 1)
            {
                // First call: return a function call
                var response = new ChatResponse([new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent("call1", "get_weather",
                        new Dictionary<string, object?> { ["location"] = "Seattle" })])]);
                return Task.FromResult(response);
            }
            // Second call (after tool invocation): return final text
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "It's sunny!")]));
        });
        var agent = new UIAgent(client, new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string location) => "Sunny, 72°F", "get_weather")]
        });
        var session = new AgentContext(agent);
        var blocks = new List<ContentBlock>();
        session.RegisterOnBlockAdded((_, b) => blocks.Add(b));

        await session.SendMessageAsync("What's the weather in Seattle?");

        var toolBlock = blocks.OfType<FunctionInvocationContentBlock>().FirstOrDefault();
        Assert.NotNull(toolBlock);
        Assert.Equal("get_weather", toolBlock.Call?.Name);

        // ContentContext wraps this correctly
        var ctx = new ContentContext(session, toolBlock);
        Assert.Equal("get_weather", ctx.ToolName);
        Assert.False(ctx.IsUser);
        Assert.True(ctx.IsAssistant);
    }
}
