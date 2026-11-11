using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class BlockTemplateTests
{
    [Fact]
    public void When_WithNoFilters_MatchesEveryBlock()
    {
        var template = new BlockTemplate();

        Assert.True(template.When(Blocks.Text("hi")));
        Assert.True(template.When(Blocks.ToolCall("search")));
        Assert.True(template.When(Blocks.Reasoning("why")));
    }

    [Fact]
    public void When_BlockType_MatchesDerivedTypes()
    {
        var template = new BlockTemplate { BlockType = typeof(RichContentBlock) };

        Assert.True(template.When(Blocks.Text("hi")));
        Assert.False(template.When(Blocks.ToolCall("search")));
    }

    [Fact]
    public void When_ToolName_MatchesCallsApprovalsAndActionsCaseInsensitively()
    {
        var template = new BlockTemplate { ToolName = "GET_WEATHER" };

        Assert.True(template.When(Blocks.ToolCall("get_weather")));
        Assert.True(template.When(Blocks.ToolCall("get_weather", result: "sunny")));
        Assert.True(template.When(Blocks.Approval("get_weather")));
        Assert.True(template.When(Blocks.UIAction("get_weather", UIActionInvocationMode.Manual)));
        Assert.False(template.When(Blocks.ToolCall("search")));
        Assert.False(template.When(Blocks.Text("get_weather")));
    }

    [Fact]
    public void When_Role_MatchesTheBlockRole()
    {
        var template = new BlockTemplate { Role = "User" };

        Assert.True(template.When(Blocks.Text("hi", ChatRole.User)));
        Assert.False(template.When(Blocks.Text("hi", ChatRole.Assistant)));
    }

    [Fact]
    public void When_FiltersCombineWithAnd()
    {
        var template = new BlockTemplate { BlockType = typeof(FunctionInvocationContentBlock), ToolName = "search", Role = "assistant" };

        Assert.True(template.When(Blocks.ToolCall("search")));
        Assert.False(template.When(Blocks.ToolCall("other")));
        Assert.False(template.When(Blocks.Approval("search")));
    }

    [Fact]
    public void When_CanBeOverridden()
    {
        var template = new FailedToolTemplate();

        Assert.True(template.When(Blocks.ToolCall("search", exception: new InvalidOperationException("boom"))));
        Assert.False(template.When(Blocks.ToolCall("search", result: "ok")));
        Assert.False(template.When(Blocks.Text("hi")));
    }

    [Fact]
    public void When_WithNullBlock_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new BlockTemplate().When(null!));

    [Fact]
    public void Select_AppTemplatesAlwaysBeatBuiltIns()
    {
        var mine = new BlockTemplate { BlockType = typeof(ContentBlock), Priority = -100, ViewType = typeof(Label) };

        Assert.Same(mine, BuiltInBlockTemplates.Select(Blocks.Text("hi"), [mine], includeBuiltIns: true));
    }

    [Fact]
    public void Select_WithoutAppTemplates_UsesTheBuiltIns()
    {
        Assert.Same(BuiltInBlockTemplates.All[0], BuiltInBlockTemplates.Select(Blocks.Text("hi"), [], includeBuiltIns: true));
        Assert.Null(BuiltInBlockTemplates.Select(Blocks.Text("hi"), [], includeBuiltIns: false));
    }

    [Fact]
    public void Select_EveryBuiltInBlockTypeHasAView()
    {
        ContentBlock[] blocks =
        [
            Blocks.Text("hi"),
            new RichContentBlock(),
            Blocks.ToolCall("search"),
            Blocks.Approval("search"),
            Blocks.UIAction("search", UIActionInvocationMode.Automatic),
            Blocks.Reasoning("why"),
            Blocks.Media(Blocks.Png()),
        ];

        Assert.All(blocks, block => Assert.True(BuiltInBlockTemplates.Select(block, null, includeBuiltIns: true)?.CanRender));
    }

    [Fact]
    public void Select_HighestPriorityWins()
    {
        var low = new BlockTemplate { ToolName = "search", ViewType = typeof(Label) };
        var high = new BlockTemplate { BlockType = typeof(ContentBlock), Priority = 1, ViewType = typeof(Label) };

        Assert.Same(high, BuiltInBlockTemplates.Select(Blocks.ToolCall("search"), [low, high], includeBuiltIns: true));
    }

    [Fact]
    public void Select_AtEqualPriority_TheMostSpecificWins()
    {
        var any = new BlockTemplate { ViewType = typeof(Label) };
        var role = new BlockTemplate { Role = "assistant", ViewType = typeof(Label) };
        var baseType = new BlockTemplate { BlockType = typeof(ContentBlock), ViewType = typeof(Label) };
        var derived = new BlockTemplate { BlockType = typeof(FunctionInvocationContentBlock), ViewType = typeof(Label) };
        var tool = new BlockTemplate { ToolName = "search", ViewType = typeof(Label) };
        var block = Blocks.ToolCall("search");

        Assert.Same(tool, BuiltInBlockTemplates.Select(block, [tool, derived, baseType, role, any], includeBuiltIns: true));
        Assert.Same(derived, BuiltInBlockTemplates.Select(block, [derived, baseType, role, any], includeBuiltIns: true));
        Assert.Same(baseType, BuiltInBlockTemplates.Select(block, [baseType, role, any], includeBuiltIns: true));
        Assert.Same(role, BuiltInBlockTemplates.Select(block, [role, any], includeBuiltIns: true));
    }

    [Fact]
    public void Select_WithEqualRank_TheLastDeclaredWins()
    {
        var first = new BlockTemplate { BlockType = typeof(TextContentBlock), ViewType = typeof(Label) };
        var second = new BlockTemplate { BlockType = typeof(TextContentBlock), ViewType = typeof(Label) };

        Assert.Same(second, BuiltInBlockTemplates.Select(Blocks.Text("hi"), [first, second], includeBuiltIns: true));
    }

    [Fact]
    public void Select_IgnoresNullEntries()
    {
        var mine = new BlockTemplate { ViewType = typeof(Label) };

        Assert.Same(mine, BuiltInBlockTemplates.Select(Blocks.Text("hi"), [null!, mine, null!], includeBuiltIns: true));
    }

    [Fact]
    public void Select_GeneratedToolBlockType_MatchesItsTypedTemplate()
    {
        var generic = new BlockTemplate { BlockType = typeof(FunctionInvocationContentBlock), ViewType = typeof(Label) };
        var typed = new BlockTemplate { BlockType = typeof(WeatherBlock), ViewType = typeof(Label) };
        var weather = new WeatherBlock { Call = new FunctionCallContent("1", "get_weather") };

        Assert.Same(typed, BuiltInBlockTemplates.Select(weather, [typed, generic], includeBuiltIns: true));
        Assert.Same(generic, BuiltInBlockTemplates.Select(Blocks.ToolCall("search"), [typed, generic], includeBuiltIns: true));
    }

    [Fact]
    public void Template_WithoutAView_HidesTheBlocksItMatches()
    {
        var hide = new BlockTemplate { BlockType = typeof(ReasoningContentBlock) };

        var selected = BuiltInBlockTemplates.Select(Blocks.Reasoning("why"), [hide], includeBuiltIns: true);

        Assert.Same(hide, selected);
        Assert.False(selected!.CanRender);
    }

    [Fact]
    public void CreateView_FromAnInlineTemplate()
    {
        var template = new BlockTemplate { Template = new DataTemplate(() => new Label { Text = "inline" }) };

        var view = Assert.IsType<Label>(template.CreateView(Blocks.Text("hi"), new ContentView(), services: null));

        Assert.Equal("inline", view.Text);
    }

    [Fact]
    public void CreateView_EvaluatesATemplateSelectorAgainstTheBlock()
    {
        var template = new BlockTemplate { Template = new FailedSelector() };

        var failed = template.CreateView(Blocks.ToolCall("x", exception: new InvalidOperationException()), new ContentView(), null);
        var ok = template.CreateView(Blocks.ToolCall("x", result: "ok"), new ContentView(), null);

        Assert.Equal("failed", Assert.IsType<Label>(failed).Text);
        Assert.Equal("ok", Assert.IsType<Label>(ok).Text);
    }

    [Fact]
    public void CreateView_ViewTypeWinsOverTheInlineTemplate()
    {
        var template = new BlockTemplate { ViewType = typeof(Button), Template = new DataTemplate(() => new Label()) };

        Assert.IsType<Button>(template.CreateView(Blocks.Text("hi"), new ContentView(), null));
    }

    [Fact]
    public void CreateView_TemplateThatIsNotAView_Throws()
    {
        var template = new BlockTemplate { Template = new DataTemplate(() => new Span()) };

        var error = Assert.Throws<InvalidOperationException>(() => template.CreateView(Blocks.Text("hi"), new ContentView(), null));
        Assert.Contains("must create a View", error.Message);
    }

    [Fact]
    public void CreateView_WithServices_ResolvesConstructorDependencies()
    {
        var services = new ServiceCollection().AddSingleton<GreetingService>().BuildServiceProvider();
        var template = new BlockTemplate { ViewType = typeof(ServiceBackedView) };

        var view = Assert.IsType<ServiceBackedView>(template.CreateView(Blocks.Text("hi"), new ContentView(), services));

        Assert.Same(services.GetRequiredService<GreetingService>(), view.Service);
    }

    [Fact]
    public void CreateView_WithServicesMissingADependency_ExplainsWhy()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(() => BlockTemplate.CreateView(typeof(ServiceBackedView), services));

        Assert.Contains("Register the view", error.Message);
    }

    [Fact]
    public void CreateView_WithoutServicesOrAParameterlessConstructor_ExplainsWhy()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BlockTemplate.CreateView(typeof(ServiceBackedView)));

        Assert.Contains("no public parameterless constructor", error.Message);
    }

    [Fact]
    public void CreateView_WithANonViewType_Throws() =>
        Assert.Throws<InvalidOperationException>(() => BlockTemplate.CreateView(typeof(string)));

    [Fact]
    public void CreateView_WithNullType_Throws() =>
        Assert.Throws<ArgumentNullException>(() => BlockTemplate.CreateView(null!));

    [Fact]
    public void RowTemplate_IsCachedUntilTheViewChanges()
    {
        var template = new BlockTemplate { ViewType = typeof(Label) };

        var first = template.GetDataTemplate();
        Assert.Same(first, template.GetDataTemplate());

        template.ViewType = typeof(Button);
        var second = template.GetDataTemplate();
        Assert.NotSame(first, second);
        Assert.Same(second, template.GetDataTemplate());

        template.Template = new DataTemplate(() => new Label());
        Assert.NotSame(second, template.GetDataTemplate());
    }

    [Fact]
    public void Template_IsTheContentProperty()
    {
        var attribute = typeof(BlockTemplate).GetCustomAttributes(typeof(ContentPropertyAttribute), inherit: true)
            .Cast<ContentPropertyAttribute>()
            .Single();

        Assert.Equal(nameof(BlockTemplate.Template), attribute.Name);
    }

    private sealed class FailedToolTemplate : BlockTemplate
    {
        public FailedToolTemplate() => BlockType = typeof(FunctionInvocationContentBlock);

        public override bool When(ContentBlock block) =>
            base.When(block) && block is FunctionInvocationContentBlock { Result.Exception: not null };
    }

    private sealed class FailedSelector : DataTemplateSelector
    {
        private readonly DataTemplate _failed = new(() => new Label { Text = "failed" });
        private readonly DataTemplate _ok = new(() => new Label { Text = "ok" });

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
            item is FunctionInvocationContentBlock { Result.Exception: not null } ? _failed : _ok;
    }
}
