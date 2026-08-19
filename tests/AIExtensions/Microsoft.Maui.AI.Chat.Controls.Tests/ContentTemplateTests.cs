using Microsoft.Maui.AI.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Controls;
using Microsoft.Maui.Chat.Controls;

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
        var block = new TextContentBlock();
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
        block.AddContent(new DataContent(
            new byte[] { 1, 2, 3 },
            "image/png"));
        var ctx = CreateAgentContext();
        return new ContentContext(ctx, block);
    }

    // ── TextContentTemplate ──

    [Fact]
    public void TextContentTemplate_MatchesTextContentBlock()
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

    // ── FunctionInvocationTemplate ──

    [Fact]
    public void FunctionInvocationTemplate_MatchesFunctionInvocationWithNoResult()
    {
        var template = new FunctionInvocationTemplate();
        var context = MakeFunctionCallContext();
        Assert.True(template.When(context));
    }

    [Fact]
    public void FunctionInvocationTemplate_MatchesFunctionInvocationWithResult()
    {
        var template = new FunctionInvocationTemplate();
        var context = MakeFunctionResultContext();
        Assert.True(template.When(context));
    }

    [Fact]
    public void FunctionInvocationTemplate_DoesNotMatchTextContent()
    {
        var template = new FunctionInvocationTemplate();
        var context = MakeTextContext("Assistant");
        Assert.False(template.When(context));
    }

    [Fact]
    public void FunctionInvocationTemplate_WithToolName_FiltersCorrectly()
    {
        var weatherTemplate = new FunctionInvocationTemplate { ToolName = "get_weather" };

        var weatherCall = MakeFunctionCallContext("get_weather");
        var weatherResult = MakeFunctionResultContext("get_weather");
        var calcContext = MakeFunctionCallContext("calculate");

        Assert.True(weatherTemplate.When(weatherCall));
        Assert.True(weatherTemplate.When(weatherResult));
        Assert.False(weatherTemplate.When(calcContext));
    }

    [Fact]
    public void FunctionInvocationTemplate_ToolNameSpecific_HasHigherPriority()
    {
        var generic = new FunctionInvocationTemplate();
        var specific = new FunctionInvocationTemplate { ToolName = "get_weather" };

        var context = MakeFunctionCallContext("get_weather");

        Assert.True(specific.GetPriority(context) > generic.GetPriority(context));
    }

    // ── DefaultContentTemplate ──

    [Fact]
    public void DefaultContentTemplate_MatchesOnlyNonCanonicalContent()
    {
        var template = new DefaultContentTemplate();

        Assert.False(template.When(MakeTextContext("User")));
        Assert.True(template.When(MakeFunctionCallContext()));
        Assert.True(template.When(MakeFunctionResultContext()));
        Assert.False(template.When(MakeMediaContext()));
    }

    [Fact]
    public void DefaultContentTemplate_HasLowestPriority()
    {
        var textTemplate = new TextContentTemplate();
        var defaultTemplate = new DefaultContentTemplate();

        var context = MakeTextContext("User");

        Assert.True(defaultTemplate.GetPriority(context) < textTemplate.GetPriority(context));
    }

    [Fact]
    public void CustomAiBody_UsesNeutralMessageChromeByDefault()
    {
        var template = new TextContentTemplate
        {
            ViewType = typeof(Label),
        };

        Assert.IsAssignableFrom<ChatBubbleView>(
            template.GetTemplate().CreateContent());
    }

    [Fact]
    public void CustomAiBody_CanOwnTheEntireRow()
    {
        var template = new TextContentTemplate
        {
            ViewType = typeof(Label),
            UseMessageChrome = false,
        };

        Assert.IsType<Label>(template.GetTemplate().CreateContent());
    }

    // ── ContentContext ──

    [Fact]
    public void ContentContext_ExposesBlockProperties()
    {
        var block = new TextContentBlock();
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
        var genericResult = new FunctionInvocationTemplate();
        var specificResult = new FunctionInvocationTemplate { ToolName = "get_weather" };

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
