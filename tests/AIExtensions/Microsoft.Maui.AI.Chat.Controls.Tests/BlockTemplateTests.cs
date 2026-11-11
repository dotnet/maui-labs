namespace Microsoft.Maui.AI.Chat.Controls.Tests;

/// <summary>
/// How the chat view picks a block's template: the app's <see cref="AIChatView.ContentTemplates"/> first, in order,
/// then the built-in views. Matching itself is covered by <see cref="BlockMatchTests"/>.
/// </summary>
public class BlockTemplateTests
{
    [Fact]
    public void Select_ATemplateWithoutWhen_MatchesEveryBlock()
    {
        var mine = new BlockTemplate { Template = new DataTemplate(() => new Label()) };

        Assert.Same(mine, BuiltInBlockTemplates.Select(Blocks.Text("hi"), [mine], includeBuiltIns: true));
        Assert.Same(mine, BuiltInBlockTemplates.Select(Blocks.ToolCall("search"), [mine], includeBuiltIns: true));
        Assert.Same(mine, BuiltInBlockTemplates.Select(Blocks.Reasoning("why"), [mine], includeBuiltIns: true));
    }

    [Fact]
    public void Select_AppTemplatesAlwaysBeatBuiltIns()
    {
        var mine = new BlockTemplate { When = BlockMatch.Is<ContentBlock>(), Template = new DataTemplate(() => new Label()) };

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

        Assert.All(blocks, block => Assert.NotNull(BuiltInBlockTemplates.Select(block, null, includeBuiltIns: true)?.Template));
    }

    [Fact]
    public void Select_TheBuiltInsAreAFallbackSelector()
    {
        var text = BuiltInBlockTemplates.Selector.SelectTemplate(Blocks.Text("hi"), new ContentView());
        var rich = BuiltInBlockTemplates.Selector.SelectTemplate(new RichContentBlock(), new ContentView());

        Assert.IsType<TextBlockView>(text.CreateContent());
        Assert.IsType<RichTextBlockView>(rich.CreateContent());
    }

    [Fact]
    public void Select_TheFirstMatchingAppTemplateWins()
    {
        var tool = new BlockTemplate { When = BlockMatch.Tool("search"), Template = new DataTemplate(() => new Label()) };
        var any = new BlockTemplate { When = BlockMatch.Is<ContentBlock>(), Template = new DataTemplate(() => new Label()) };

        Assert.Same(tool, BuiltInBlockTemplates.Select(Blocks.ToolCall("search"), [tool, any], includeBuiltIns: true));
        Assert.Same(any, BuiltInBlockTemplates.Select(Blocks.ToolCall("search"), [any, tool], includeBuiltIns: true));
    }

    [Fact]
    public void Select_IgnoresNullEntries()
    {
        var mine = new BlockTemplate { Template = new DataTemplate(() => new Label()) };

        Assert.Same(mine, BuiltInBlockTemplates.Select(Blocks.Text("hi"), [null!, mine, null!], includeBuiltIns: true));
    }

    [Fact]
    public void Select_GeneratedToolBlockType_MatchesItsTypedTemplate()
    {
        var typed = new BlockTemplate { When = BlockMatch.Is<WeatherBlock>(), Template = new DataTemplate(() => new Label()) };
        var generic = new BlockTemplate { When = BlockMatch.Is<FunctionInvocationContentBlock>(), Template = new DataTemplate(() => new Label()) };
        var weather = new WeatherBlock { Call = new FunctionCallContent("1", "get_weather") };

        Assert.Same(typed, BuiltInBlockTemplates.Select(weather, [typed, generic], includeBuiltIns: true));
        Assert.Same(generic, BuiltInBlockTemplates.Select(Blocks.ToolCall("search"), [typed, generic], includeBuiltIns: true));
    }

    [Fact]
    public void Template_WithoutAView_HidesTheBlocksItMatches()
    {
        var hide = new BlockTemplate { When = BlockMatch.Is<ReasoningContentBlock>() };

        var selected = BuiltInBlockTemplates.Select(Blocks.Reasoning("why"), [hide], includeBuiltIns: true);

        Assert.Same(hide, selected);
        Assert.Null(selected!.Template);
    }

    [Fact]
    public void CreateView_FromAnInlineTemplate()
    {
        var template = new BlockTemplate { Template = new DataTemplate(() => new Label { Text = "inline" }) };

        var view = Assert.IsType<Label>(BuiltInBlockTemplates.CreateView(template, Blocks.Text("hi"), new ContentView()));

        Assert.Equal("inline", view.Text);
    }

    [Fact]
    public void CreateView_FromAViewTemplate()
    {
        var template = new BlockTemplate { Template = new ViewTemplate(typeof(CountingBlockView)) };

        Assert.IsType<CountingBlockView>(BuiltInBlockTemplates.CreateView(template, Blocks.Text("hi"), new ContentView()));
    }

    [Fact]
    public void CreateView_EvaluatesATemplateSelectorAgainstTheBlock()
    {
        var template = new BlockTemplate { Template = new FailedSelector() };

        var failed = BuiltInBlockTemplates.CreateView(template, Blocks.ToolCall("x", exception: new InvalidOperationException()), new ContentView());
        var ok = BuiltInBlockTemplates.CreateView(template, Blocks.ToolCall("x", result: "ok"), new ContentView());

        Assert.Equal("failed", Assert.IsType<Label>(failed).Text);
        Assert.Equal("ok", Assert.IsType<Label>(ok).Text);
    }

    [Fact]
    public void CreateView_TemplateThatIsNotAView_Throws()
    {
        var template = new BlockTemplate { Template = new DataTemplate(() => new Span()) };

        var error = Assert.Throws<InvalidOperationException>(() => BuiltInBlockTemplates.CreateView(template, Blocks.Text("hi"), new ContentView()));
        Assert.Contains("must create a View", error.Message);
    }

    [Fact]
    public void RowTemplate_IsSharedUntilTheTemplateShowsAnotherView() => UIThread.Run(async () =>
    {
        var client = new ScriptedChatClient().Reply("One").Reply("Two");
        var template = new BlockTemplate { When = BlockMatch.Role(ChatRole.Assistant), Template = new DataTemplate(() => new Label()) };
        var view = ChatHost.Create(ChatHost.Session(client), v => v.ContentTemplates.Add(template));
        await view.Session!.SendMessageAsync("first");
        await view.Session.SendMessageAsync("second");

        var rows = view.Rows().OfType<BlockRow>().ToArray();
        Assert.Equal(2, rows.Length);
        var first = RowTemplate(view, rows[0]);
        Assert.Same(first, RowTemplate(view, rows[1]));

        template.Template = new DataTemplate(() => new Button());
        var edited = view.Rows().OfType<BlockRow>().ToArray();
        Assert.NotSame(rows[0], edited[0]);
        Assert.NotSame(first, RowTemplate(view, edited[0]));
        Assert.Same(RowTemplate(view, edited[0]), RowTemplate(view, edited[1]));
    });

    private static DataTemplate RowTemplate(AIChatView view, ChatRow row) =>
        ((DataTemplateSelector)view.MessageList.ItemTemplate!).SelectTemplate(row, view.MessageList);

    private sealed class FailedSelector : DataTemplateSelector
    {
        private readonly DataTemplate _failed = new(() => new Label { Text = "failed" });
        private readonly DataTemplate _ok = new(() => new Label { Text = "ok" });

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
            item is FunctionInvocationContentBlock { Result.Exception: not null } ? _failed : _ok;
    }
}
