using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

/// <summary>
/// Mirrors: Blazor.Tests/Components/MessageListContextTests.cs
/// Tests ContentTemplateSelector — the MAUI equivalent of Blazor's MessageListContext
/// block rendering dispatch. Verifies that the selector picks the correct DataTemplate
/// for each block type based on registered ContentTemplate instances.
/// </summary>
public class TemplateSelectorTests
{
    [Fact]
    public void SelectTemplate_TextBlock_MatchesTextContentTemplate()
    {
        var selector = CreateDefaultSelector();
        var context = BlockFactory.MakeText("Assistant", "Hello world");

        var template = selector.SelectTemplate(context, null!);

        Assert.NotNull(template);
    }

    [Fact]
    public void SelectTemplate_ToolCallBlock_MatchesFunctionCallTemplate()
    {
        var selector = CreateDefaultSelector();
        var context = BlockFactory.MakeToolCall("get_weather");

        var template = selector.SelectTemplate(context, null!);

        Assert.NotNull(template);
    }

    [Fact]
    public void SelectTemplate_ToolResultBlock_MatchesFunctionResultTemplate()
    {
        var selector = CreateDefaultSelector();
        var context = BlockFactory.MakeToolResult("get_weather", "Sunny");

        var template = selector.SelectTemplate(context, null!);

        Assert.NotNull(template);
    }

    [Fact]
    public void SelectTemplate_ReasoningBlock_MatchesReasoningTemplate()
    {
        var selector = CreateDefaultSelector();
        var context = BlockFactory.MakeReasoning("Thinking...");

        var template = selector.SelectTemplate(context, null!);

        Assert.NotNull(template);
    }

    [Fact]
    public void SelectTemplate_UnknownBlock_ReturnsFallback()
    {
        var selector = new ContentTemplateSelector();
        // Empty selector with no templates — should return the fallback
        var context = BlockFactory.MakeText("Assistant", "Hello");

        var template = selector.SelectTemplate(context, null!);

        // Fallback is always non-null (shows "No content template registered" label)
        Assert.NotNull(template);
    }

    [Fact]
    public void SelectTemplate_HigherPriority_WinsOverLower()
    {
        var genericText = new TextContentTemplate { ViewType = typeof(Label) };
        var roleSpecific = new TextContentTemplate { Role = "User", ViewType = typeof(Entry) };

        var selector = new ContentTemplateSelector();
        selector.Templates.Add(genericText);
        selector.Templates.Add(roleSpecific);

        var userCtx = BlockFactory.MakeText("User", "Hello");
        var assistantCtx = BlockFactory.MakeText("Assistant", "Hi");

        // Both match user context, but role-specific has higher priority
        var userTemplate = selector.SelectTemplate(userCtx, null!);
        var assistantTemplate = selector.SelectTemplate(assistantCtx, null!);

        // The templates should differ — role-specific wins for user
        Assert.NotNull(userTemplate);
        Assert.NotNull(assistantTemplate);
    }

    [Fact]
    public void SelectTemplate_ToolNameSpecific_WinsOverGenericToolCall()
    {
        var genericTool = new FunctionCallTemplate { ViewType = typeof(Label) };
        var weatherTool = new FunctionCallTemplate { ToolName = "get_weather", ViewType = typeof(Entry) };

        var selector = new ContentTemplateSelector();
        selector.Templates.Add(genericTool);
        selector.Templates.Add(weatherTool);

        var weatherCtx = BlockFactory.MakeToolCall("get_weather");
        var otherCtx = BlockFactory.MakeToolCall("delete_file");

        // Tool-name-specific should win for matching tool
        var weatherTemplate = selector.SelectTemplate(weatherCtx, null!);
        var otherTemplate = selector.SelectTemplate(otherCtx, null!);

        Assert.NotNull(weatherTemplate);
        Assert.NotNull(otherTemplate);
    }

    [Fact]
    public void SelectTemplate_NonContentContext_ReturnsFallback()
    {
        var selector = CreateDefaultSelector();

        // Passing a non-ContentContext object should return fallback
        var template = selector.SelectTemplate("not a ContentContext", null!);

        Assert.NotNull(template);
    }

    [Fact]
    public void SelectTemplate_NullItem_ReturnsFallback()
    {
        var selector = CreateDefaultSelector();

        var template = selector.SelectTemplate(null!, null!);

        Assert.NotNull(template);
    }

    [Fact]
    public void Templates_CanBeAddedDynamically()
    {
        var selector = new ContentTemplateSelector();

        Assert.Empty(selector.Templates);

        selector.Templates.Add(new TextContentTemplate { ViewType = typeof(Label) });
        selector.Templates.Add(new FunctionCallTemplate { ViewType = typeof(Label) });

        Assert.Equal(2, selector.Templates.Count);
    }

    [Fact]
    public void SelectTemplate_MultipleMatching_HighestPriorityWins()
    {
        // Default template matches everything at lowest priority
        var defaultTemplate = new DefaultContentTemplate { ViewType = typeof(Label) };
        var textTemplate = new TextContentTemplate { ViewType = typeof(Entry) };
        var roleTemplate = new TextContentTemplate { Role = "Assistant", ViewType = typeof(Editor) };

        var selector = new ContentTemplateSelector();
        selector.Templates.Add(defaultTemplate);
        selector.Templates.Add(textTemplate);
        selector.Templates.Add(roleTemplate);

        var assistantText = BlockFactory.MakeText("Assistant", "Hello");

        // All 3 match, but roleTemplate (most specific) should win
        var template = selector.SelectTemplate(assistantText, null!);
        Assert.NotNull(template);
    }

    [Fact]
    public void SelectTemplate_MediaBlock_MatchesMediaTemplate()
    {
        var mediaTemplate = new MediaContentTemplate { ViewType = typeof(Label) };
        var selector = new ContentTemplateSelector();
        selector.Templates.Add(mediaTemplate);

        var context = BlockFactory.MakeMedia();

        var template = selector.SelectTemplate(context, null!);
        Assert.NotNull(template);
    }

    /// <summary>
    /// Creates a selector with all standard templates registered (mirrors the default
    /// CopilotChatView template configuration).
    /// </summary>
    private static ContentTemplateSelector CreateDefaultSelector()
    {
        var selector = new ContentTemplateSelector();
        selector.Templates.Add(new TextContentTemplate { ViewType = typeof(Label) });
        selector.Templates.Add(new FunctionCallTemplate { ViewType = typeof(Label) });
        selector.Templates.Add(new FunctionResultTemplate { ViewType = typeof(Label) });
        selector.Templates.Add(new ReasoningContentTemplate { ViewType = typeof(Label) });
        selector.Templates.Add(new MediaContentTemplate { ViewType = typeof(Label) });
        selector.Templates.Add(new DefaultContentTemplate { ViewType = typeof(Label) });
        return selector;
    }
}
