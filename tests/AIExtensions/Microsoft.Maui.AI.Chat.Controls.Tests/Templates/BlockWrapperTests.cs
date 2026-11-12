namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class BlockWrapperTests
{
    [Fact]
    public void ApprovalWrapper_ShowsItsChromeAroundTheViewOfTheCall()
    {
        var approval = Blocks.Approval("search");
        var chrome = Chrome();
        var selector = new BlockTemplateSelector
        {
            Wrappers = { new ApprovalWrapper { Template = chrome } },
            Templates = { new BlockTemplate { When = BlockMatch.Tool("search"), Template = new DataTemplate(typeof(ToolNameView)) } },
        };

        var view = Assert.IsAssignableFrom<ContentView>(Show(selector, approval));

        Assert.Same(approval, view.BindingContext);
        Assert.Same(chrome, view.ControlTemplate);
        var card = Assert.IsType<ToolNameView>(view.Content);
        Assert.Same(card, Assert.Single(view.GetVisualTreeDescendants().OfType<ContentPresenter>()).Content);
        Assert.Same(approval.InnerBlock, card.BindingContext);
        Assert.Same(approval.InnerBlock, card.Block);
        Assert.Equal("search", card.Label.Text);
    }

    [Fact]
    public void ApprovalWrapper_TheChromeBindsToTheApproval()
    {
        var approval = Blocks.Approval("search");
        var view = Show(Selector(new ApprovalWrapper { Template = Chrome() }), approval);
        var awaiting = Label(view, "Awaiting");

        Assert.Equal("True", awaiting.Text);
        Assert.Equal("search", Label(view, "Tool").Text);

        approval.Approve();

        Assert.Equal("False", awaiting.Text);
    }

    [Fact]
    public void ApprovalWrapper_ShowsABlockOfTheAppsOwnTypeWithItsTemplate()
    {
        var call = Blocks.ToolCall("get_weather");
        var card = new WeatherCard();
        var approval = Blocks.Approval(card, call);
        var selector = new BlockTemplateSelector
        {
            Wrappers = { new ApprovalWrapper { Template = Chrome() } },
            Templates =
            {
                new BlockTemplate { When = BlockMatch.Is<WeatherCard>(), Template = new DataTemplate(() => new Label { Text = "card" }) },
                new BlockTemplate { Template = new DataTemplate(typeof(ToolNameView)) },
            },
        };

        var view = Assert.IsAssignableFrom<ContentView>(Show(selector, approval));

        var label = Assert.IsType<Label>(view.Content);
        Assert.Equal("card", label.Text);
        Assert.Same(card, label.BindingContext);
        Assert.Equal("get_weather", Label(view, "Tool").Text);
    }

    [Fact]
    public void ApprovalWrapper_AReusedViewShowsTheNextApproval()
    {
        // A list that caches item views by template gives the same view the next block that selects its template.
        var first = Blocks.Approval("search");
        var second = Blocks.Approval("search");
        var selector = Selector(new ApprovalWrapper { Template = Chrome() });
        var template = Select(selector, first);
        Assert.Same(template, Select(selector, second));
        var view = (ContentView)template.CreateContent();
        view.BindingContext = first;
        var card = Assert.IsType<ToolNameView>(view.Content);
        first.Approve();
        Assert.Equal("False", Label(view, "Awaiting").Text);

        view.BindingContext = second;

        Assert.Same(card, view.Content);
        Assert.Same(second.InnerBlock, card.Block);
        Assert.Equal("True", Label(view, "Awaiting").Text);
    }

    [Fact]
    public void SelectTemplate_ComposesOneTemplateForEachChromeAndWrappedTemplate()
    {
        var search = new DataTemplate(typeof(ToolNameView));
        var wrapper = new ApprovalWrapper { Template = Chrome() };
        var selector = new BlockTemplateSelector
        {
            Wrappers = { wrapper },
            Templates =
            {
                new BlockTemplate { When = BlockMatch.Tool("search"), Template = search },
                new BlockTemplate { Template = new DataTemplate(() => new Label()) },
            },
        };

        var composed = Select(selector, Blocks.Approval("search"));

        Assert.NotSame(search, composed);
        Assert.Same(composed, Select(selector, Blocks.Approval("search")));
        Assert.NotSame(composed, Select(selector, Blocks.Approval("other")));
        Assert.Same(search, Select(selector, Blocks.ToolCall("search")));

        wrapper.Template = Chrome();

        Assert.NotSame(composed, Select(selector, Blocks.Approval("search")));
    }

    [Fact]
    public void Wrapper_When_MatchesTheWrappedBlock()
    {
        var deletion = Chrome();
        var other = Chrome();
        var selector = Selector(
            new ApprovalWrapper { When = BlockMatch.Tool("delete_file"), Template = deletion },
            new ApprovalWrapper { Template = other });

        Assert.Same(deletion, ((ContentView)Show(selector, Blocks.Approval("delete_file"))).ControlTemplate);
        Assert.Same(other, ((ContentView)Show(selector, Blocks.Approval("search"))).ControlTemplate);
    }

    [Fact]
    public void Wrapper_WithoutChrome_ShowsTheWrappedBlocksViewBoundToTheWrappedBlock()
    {
        var approval = Blocks.Approval("search");
        var selector = Selector(new ApprovalWrapper());

        var view = Assert.IsAssignableFrom<ContentView>(Show(selector, approval));

        Assert.Null(view.ControlTemplate);
        var card = Assert.IsType<ToolNameView>(view.Content);
        Assert.Same(approval.InnerBlock, card.Block);
        Assert.Equal("search", card.Label.Text);
    }

    [Fact]
    public void Wrappers_AreTriedBeforeTheTemplates()
    {
        var approvalTemplate = new DataTemplate(() => new Label { Text = "approval" });
        var chrome = Chrome();
        var selector = new BlockTemplateSelector
        {
            Wrappers = { new ApprovalWrapper { When = BlockMatch.Tool("delete_file"), Template = chrome } },
            Templates =
            {
                new BlockTemplate { When = BlockMatch.Is<ToolApprovalBlock>(), Template = approvalTemplate },
                new BlockTemplate { Template = new DataTemplate(typeof(ToolNameView)) },
            },
        };

        Assert.Same(chrome, ((ContentView)Show(selector, Blocks.Approval("delete_file"))).ControlTemplate);
        Assert.Same(approvalTemplate, Select(selector, Blocks.Approval("search")));
    }

    [Fact]
    public void Wrappers_ABlockThatNoneWraps_GetsItsTemplate()
    {
        var text = new DataTemplate(typeof(RawTextLabel));
        var selector = new BlockTemplateSelector
        {
            Wrappers = { new ApprovalWrapper { Template = Chrome() } },
            Templates = { new BlockTemplate { Template = text } },
        };

        Assert.Same(text, Select(selector, Blocks.Text("hi")));
        Assert.Same(text, Select(selector, Blocks.ToolCall("search")));
        Assert.Same(text, Select(selector, Blocks.UIAction("search", UIActionInvocationMode.Manual)));
    }

    [Fact]
    public void Wrapper_TheWrappedBlocksTemplateCanComeFromANestedSelector()
    {
        var selector = new BlockTemplateSelector
        {
            Wrappers = { new ApprovalWrapper { Template = Chrome() } },
            Templates =
            {
                new BlockTemplate
                {
                    When = BlockMatch.Is<FunctionInvocationContentBlock>(),
                    Template = new BlockTemplateSelector
                    {
                        Templates =
                        {
                            new BlockTemplate
                            {
                                When = BlockMatch.When(block => block is FunctionInvocationContentBlock { HasResult: true }),
                                Template = new DataTemplate(() => new Label { Text = "done" }),
                            },
                            new BlockTemplate { Template = new DataTemplate(() => new Label { Text = "running" }) },
                        },
                    },
                },
            },
        };

        var running = (ContentView)Show(selector, Blocks.Approval(Blocks.ToolCall("search")));
        var done = (ContentView)Show(selector, Blocks.Approval(Blocks.ToolCall("search", result: "ok")));

        Assert.Equal("running", Assert.IsType<Label>(running.Content).Text);
        Assert.Equal("done", Assert.IsType<Label>(done.Content).Text);
    }

    [Fact]
    public void Wrappers_Nest()
    {
        var approval = Blocks.Approval("search");
        var envelope = new Envelope(approval);
        var outer = Chrome();
        var inner = Chrome();
        var selector = Selector(new EnvelopeWrapper { Template = outer }, new ApprovalWrapper { Template = inner });

        var view = Assert.IsAssignableFrom<ContentView>(Show(selector, envelope));

        Assert.Same(outer, view.ControlTemplate);
        var approvalView = Assert.IsAssignableFrom<ContentView>(view.Content);
        Assert.Same(approval, approvalView.BindingContext);
        Assert.Same(inner, approvalView.ControlTemplate);
        var card = Assert.IsType<ToolNameView>(approvalView.Content);
        Assert.Same(approval.InnerBlock, card.Block);
    }

    [Fact]
    public void Wrapper_ThatReturnsTheBlockItWasGiven_Throws()
    {
        var selector = Selector(new SelfWrapper { Template = Chrome() });

        var exception = Assert.Throws<InvalidOperationException>(() => Select(selector, Blocks.Text("hi")));

        Assert.Contains(nameof(SelfWrapper), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrappers_IgnoreNullWrappers()
    {
        var chrome = Chrome();
        var selector = Selector(null!, new ApprovalWrapper { Template = chrome });

        Assert.Same(chrome, ((ContentView)Show(selector, Blocks.Approval("search"))).ControlTemplate);
    }

    [Fact]
    public void ApprovalWrapper_WithANullBlock_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new ApprovalWrapper().GetWrappedBlock(null!));

    [Fact]
    public void Wrapper_ReportsChangesToWhenAndTemplate()
    {
        var wrapper = new ApprovalWrapper();
        var changed = new List<string?>();
        wrapper.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        wrapper.When = BlockMatch.Tool("search");
        wrapper.Template = Chrome();

        Assert.Equal([nameof(BlockWrapper.When), nameof(BlockWrapper.Template)], changed);
    }

    [Fact]
    public void Wrapper_ContentPropertyIsTheTemplate() =>
        Assert.Equal(nameof(BlockWrapper.Template), BlockMatchTests.ContentProperty<ApprovalWrapper>());

    [Fact]
    public void AViewInTheWrappedView_FindsTheApprovalAsAnAncestor()
    {
        // A call's card can follow the approval around it (to hide its spinner while the call waits for a decision)
        // with a RelativeSource binding to the ancestor whose binding context is a ToolApprovalBlock.
        var selector = new BlockTemplateSelector
        {
            Wrappers = { new ApprovalWrapper { Template = Chrome() } },
            Templates = { new BlockTemplate { Template = new DataTemplate(typeof(ApprovalStateView)) } },
        };
        var first = Blocks.Approval("search");
        var view = (ContentView)Show(selector, first);
        var state = Assert.IsType<ApprovalStateView>(view.Content);

        Assert.Equal("True", state.Label.Text);

        first.Approve();
        Assert.Equal("False", state.Label.Text);

        view.BindingContext = Blocks.Approval("search");
        Assert.Equal("True", state.Label.Text);

        var call = Assert.IsType<ApprovalStateView>(Show(selector, Blocks.ToolCall("search")));
        Assert.Null(call.Label.Text);
    }

    [Fact]
    public void Selector_RendersWrappedBlocksInABindableLayout()
    {
        var text = Blocks.Text("Hello");
        var approval = Blocks.Approval("search");
        var chrome = Chrome();
        var selector = new BlockTemplateSelector
        {
            Wrappers = { new ApprovalWrapper { Template = chrome } },
            Templates =
            {
                new BlockTemplate { When = BlockMatch.Tool("search"), Template = new DataTemplate(typeof(ToolNameView)) },
                new BlockTemplate { When = BlockMatch.Is<TextContentBlock>(), Template = new DataTemplate(typeof(RawTextLabel)) },
            },
        };
        var layout = new VerticalStackLayout();
        BindableLayout.SetItemTemplateSelector(layout, selector);

        BindableLayout.SetItemsSource(layout, new ContentBlock[] { text, approval });

        Assert.Equal(2, layout.Children.Count);
        Assert.Same(text, Assert.IsType<RawTextLabel>(layout.Children[0]).BindingContext);
        var wrapped = Assert.IsAssignableFrom<ContentView>(layout.Children[1]);
        Assert.Same(approval, wrapped.BindingContext);
        Assert.Same(chrome, wrapped.ControlTemplate);
        Assert.Same(approval.InnerBlock, Assert.IsType<ToolNameView>(wrapped.Content).Block);
        Assert.Equal("True", Label(wrapped, "Awaiting").Text);
    }

    // The chrome of the tests: the tool name and whether the approval waits, around the wrapped block's view.
    private static ControlTemplate Chrome() => new(() =>
    {
        var tool = new Label { AutomationId = "Tool" };
        tool.SetBinding(Microsoft.Maui.Controls.Label.TextProperty, nameof(ToolApprovalBlock.ToolName));
        var awaiting = new Label { AutomationId = "Awaiting" };
        awaiting.SetBinding(Microsoft.Maui.Controls.Label.TextProperty, nameof(ToolApprovalBlock.IsAwaitingInput));
        return new VerticalStackLayout { tool, new ContentPresenter(), awaiting };
    });

    private static BlockTemplateSelector Selector(params BlockWrapper[] wrappers)
    {
        var selector = new BlockTemplateSelector { Templates = { new BlockTemplate { Template = new DataTemplate(typeof(ToolNameView)) } } };
        foreach (var wrapper in wrappers)
            selector.Wrappers.Add(wrapper);
        return selector;
    }

    private static DataTemplate Select(BlockTemplateSelector selector, ContentBlock block) =>
        selector.SelectTemplate(block, new ContentView());

    private static View Show(BlockTemplateSelector selector, ContentBlock block)
    {
        var view = (View)Select(selector, block).CreateContent();
        view.BindingContext = block;
        return view;
    }

    private static Label Label(View view, string automationId) =>
        view.GetVisualTreeDescendants().OfType<Label>().Single(label => label.AutomationId == automationId);

    /// <summary>A block of the app's own type that shows a call.</summary>
    private sealed class WeatherCard : ContentBlock
    {
    }

    /// <summary>A block of the app's own type that wraps another, with the wrapper that shows it.</summary>
    private sealed class Envelope(ContentBlock content) : ContentBlock
    {
        public ContentBlock Content { get; } = content;
    }

    private sealed class EnvelopeWrapper : BlockWrapper
    {
        public override ContentBlock? GetWrappedBlock(ContentBlock block) => (block as Envelope)?.Content;
    }

    private sealed class SelfWrapper : BlockWrapper
    {
        public override ContentBlock? GetWrappedBlock(ContentBlock block) => block;
    }
}

/// <summary>Shows whether the approval around it waits for a decision.</summary>
public sealed class ApprovalStateView : ContentView
{
    public ApprovalStateView()
    {
        Label.SetBinding(
            Label.TextProperty,
            new Binding(
                nameof(ToolApprovalBlock.IsAwaitingInput),
                source: new RelativeBindingSource(RelativeBindingSourceMode.FindAncestorBindingContext, typeof(ToolApprovalBlock))));
        Content = Label;
    }

    public Label Label { get; } = new();
}
