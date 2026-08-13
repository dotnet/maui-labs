using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.Chat.Controls.Tests;

/// <summary>Covers template matching, the two selection tiers, priority, and template caching.</summary>
public class ChatContentTemplateTests
{
    [Fact]
    public void DefaultTemplates_MatchTheirContent()
    {
        var participant = ChatFactory.Remote();
        var text = ChatFactory.Item(participant, new TextMessageContent("hi"));
        var image = ChatFactory.Item(participant, ChatFactory.Image());
        var file = ChatFactory.Item(participant, ChatFactory.File());

        Assert.True(new ChatTextContentTemplate().When(text));
        Assert.False(new ChatTextContentTemplate().When(image));

        Assert.True(new ChatMediaContentTemplate().When(image));
        Assert.False(new ChatMediaContentTemplate().When(file));
        Assert.False(new ChatMediaContentTemplate().When(text));

        Assert.True(new ChatFileContentTemplate().When(file));
        Assert.False(new ChatFileContentTemplate().When(image));
    }

    [Fact]
    public void DefaultTemplates_UseTheBuiltInViews()
    {
        Assert.Equal(typeof(ChatTextContentView), new ChatTextContentTemplate().ViewType);
        Assert.Equal(typeof(ChatMediaContentView), new ChatMediaContentTemplate().ViewType);
        Assert.Equal(typeof(ChatFileContentView), new ChatFileContentTemplate().ViewType);
    }

    [Fact]
    public void GetTemplate_ReturnsTheSameInstanceEveryTime()
    {
        var template = new ChatTextContentTemplate();

        Assert.Same(template.GetTemplate(), template.GetTemplate());
    }

    [Fact]
    public void GetTemplate_AfterViewTypeChange_RebuildsOnce()
    {
        var template = new ChatTextContentTemplate();
        var first = template.GetTemplate();

        template.ViewType = typeof(ChatFileContentView);
        var second = template.GetTemplate();

        Assert.NotSame(first, second);
        Assert.Same(second, template.GetTemplate());
    }

    [Fact]
    public void GetTemplate_WithoutViewType_Throws()
    {
        var template = new GenericChatContentTemplate();

        Assert.Throws<InvalidOperationException>(() => template.GetTemplate());
    }

    [Fact]
    public void GetTemplate_CreatesTheViewAndBindsTheItem()
    {
        var template = new ChatTextContentTemplate();
        var item = ChatFactory.Item(ChatFactory.Remote(), new TextMessageContent("hello"));

        var view = Assert.IsType<ChatTextContentView>(template.GetTemplate().CreateContent());
        view.BindingContext = item;

        Assert.Same(item, view.Item);
    }

    [Fact]
    public void CreateView_WithNonViewType_Throws() =>
        Assert.Throws<InvalidOperationException>(() => ChatContentTemplate.CreateView(typeof(string)));

    [Fact]
    public void CreateView_WithNullType_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ChatContentTemplate.CreateView(null!));

    [Fact]
    public void CreateView_WithoutParameterlessConstructor_ExplainsWhy()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ChatContentTemplate.CreateView(typeof(NeedsDependencyView)));

        Assert.Contains("parameterless constructor", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateView_WithServiceProvider_ResolvesConstructorDependencies()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Dependency());
        using var provider = services.BuildServiceProvider();

        var view = ChatContentTemplate.CreateView(typeof(NeedsDependencyView), provider);

        Assert.IsType<NeedsDependencyView>(view);
    }

    [Fact]
    public void CreateView_WithServiceProviderMissingDependency_Throws()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(
            () => ChatContentTemplate.CreateView(typeof(NeedsDependencyView), provider));
    }

    [Fact]
    public void Generic_WithNoFilters_MatchesEverything()
    {
        var template = new GenericChatContentTemplate();

        Assert.True(template.When(ChatFactory.Item(ChatFactory.Remote())));
        Assert.True(template.When(ChatFactory.Item(ChatFactory.Local(), ChatFactory.Image())));
        Assert.False(template.When(null!));
    }

    [Fact]
    public void Generic_FiltersCombineWithAnd()
    {
        var template = new GenericChatContentTemplate
        {
            ContentType = typeof(TextMessageContent),
            ParticipantKind = ChatParticipantKind.Agent,
        };

        Assert.True(template.When(ChatFactory.Item(ChatFactory.Agent())));
        Assert.False(template.When(ChatFactory.Item(ChatFactory.Remote())));
        Assert.False(template.When(ChatFactory.Item(ChatFactory.Agent(), ChatFactory.Image())));
    }

    [Fact]
    public void Generic_ContentTypeMatchesSubclasses()
    {
        var template = new GenericChatContentTemplate { ContentType = typeof(MessageContent) };

        Assert.True(template.When(ChatFactory.Item(ChatFactory.Remote(), ChatFactory.Image())));
    }

    [Fact]
    public void Generic_DirectionFilterUsesTheRowFlag()
    {
        var template = new GenericChatContentTemplate { IsOutgoing = true };
        var item = ChatFactory.Item(ChatFactory.Remote());

        Assert.False(template.When(item));

        item.UpdateFlags(true, true, true, true, true);
        Assert.True(template.When(item));
    }

    [Fact]
    public void Generic_MoreSpecificFiltersGetHigherPriority()
    {
        var item = ChatFactory.Item(ChatFactory.Agent());
        var broad = new GenericChatContentTemplate();
        var narrow = new GenericChatContentTemplate
        {
            ContentType = typeof(TextMessageContent),
            ParticipantKind = ChatParticipantKind.Agent,
            IsOutgoing = false,
        };

        Assert.True(narrow.GetPriority(item) > broad.GetPriority(item));
    }

    [Fact]
    public void Selector_WithNoTemplates_RendersNothing()
    {
        var selector = new ChatContentTemplateSelector();

        var template = selector.SelectTemplate(ChatFactory.Item(ChatFactory.Remote()), null!);

        AssertHidden(template);
    }

    [Fact]
    public void Selector_WithUnknownItem_RendersNothing()
    {
        var selector = new ChatContentTemplateSelector();
        selector.FallbackTemplates.Add(new ChatTextContentTemplate());

        AssertHidden(selector.SelectTemplate("not a row", null!));
    }

    [Fact]
    public void Selector_WithUnmatchedCustomContent_RendersNothing()
    {
        var selector = CreateDefaultSelector();
        var item = ChatFactory.Item(ChatFactory.Remote(), new CustomContent());

        AssertHidden(selector.SelectTemplate(item, null!));
    }

    [Fact]
    public void Selector_UsesFallbacksWhenNoConsumerTemplateMatches()
    {
        var selector = CreateDefaultSelector();
        var item = ChatFactory.Item(ChatFactory.Remote(), new TextMessageContent("hi"));

        var template = selector.SelectTemplate(item, null!);

        Assert.Same(selector.FallbackTemplates[0].GetTemplate(), template);
    }

    [Fact]
    public void Selector_PrefersConsumerTemplatesOverHigherPriorityFallbacks()
    {
        var selector = CreateDefaultSelector();
        var consumer = new GenericChatContentTemplate
        {
            ViewType = typeof(ChatTextContentView),
            Priority = -1000,
        };
        selector.Templates.Add(consumer);
        selector.FallbackTemplates[0].Priority = 1000;

        var item = ChatFactory.Item(ChatFactory.Remote(), new TextMessageContent("hi"));

        Assert.Same(consumer.GetTemplate(), selector.SelectTemplate(item, null!));
    }

    [Fact]
    public void Selector_WithinATier_HighestPriorityWins()
    {
        var selector = new ChatContentTemplateSelector();
        var low = new GenericChatContentTemplate { ViewType = typeof(ChatTextContentView), Priority = 1 };
        var high = new GenericChatContentTemplate { ViewType = typeof(ChatFileContentView), Priority = 5 };
        selector.Templates.Add(low);
        selector.Templates.Add(high);

        var template = selector.SelectTemplate(ChatFactory.Item(ChatFactory.Remote()), null!);

        Assert.Same(high.GetTemplate(), template);
    }

    [Fact]
    public void Selector_WithEqualPriority_KeepsDeclarationOrder()
    {
        var selector = new ChatContentTemplateSelector();
        var first = new GenericChatContentTemplate { ViewType = typeof(ChatTextContentView) };
        var second = new GenericChatContentTemplate { ViewType = typeof(ChatFileContentView) };
        selector.Templates.Add(first);
        selector.Templates.Add(second);

        Assert.Same(first.GetTemplate(), selector.SelectTemplate(ChatFactory.Item(ChatFactory.Remote()), null!));
    }

    [Fact]
    public void Selector_ReturnsAStableTemplateAcrossSelections()
    {
        var selector = CreateDefaultSelector();
        var item = ChatFactory.Item(ChatFactory.Remote(), new TextMessageContent("hi"));

        Assert.Same(selector.SelectTemplate(item, null!), selector.SelectTemplate(item, null!));
    }

    [Fact]
    public void Selector_IgnoresNullEntries()
    {
        var selector = new ChatContentTemplateSelector();
        selector.Templates.Add(null!);
        selector.Templates.Add(new GenericChatContentTemplate { ViewType = typeof(ChatTextContentView) });

        Assert.Same(
            selector.Templates[1]!.GetTemplate(),
            selector.SelectTemplate(ChatFactory.Item(ChatFactory.Remote()), null!));
    }

    [Fact]
    public void HiddenTemplate_IsSharedZeroSizedAndInvisible()
    {
        Assert.Same(
            ChatContentTemplateSelector.GetHiddenTemplate(),
            ChatContentTemplateSelector.GetHiddenTemplate());

        AssertHidden(ChatContentTemplateSelector.GetHiddenTemplate());
    }

    private static ChatContentTemplateSelector CreateDefaultSelector()
    {
        var selector = new ChatContentTemplateSelector();
        selector.FallbackTemplates.Add(new ChatTextContentTemplate());
        selector.FallbackTemplates.Add(new ChatMediaContentTemplate());
        selector.FallbackTemplates.Add(new ChatFileContentTemplate());
        return selector;
    }

    private static void AssertHidden(DataTemplate template)
    {
        var view = Assert.IsType<ContentView>(template.CreateContent());

        Assert.False(view.IsVisible);
        Assert.Equal(0, view.HeightRequest);
    }

    private sealed class CustomContent : MessageContent
    {
    }

    private sealed class Dependency
    {
    }

    private sealed class NeedsDependencyView : ChatContentView
    {
        public NeedsDependencyView(Dependency dependency) => Dependency = dependency;

        public Dependency Dependency { get; }

        protected override void RefreshContent()
        {
        }
    }
}
