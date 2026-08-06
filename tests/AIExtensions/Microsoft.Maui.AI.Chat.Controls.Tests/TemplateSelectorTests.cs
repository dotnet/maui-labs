using Microsoft.Maui.AI.Chat;
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
    public void SelectTemplate_ToolCallBlock_MatchesFunctionInvocationTemplate()
    {
        var selector = CreateDefaultSelector();
        var context = BlockFactory.MakeToolCall("get_weather");

        var template = selector.SelectTemplate(context, null!);

        Assert.NotNull(template);
    }

    [Fact]
    public void SelectTemplate_ToolResultBlock_MatchesFunctionInvocationTemplate()
    {
        var selector = CreateDefaultSelector();
        var context = BlockFactory.MakeToolResult("get_weather", "Sunny");

        var template = selector.SelectTemplate(context, null!);

        Assert.NotNull(template);
    }

    [Fact]
    public void SelectTemplate_UnknownBlock_RendersNothing()
    {
        var selector = new ContentTemplateSelector();
        // Empty selector with no templates — an unmatched block renders nothing.
        var context = BlockFactory.MakeText("Assistant", "Hello");

        var template = selector.SelectTemplate(context, null!);

        AssertRendersNothing(template);
    }

    [Fact]
    public void SelectTemplate_BlockWithNoMatchingTemplate_RendersNothing()
    {
        // Only a text template is registered; a tool-call block has no match, so it renders
        // nothing (templates are the allow-list — omitting FunctionInvocationTemplate hides tools).
        var selector = new ContentTemplateSelector();
        selector.Templates.Add(new TextContentTemplate { ViewType = typeof(Label) });

        var toolCall = BlockFactory.MakeToolCall("get_weather");
        var template = selector.SelectTemplate(toolCall, null!);

        AssertRendersNothing(template);
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
        var genericTool = new FunctionInvocationTemplate { ViewType = typeof(Label) };
        var weatherTool = new FunctionInvocationTemplate { ToolName = "get_weather", ViewType = typeof(Entry) };

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
    public void SelectTemplate_NonContentContext_RendersNothing()
    {
        var selector = CreateDefaultSelector();

        // Passing a non-ContentContext object renders nothing
        var template = selector.SelectTemplate("not a ContentContext", null!);

        AssertRendersNothing(template);
    }

    [Fact]
    public void SelectTemplate_NullItem_RendersNothing()
    {
        var selector = CreateDefaultSelector();

        var template = selector.SelectTemplate(null!, null!);

        AssertRendersNothing(template);
    }

    private static void AssertRendersNothing(DataTemplate template)
    {
        Assert.NotNull(template);
        var view = Assert.IsType<ContentView>(template.CreateContent());
        Assert.False(view.IsVisible);
    }

    [Fact]
    public void Templates_CanBeAddedDynamically()
    {
        var selector = new ContentTemplateSelector();

        Assert.Empty(selector.Templates);

        selector.Templates.Add(new TextContentTemplate { ViewType = typeof(Label) });
        selector.Templates.Add(new FunctionInvocationTemplate { ViewType = typeof(Label) });

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

    [Fact]
    public void MessageListView_ZeroConfiguration_ProvidesVisibleBuiltInFallbacks()
    {
        var view = new MessageListView();
        var selector = view.CreateTemplateSelector();
        var session = SessionFactory.Create();

        Assert.IsType<ChatMessageView>(
            selector.SelectTemplate(BlockFactory.MakeText("User", "Hello"), null!).CreateContent());
        Assert.IsType<ChatMessageView>(
            selector.SelectTemplate(BlockFactory.MakeText("Assistant", "Hi"), null!).CreateContent());
        Assert.IsType<ToolApprovalView>(
            selector.SelectTemplate(BlockFactory.MakeApproval("delete_file"), null!).CreateContent());
        Assert.IsType<MediaContentView>(
            selector.SelectTemplate(BlockFactory.MakeMedia(), null!).CreateContent());
        Assert.IsType<ThinkingView>(
            selector.SelectTemplate(
                new ContentContext(session, new ThinkingContentBlock(), view),
                null!).CreateContent());
        Assert.IsType<ErrorMessageView>(
            selector.SelectTemplate(
                new ContentContext(session, new ErrorContentBlock("error"), view),
                null!).CreateContent());
    }

    [Fact]
    public void MessageListView_ZeroConfiguration_HidesRawFunctionInvocationsAndUnknownBlocks()
    {
        var view = new MessageListView();
        var selector = view.CreateTemplateSelector();
        var session = SessionFactory.Create();

        AssertRendersNothing(selector.SelectTemplate(BlockFactory.MakeToolCall("get_weather"), null!));
        AssertRendersNothing(selector.SelectTemplate(
            new ContentContext(session, new UnknownContentBlock(), view),
            null!));
    }

    [Fact]
    public void ConsumerTemplate_AlwaysOutranksBuiltInFallback()
    {
        var view = new MessageListView();
        view.ContentTemplates.Add(new CustomTextTemplate { Priority = -20_000 });

        var block = new CustomTextBlock();
        block.AppendText("custom");
        block.Role = ChatRole.Assistant;
        var context = new ContentContext(SessionFactory.Create(), block, view);

        var selected = view.CreateTemplateSelector().SelectTemplate(context, null!);

        Assert.IsType<Editor>(selected.CreateContent());
    }

    [Fact]
    public void UseDefaultContentTemplatesFalse_RestoresStrictAllowListRendering()
    {
        var view = new MessageListView { UseDefaultContentTemplates = false };
        var context = BlockFactory.MakeText("Assistant", "Hello");

        AssertRendersNothing(view.CreateTemplateSelector().SelectTemplate(context, null!));

        view.ContentTemplates.Add(new TextContentTemplate
        {
            ViewType = typeof(Label),
            Priority = -20_000,
        });

        var selected = view.CreateTemplateSelector().SelectTemplate(context, null!);
        Assert.IsType<Label>(selected.CreateContent());
    }

    /// <summary>
    /// Creates a selector with all standard templates registered (mirrors the default
    /// CopilotChatView template configuration).
    /// </summary>
    private static ContentTemplateSelector CreateDefaultSelector()
    {
        var selector = new ContentTemplateSelector();
        selector.Templates.Add(new TextContentTemplate { ViewType = typeof(Label) });
        selector.Templates.Add(new FunctionInvocationTemplate { ViewType = typeof(Label) });
        selector.Templates.Add(new MediaContentTemplate { ViewType = typeof(Label) });
        selector.Templates.Add(new DefaultContentTemplate { ViewType = typeof(Label) });
        return selector;
    }

    private sealed class UnknownContentBlock : ContentBlock;

    private sealed class CustomTextBlock : TextContentBlock;

    private sealed class CustomTextTemplate : ContentTemplate
    {
        public CustomTextTemplate() => ViewType = typeof(Editor);

        public override bool When(ContentContext context) => context.Block is CustomTextBlock;
    }
}
