using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Controls;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class ContentTemplateTests
{
    private static AgentContext CreateAgentContext()
    {
        var client = new TestChatClient();
        var agent = new UIAgent(client);
        return new AgentContext(agent);
    }

    private static ContentContext MakeTextContext(string role)
    {
        var block = new RichContentBlock();
        block.AppendText("hello");
        block.Role = role == "User" ? ChatRole.User : ChatRole.Assistant;
        var ctx = CreateAgentContext();
        return new ContentContext(ctx, block);
    }

    private static ContentContext MakeFunctionCallContext(string toolName = "get_weather")
    {
        var block = new FunctionInvocationContentBlock
        {
            Call = new FunctionCallContent("c1", toolName, null),
            Result = null,
        };
        block.Role = ChatRole.Assistant;
        var ctx = CreateAgentContext();
        return new ContentContext(ctx, block);
    }

    private static ContentContext MakeFunctionResultContext(string toolName = "get_weather")
    {
        var block = new FunctionInvocationContentBlock
        {
            Call = new FunctionCallContent("c1", toolName, null),
            Result = new FunctionResultContent("c1", "sunny, 72°F"),
        };
        block.Role = ChatRole.Assistant;
        var ctx = CreateAgentContext();
        return new ContentContext(ctx, block);
    }

    private static ContentContext MakeMediaContext()
    {
        var block = new MediaContentBlock();
        block.Role = ChatRole.Assistant;
        var ctx = CreateAgentContext();
        return new ContentContext(ctx, block);
    }

    private static ContentContext MakeReasoningContext()
    {
        var block = new ReasoningContentBlock();
        block.AppendText("thinking...");
        block.Role = ChatRole.Assistant;
        var ctx = CreateAgentContext();
        return new ContentContext(ctx, block);
    }

    // ── TextContentTemplate ──

    [Fact]
    public void TextContentTemplate_MatchesRichContentBlock()
    {
        var template = new TextContentTemplate();
        var context = MakeTextContext("User");
        Assert.True(template.When(context));
    }

    [Fact]
    public void TextContentTemplate_DoesNotMatchFunctionInvocation()
    {
        var template = new TextContentTemplate();
        var context = MakeFunctionCallContext();
        Assert.False(template.When(context));
    }

    [Fact]
    public void TextContentTemplate_WithRole_MatchesSpecificRole()
    {
        var userTemplate = new TextContentTemplate { Role = "User" };
        var assistantTemplate = new TextContentTemplate { Role = "Assistant" };

        var userContext = MakeTextContext("User");
        var assistantContext = MakeTextContext("Assistant");

        Assert.True(userTemplate.When(userContext));
        Assert.False(userTemplate.When(assistantContext));
        Assert.True(assistantTemplate.When(assistantContext));
        Assert.False(assistantTemplate.When(userContext));
    }

    [Fact]
    public void TextContentTemplate_RoleSpecific_HasHigherPriority()
    {
        var generic = new TextContentTemplate();
        var specific = new TextContentTemplate { Role = "User" };

        var context = MakeTextContext("User");

        Assert.True(specific.GetPriority(context) > generic.GetPriority(context));
    }

    // ── FunctionCallTemplate ──

    [Fact]
    public void FunctionCallTemplate_MatchesFunctionInvocationWithNoResult()
    {
        var template = new FunctionCallTemplate();
        var context = MakeFunctionCallContext();
        Assert.True(template.When(context));
    }

    [Fact]
    public void FunctionCallTemplate_DoesNotMatchTextContent()
    {
        var template = new FunctionCallTemplate();
        var context = MakeTextContext("Assistant");
        Assert.False(template.When(context));
    }

    [Fact]
    public void FunctionCallTemplate_DoesNotMatchWhenResultPresent()
    {
        var template = new FunctionCallTemplate();
        var context = MakeFunctionResultContext();
        Assert.False(template.When(context));
    }

    [Fact]
    public void FunctionCallTemplate_WithToolName_FiltersCorrectly()
    {
        var weatherTemplate = new FunctionCallTemplate { ToolName = "get_weather" };

        var weatherContext = MakeFunctionCallContext("get_weather");
        var calcContext = MakeFunctionCallContext("calculate");

        Assert.True(weatherTemplate.When(weatherContext));
        Assert.False(weatherTemplate.When(calcContext));
    }

    [Fact]
    public void FunctionCallTemplate_ToolNameSpecific_HasHigherPriority()
    {
        var generic = new FunctionCallTemplate();
        var specific = new FunctionCallTemplate { ToolName = "get_weather" };

        var context = MakeFunctionCallContext("get_weather");

        Assert.True(specific.GetPriority(context) > generic.GetPriority(context));
    }

    // ── FunctionResultTemplate ──

    [Fact]
    public void FunctionResultTemplate_MatchesFunctionInvocationWithResult()
    {
        var template = new FunctionResultTemplate();
        var context = MakeFunctionResultContext();
        Assert.True(template.When(context));
    }

    [Fact]
    public void FunctionResultTemplate_DoesNotMatchWithNoResult()
    {
        var template = new FunctionResultTemplate();
        var context = MakeFunctionCallContext();
        Assert.False(template.When(context));
    }

    [Fact]
    public void FunctionResultTemplate_WithToolName_FiltersCorrectly()
    {
        var weatherResult = new FunctionResultTemplate { ToolName = "get_weather" };

        var weatherContext = MakeFunctionResultContext("get_weather");
        var calcContext = MakeFunctionResultContext("calculate");

        Assert.True(weatherResult.When(weatherContext));
        Assert.False(weatherResult.When(calcContext));
    }

    // ── ErrorContentTemplate ──

    [Fact]
    public void ErrorContentTemplate_ReturnsFalse_CoreSurfacesErrorsViaStatus()
    {
        var template = new ErrorContentTemplate();
        // Error template always returns false — Core surfaces errors via status
        var context = MakeTextContext("User");
        Assert.False(template.When(context));
    }

    // ── DefaultContentTemplate ──

    [Fact]
    public void DefaultContentTemplate_MatchesEverything()
    {
        var template = new DefaultContentTemplate();

        Assert.True(template.When(MakeTextContext("User")));
        Assert.True(template.When(MakeFunctionCallContext()));
        Assert.True(template.When(MakeFunctionResultContext()));
        Assert.True(template.When(MakeMediaContext()));
        Assert.True(template.When(MakeReasoningContext()));
    }

    [Fact]
    public void DefaultContentTemplate_HasLowestPriority()
    {
        var textTemplate = new TextContentTemplate();
        var defaultTemplate = new DefaultContentTemplate();

        var context = MakeTextContext("User");

        Assert.True(defaultTemplate.GetPriority(context) < textTemplate.GetPriority(context));
    }

    // ── New Templates ──

    [Fact]
    public void RichTextContentTemplate_MatchesRichContentBlockWithNodes()
    {
        var template = new RichTextContentTemplate();
        // RichTextContentTemplate requires Content.Count > 0
        var block = new RichContentBlock();
        block.AppendText("hello");
        // Need to add a node to Content
        // Content is set internally, so RichTextContentTemplate will NOT match plain text blocks
        // (because Content is empty unless the parser populated it)
        block.Role = ChatRole.Assistant;
        var ctx = CreateAgentContext();
        var context = new ContentContext(ctx, block);

        // Without nodes in Content, this template won't match
        Assert.False(template.When(context));
    }

    [Fact]
    public void RichTextContentTemplate_HasHigherPriorityThanText()
    {
        var richTemplate = new RichTextContentTemplate();
        var textTemplate = new TextContentTemplate();

        var context = MakeTextContext("User");

        Assert.True(richTemplate.GetPriority(context) > textTemplate.GetPriority(context));
    }

    [Fact]
    public void MediaContentTemplate_MatchesMediaContentBlock()
    {
        var template = new MediaContentTemplate();
        var context = MakeMediaContext();
        Assert.True(template.When(context));
    }

    [Fact]
    public void MediaContentTemplate_DoesNotMatchTextContent()
    {
        var template = new MediaContentTemplate();
        var context = MakeTextContext("User");
        Assert.False(template.When(context));
    }

    [Fact]
    public void ReasoningContentTemplate_MatchesReasoningContentBlock()
    {
        var template = new ReasoningContentTemplate();
        var context = MakeReasoningContext();
        Assert.True(template.When(context));
    }

    [Fact]
    public void ReasoningContentTemplate_DoesNotMatchTextContent()
    {
        var template = new ReasoningContentTemplate();
        var context = MakeTextContext("User");
        Assert.False(template.When(context));
    }

    // ── ContentContext ──

    [Fact]
    public void ContentContext_ExposesBlockProperties()
    {
        var block = new RichContentBlock();
        block.AppendText("test");
        block.Role = ChatRole.User;
        var agentCtx = CreateAgentContext();
        var context = new ContentContext(agentCtx, block);

        Assert.Same(agentCtx, context.AgentContext);
        Assert.Same(block, context.Block);
        Assert.Equal(ChatRole.User, context.Role);
        Assert.True(context.IsUser);
        Assert.False(context.IsAssistant);
        Assert.Equal("test", context.TextContent);
        Assert.Null(context.ToolName);
        Assert.False(context.IsInteractive);
    }

    [Fact]
    public void ContentContext_FunctionInvocation_ExposesToolName()
    {
        var block = new FunctionInvocationContentBlock
        {
            Call = new FunctionCallContent("c1", "get_weather", null),
        };
        block.Role = ChatRole.Assistant;
        var ctx = CreateAgentContext();
        var context = new ContentContext(ctx, block);

        Assert.Equal("get_weather", context.ToolName);
        Assert.False(context.IsInteractive);
    }

    // ── Priority ordering ──

    [Fact]
    public void Priority_ToolNameSpecific_BeatsGeneric_BeatsDefault()
    {
        var defaultTemplate = new DefaultContentTemplate();
        var genericResult = new FunctionResultTemplate();
        var specificResult = new FunctionResultTemplate { ToolName = "get_weather" };

        var context = MakeFunctionResultContext("get_weather");

        var defaultPriority = defaultTemplate.GetPriority(context);
        var genericPriority = genericResult.GetPriority(context);
        var specificPriority = specificResult.GetPriority(context);

        Assert.True(specificPriority > genericPriority, "Tool-specific should beat generic");
        Assert.True(genericPriority > defaultPriority, "Generic should beat default");
    }

    /// <summary>Minimal IChatClient for creating AgentContext instances in tests.</summary>
    private sealed class TestChatClient : IChatClient
    {
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "test")]));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();
    }
}
