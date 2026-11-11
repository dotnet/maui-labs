namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class BlockTemplateSelectorTests
{
    [Fact]
    public void SelectTemplate_TheFirstMatchingTemplateWins()
    {
        var search = new DataTemplate(() => new Label { Text = "search" });
        var call = new DataTemplate(() => new Label { Text = "call" });
        var any = new DataTemplate(() => new Label { Text = "any" });
        var selector = new BlockTemplateSelector
        {
            Templates =
            {
                new BlockTemplate { When = BlockMatch.Tool("search"), Template = search },
                new BlockTemplate { When = BlockMatch.Is<FunctionInvocationContentBlock>(), Template = call },
                new BlockTemplate { Template = any },
            },
        };

        Assert.Same(search, Select(selector, Blocks.ToolCall("search")));
        Assert.Same(call, Select(selector, Blocks.ToolCall("other")));
        Assert.Same(any, Select(selector, Blocks.Text("hi")));
    }

    [Fact]
    public void SelectTemplate_OrderDecidesNotSpecificity()
    {
        var call = new DataTemplate(() => new Label());
        var search = new DataTemplate(() => new Label());
        var selector = new BlockTemplateSelector
        {
            Templates =
            {
                new BlockTemplate { When = BlockMatch.Is<FunctionInvocationContentBlock>(), Template = call },
                new BlockTemplate { When = BlockMatch.Tool("search"), Template = search },
            },
        };

        Assert.Same(call, Select(selector, Blocks.ToolCall("search")));
    }

    [Fact]
    public void SelectTemplate_ReturnsTheSameTemplateEveryTime()
    {
        var text = new DataTemplate(() => new Label());
        var selector = new BlockTemplateSelector { Templates = { new BlockTemplate { When = BlockMatch.Is<TextContentBlock>(), Template = text } } };

        Assert.Same(Select(selector, Blocks.Text("one")), Select(selector, Blocks.Text("two")));
        Assert.Same(Select(selector, Blocks.Reasoning("why")), Select(selector, Blocks.ToolCall("search")));
    }

    [Fact]
    public void SelectTemplate_WithoutAMatch_ReturnsOneEmptyTemplate()
    {
        var selector = new BlockTemplateSelector { Templates = { new BlockTemplate { When = BlockMatch.Tool("search") } } };
        var other = new BlockTemplateSelector();

        var empty = Select(selector, Blocks.Text("hi"));

        Assert.Same(empty, Select(other, Blocks.Reasoning("why")));
        var view = Assert.IsType<ContentView>(empty.CreateContent());
        Assert.Null(view.Content);
    }

    [Fact]
    public void SelectTemplate_AMatchWithoutATemplate_ShowsTheEmptyTemplate()
    {
        var selector = new BlockTemplateSelector
        {
            Templates =
            {
                new BlockTemplate { When = BlockMatch.Is<ReasoningContentBlock>() },
                new BlockTemplate { Template = new DataTemplate(() => new Label()) },
            },
        };

        Assert.Same(Select(new BlockTemplateSelector(), Blocks.Text("hi")), Select(selector, Blocks.Reasoning("why")));
    }

    [Fact]
    public void SelectTemplate_ForAnItemThatIsNotABlock_ReturnsTheEmptyTemplate()
    {
        var selector = new BlockTemplateSelector { Templates = { new BlockTemplate { Template = new DataTemplate(() => new Label()) } } };

        Assert.Same(Select(new BlockTemplateSelector(), Blocks.Text("hi")), selector.SelectTemplate("not a block", new ContentView()));
    }

    [Fact]
    public void SelectTemplate_EvaluatesANestedSelectorAgainstTheBlock()
    {
        var failed = new DataTemplate(() => new Label { Text = "failed" });
        var ok = new DataTemplate(() => new Label { Text = "ok" });
        var selector = new BlockTemplateSelector
        {
            Templates =
            {
                new BlockTemplate
                {
                    When = BlockMatch.Is<FunctionInvocationContentBlock>(),
                    Template = new BlockTemplateSelector
                    {
                        Templates =
                        {
                            new BlockTemplate { When = BlockMatch.When(block => block is FunctionInvocationContentBlock { Result.Exception: not null }), Template = failed },
                            new BlockTemplate { Template = ok },
                        },
                    },
                },
            },
        };

        Assert.Same(failed, Select(selector, Blocks.ToolCall("x", exception: new InvalidOperationException())));
        Assert.Same(ok, Select(selector, Blocks.ToolCall("x", result: "ok")));
    }

    [Fact]
    public void SelectTemplate_IgnoresNullTemplates()
    {
        var text = new DataTemplate(() => new Label());
        var selector = new BlockTemplateSelector { Templates = { null!, new BlockTemplate { Template = text } } };

        Assert.Same(text, Select(selector, Blocks.Text("hi")));
    }

    [Fact]
    public void BlockTemplate_ReportsChangesToWhenAndTemplate()
    {
        var template = new BlockTemplate();
        var changed = new List<string?>();
        template.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        template.When = BlockMatch.Tool("search");
        template.Template = new DataTemplate(() => new Label());

        Assert.Equal([nameof(BlockTemplate.When), nameof(BlockTemplate.Template)], changed);
    }

    [Fact]
    public void ContentProperties_AreTheTemplates()
    {
        Assert.Equal(nameof(BlockTemplateSelector.Templates), BlockMatchTests.ContentProperty<BlockTemplateSelector>());
        Assert.Equal(nameof(BlockTemplate.Template), BlockMatchTests.ContentProperty<BlockTemplate>());
    }

    [Fact]
    public void Selector_RendersABindableLayoutOfBlocks()
    {
        var selector = new BlockTemplateSelector
        {
            Templates =
            {
                new BlockTemplate { When = BlockMatch.Tool("search"), Template = new DataTemplate(typeof(ToolNameView)) },
                new BlockTemplate { When = BlockMatch.Is<TextContentBlock>(), Template = new DataTemplate(typeof(RawTextLabel)) },
            },
        };
        var text = Blocks.Text("Hello");
        var search = Blocks.ToolCall("search");
        var layout = new VerticalStackLayout();
        BindableLayout.SetItemTemplateSelector(layout, selector);

        BindableLayout.SetItemsSource(layout, new ContentBlock[] { text, search, Blocks.Reasoning("why") });

        Assert.Equal(3, layout.Children.Count);
        var label = Assert.IsType<RawTextLabel>(layout.Children[0]);
        Assert.Same(text, label.BindingContext);
        Assert.Equal("Hello", label.Text);
        var tool = Assert.IsType<ToolNameView>(layout.Children[1]);
        Assert.Same(search, tool.Block);
        Assert.Equal("search", tool.Label.Text);
        Assert.Null(Assert.IsType<ContentView>(layout.Children[2]).Content);

        text.AppendText(", world");
        Assert.Equal("Hello, world", label.Text);
    }

    private static DataTemplate Select(BlockTemplateSelector selector, ContentBlock block) =>
        selector.SelectTemplate(block, new ContentView());
}

/// <summary>A label bound to a text block's raw text, as an app would write it in XAML.</summary>
public sealed class RawTextLabel : Label
{
    public RawTextLabel() => this.SetBinding(TextProperty, nameof(RichContentBlock.RawText));
}

/// <summary>Shows the tool name of the call it renders.</summary>
public sealed class ToolNameView : BlockView<FunctionInvocationContentBlock>
{
    public ToolNameView() => Content = Label;

    public Label Label { get; } = new();

    protected override void OnBlockChanged() => Label.Text = Block?.ToolName;
}
