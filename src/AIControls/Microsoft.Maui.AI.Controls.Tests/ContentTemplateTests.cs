using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Controls.Chat;

namespace Microsoft.Maui.AI.Controls.Tests;

public class ContentTemplateTests
{
    private static ChatSession CreateSession() =>
        new([], new TestChatClient());

    private static ContentContext MakeContext(AIContent content, ContentRole role, string? toolName = null, ToolApprovalState approval = ToolApprovalState.None) =>
        new(CreateSession(), new ChatEntry(
            Guid.NewGuid().ToString("n"), content, role,
            DateTimeOffset.UtcNow, toolName, approval));

    // ── TextContentTemplate ──

    [Fact]
    public void TextContentTemplate_MatchesTextContent()
    {
        var template = new TextContentTemplate();
        var context = MakeContext(new TextContent("hello"), ContentRole.User);
        Assert.True(template.When(context));
    }

    [Fact]
    public void TextContentTemplate_DoesNotMatchFunctionCall()
    {
        var template = new TextContentTemplate();
        var context = MakeContext(
            new FunctionCallContent("c1", "test", null), ContentRole.Tool);
        Assert.False(template.When(context));
    }

    [Fact]
    public void TextContentTemplate_WithRole_MatchesSpecificRole()
    {
        var userTemplate = new TextContentTemplate { Role = ContentRole.User };
        var assistantTemplate = new TextContentTemplate { Role = ContentRole.Assistant };

        var userContext = MakeContext(new TextContent("hello"), ContentRole.User);
        var assistantContext = MakeContext(new TextContent("hi"), ContentRole.Assistant);

        Assert.True(userTemplate.When(userContext));
        Assert.False(userTemplate.When(assistantContext));
        Assert.True(assistantTemplate.When(assistantContext));
        Assert.False(assistantTemplate.When(userContext));
    }

    [Fact]
    public void TextContentTemplate_RoleSpecific_HasHigherPriority()
    {
        var generic = new TextContentTemplate();
        var specific = new TextContentTemplate { Role = ContentRole.User };

        var context = MakeContext(new TextContent("hello"), ContentRole.User);

        Assert.True(specific.GetPriority(context) > generic.GetPriority(context));
    }

    // ── FunctionCallTemplate ──

    [Fact]
    public void FunctionCallTemplate_MatchesFunctionCallContent()
    {
        var template = new FunctionCallTemplate();
        var context = MakeContext(
            new FunctionCallContent("c1", "get_weather", null), ContentRole.Tool, "get_weather");
        Assert.True(template.When(context));
    }

    [Fact]
    public void FunctionCallTemplate_DoesNotMatchTextContent()
    {
        var template = new FunctionCallTemplate();
        var context = MakeContext(new TextContent("hi"), ContentRole.Assistant);
        Assert.False(template.When(context));
    }

    [Fact]
    public void FunctionCallTemplate_WithToolName_FiltersCorrectly()
    {
        var weatherTemplate = new FunctionCallTemplate { ToolName = "get_weather" };

        var weatherContext = MakeContext(
            new FunctionCallContent("c1", "get_weather", null), ContentRole.Tool, "get_weather");
        var calcContext = MakeContext(
            new FunctionCallContent("c2", "calculate", null), ContentRole.Tool, "calculate");

        Assert.True(weatherTemplate.When(weatherContext));
        Assert.False(weatherTemplate.When(calcContext));
    }

    [Fact]
    public void FunctionCallTemplate_ToolNameSpecific_HasHigherPriority()
    {
        var generic = new FunctionCallTemplate();
        var specific = new FunctionCallTemplate { ToolName = "get_weather" };

        var context = MakeContext(
            new FunctionCallContent("c1", "get_weather", null), ContentRole.Tool, "get_weather");

        Assert.True(specific.GetPriority(context) > generic.GetPriority(context));
    }

    // ── FunctionResultTemplate ──

    [Fact]
    public void FunctionResultTemplate_MatchesFunctionResultContent()
    {
        var template = new FunctionResultTemplate();
        var context = MakeContext(
            new FunctionResultContent("c1", "result data"), ContentRole.Tool, "get_weather");
        Assert.True(template.When(context));
    }

    [Fact]
    public void FunctionResultTemplate_WithToolName_FiltersCorrectly()
    {
        var weatherResult = new FunctionResultTemplate { ToolName = "get_weather" };

        var weatherContext = MakeContext(
            new FunctionResultContent("c1", "sunny"), ContentRole.Tool, "get_weather");
        var calcContext = MakeContext(
            new FunctionResultContent("c2", "42"), ContentRole.Tool, "calculate");

        Assert.True(weatherResult.When(weatherContext));
        Assert.False(weatherResult.When(calcContext));
    }

    // ── ToolApprovalTemplate ──

    [Fact]
    public void ToolApprovalTemplate_MatchesToolApprovalRequest()
    {
        var template = new ToolApprovalTemplate();
        var fc = new FunctionCallContent("c1", "add_plant", null);
        var request = new ToolApprovalRequestContent("req1", fc);
        var context = MakeContext(request, ContentRole.Approval, "add_plant", ToolApprovalState.Pending);
        Assert.True(template.When(context));
    }

    [Fact]
    public void ToolApprovalTemplate_WithToolName_FiltersCorrectly()
    {
        var plantApproval = new ToolApprovalTemplate { ToolName = "add_plant" };

        var plantContext = MakeContext(
            new ToolApprovalRequestContent("req1", new FunctionCallContent("c1", "add_plant", null)),
            ContentRole.Approval, "add_plant", ToolApprovalState.Pending);
        var otherContext = MakeContext(
            new ToolApprovalRequestContent("req2", new FunctionCallContent("c2", "delete_plant", null)),
            ContentRole.Approval, "delete_plant", ToolApprovalState.Pending);

        Assert.True(plantApproval.When(plantContext));
        Assert.False(plantApproval.When(otherContext));
    }

    [Fact]
    public void ToolApprovalTemplate_DoesNotMatchTextContent()
    {
        var template = new ToolApprovalTemplate();
        var context = MakeContext(new TextContent("hello"), ContentRole.User);
        Assert.False(template.When(context));
    }

    // ── ErrorContentTemplate ──

    [Fact]
    public void ErrorContentTemplate_MatchesErrorContent()
    {
        var template = new ErrorContentTemplate();
        var context = MakeContext(new ErrorContent("something went wrong"), ContentRole.Error);
        Assert.True(template.When(context));
    }

    [Fact]
    public void ErrorContentTemplate_DoesNotMatchTextContent()
    {
        var template = new ErrorContentTemplate();
        var context = MakeContext(new TextContent("hello"), ContentRole.User);
        Assert.False(template.When(context));
    }

    // ── DefaultContentTemplate ──

    [Fact]
    public void DefaultContentTemplate_MatchesEverything()
    {
        var template = new DefaultContentTemplate();

        Assert.True(template.When(MakeContext(new TextContent("hi"), ContentRole.User)));
        Assert.True(template.When(MakeContext(new FunctionCallContent("c1", "test", null), ContentRole.Tool)));
        Assert.True(template.When(MakeContext(new ErrorContent("err"), ContentRole.Error)));
    }

    [Fact]
    public void DefaultContentTemplate_HasLowestPriority()
    {
        var textTemplate = new TextContentTemplate();
        var defaultTemplate = new DefaultContentTemplate();

        var context = MakeContext(new TextContent("hello"), ContentRole.User);

        Assert.True(defaultTemplate.GetPriority(context) < textTemplate.GetPriority(context));
    }

    // ── ContentContext ──

    [Fact]
    public void ContentContext_ExposesEntryProperties()
    {
        var session = CreateSession();
        var entry = new ChatEntry("id1", new TextContent("test"), ContentRole.User,
            DateTimeOffset.UtcNow, null, ToolApprovalState.None);
        var context = new ContentContext(session, entry);

        Assert.Same(session, context.Session);
        Assert.Same(entry, context.Entry);
        Assert.IsType<TextContent>(context.Content);
        Assert.Equal(ContentRole.User, context.Role);
        Assert.Null(context.ToolName);
        Assert.Equal(ToolApprovalState.None, context.ApprovalState);
        Assert.False(context.ApprovalResolved);
        Assert.Null(context.ApprovalResolutionText);
    }

    [Fact]
    public void ContentContext_ApprovalResolved_TrueForApprovedOrRejected()
    {
        var session = CreateSession();
        var fc = new FunctionCallContent("c1", "test", null);
        var approvedEntry = new ChatEntry("id1",
            new ToolApprovalRequestContent("req1", fc),
            ContentRole.Approval, DateTimeOffset.UtcNow, "test", ToolApprovalState.Approved);
        var rejectedEntry = approvedEntry with { ApprovalState = ToolApprovalState.Rejected };
        var pendingEntry = approvedEntry with { ApprovalState = ToolApprovalState.Pending };

        Assert.True(new ContentContext(session, approvedEntry).ApprovalResolved);
        Assert.True(new ContentContext(session, rejectedEntry).ApprovalResolved);
        Assert.False(new ContentContext(session, pendingEntry).ApprovalResolved);
    }

    [Fact]
    public void ContentContext_ApprovalResolutionText_ReturnsCorrectText()
    {
        var session = CreateSession();
        var fc = new FunctionCallContent("c1", "add_plant", null);
        var entry = new ChatEntry("id1",
            new ToolApprovalRequestContent("req1", fc),
            ContentRole.Approval, DateTimeOffset.UtcNow, "add_plant", ToolApprovalState.Approved);
        var context = new ContentContext(session, entry);

        Assert.Equal("Approved - add_plant", context.ApprovalResolutionText);

        var rejected = new ContentContext(session, entry with { ApprovalState = ToolApprovalState.Rejected });
        Assert.Equal("Rejected - add_plant", rejected.ApprovalResolutionText);
    }

    // ── Priority ordering ──

    [Fact]
    public void Priority_ToolNameSpecific_BeatsGeneric_BeatDefault()
    {
        var defaultTemplate = new DefaultContentTemplate();
        var genericResult = new FunctionResultTemplate();
        var specificResult = new FunctionResultTemplate { ToolName = "get_weather" };

        var context = MakeContext(
            new FunctionResultContent("c1", "data"), ContentRole.Tool, "get_weather");

        var defaultPriority = defaultTemplate.GetPriority(context);
        var genericPriority = genericResult.GetPriority(context);
        var specificPriority = specificResult.GetPriority(context);

        Assert.True(specificPriority > genericPriority, "Tool-specific should beat generic");
        Assert.True(genericPriority > defaultPriority, "Generic should beat default");
    }

    /// <summary>Minimal IChatClient for creating ChatSession instances in tests.</summary>
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
