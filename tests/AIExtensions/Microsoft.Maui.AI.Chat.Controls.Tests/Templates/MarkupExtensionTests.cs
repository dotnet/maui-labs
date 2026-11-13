using Microsoft.Maui.Controls.Xaml;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class MarkupExtensionTests
{
    // The selector of SelectorXaml, written the way an app would: markup extensions for the common cases, the match
    // types as elements to compose them.
    internal const string SelectorXaml = """
        <ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                     xmlns:chat="http://schemas.microsoft.com/dotnet/2026/maui/ai/chat"
                     xmlns:local="clr-namespace:Microsoft.Maui.AI.Chat.Controls.Tests;assembly=Microsoft.Maui.AI.Chat.Controls.Tests">
            <ContentView.Resources>
                <chat:BlockTemplateSelector x:Key="Blocks">
                    <chat:BlockTemplateSelector.Wrappers>
                        <chat:ApprovalWrapper When="{chat:Tool delete_file}">
                            <ControlTemplate>
                                <VerticalStackLayout>
                                    <Label Text="Delete the file?" />
                                    <ContentPresenter />
                                </VerticalStackLayout>
                            </ControlTemplate>
                        </chat:ApprovalWrapper>
                        <chat:ApprovalWrapper>
                            <ControlTemplate x:DataType="chat:ToolApprovalBlock">
                                <VerticalStackLayout>
                                    <ContentPresenter />
                                    <Label Text="{Binding Status}" />
                                </VerticalStackLayout>
                            </ControlTemplate>
                        </chat:ApprovalWrapper>
                    </chat:BlockTemplateSelector.Wrappers>
                    <chat:BlockTemplate When="{chat:Tool search}" Template="{chat:ViewTemplate local:ToolNameView}" />
                    <chat:BlockTemplate When="{chat:Tool delete_file}">
                        <DataTemplate x:DataType="chat:FunctionInvocationContentBlock">
                            <Label Text="{Binding IsAwaitingInput, Source={RelativeSource AncestorType={x:Type chat:ToolApprovalBlock}}, x:DataType=chat:ToolApprovalBlock}" />
                        </DataTemplate>
                    </chat:BlockTemplate>
                    <chat:BlockTemplate When="{chat:Role user}">
                        <DataTemplate>
                            <Label Text="{Binding RawText}" />
                        </DataTemplate>
                    </chat:BlockTemplate>
                    <chat:BlockTemplate Template="{chat:ViewTemplate local:RawTextLabel}">
                        <chat:BlockTemplate.When>
                            <chat:AllMatch>
                                <chat:BlockTypeMatch Type="{x:Type chat:TextContentBlock}" />
                                <chat:NotMatch>
                                    <chat:RoleMatch Role="system" />
                                </chat:NotMatch>
                            </chat:AllMatch>
                        </chat:BlockTemplate.When>
                    </chat:BlockTemplate>
                    <chat:BlockTemplate When="{chat:Is chat:ReasoningContentBlock}" />
                    <chat:BlockTemplate>
                        <chat:BlockTemplate.When>
                            <chat:AnyMatch>
                                <chat:BlockTypeMatch Type="{x:Type chat:MediaContentBlock}" />
                                <chat:ToolMatch Name="get_weather" />
                            </chat:AnyMatch>
                        </chat:BlockTemplate.When>
                        <DataTemplate>
                            <Label Text="media or weather" />
                        </DataTemplate>
                    </chat:BlockTemplate>
                </chat:BlockTemplateSelector>
            </ContentView.Resources>
        </ContentView>
        """;

    [Fact]
    public void Is_ProvidesATypeMatch()
    {
        var match = Provide(new IsExtension { Type = typeof(TextContentBlock) });

        Assert.Equal(typeof(TextContentBlock), Assert.IsType<BlockTypeMatch>(match).Type);
        Assert.True(match.Matches(Blocks.Text("hi")));
    }

    [Fact]
    public void Tool_ProvidesAToolMatch()
    {
        var match = Provide(new ToolExtension { Name = "search" });

        Assert.Equal("search", Assert.IsType<ToolMatch>(match).Name);
        Assert.True(match.Matches(Blocks.ToolCall("search")));
    }

    [Fact]
    public void Role_ProvidesARoleMatch()
    {
        var match = Provide(new RoleExtension { Role = "user" });

        Assert.Equal("user", Assert.IsType<RoleMatch>(match).Role);
        Assert.True(match.Matches(Blocks.Text("hi", ChatRole.User)));
    }

    [Fact]
    public void ViewTemplate_ProvidesATemplateThatCreatesTheView()
    {
        var template = new ViewTemplateExtension { ViewType = typeof(RawTextLabel) }.ProvideValue(EmptyServices.Instance);

        Assert.Equal(typeof(RawTextLabel), Assert.IsType<ViewTemplate>(template).ViewType);
        Assert.IsType<RawTextLabel>(template.CreateContent());
    }

    [Fact]
    public void Extensions_WithoutAValue_Throw()
    {
        Assert.Throws<InvalidOperationException>(() => Provide(new IsExtension()));
        Assert.Throws<InvalidOperationException>(() => Provide(new ToolExtension()));
        Assert.Throws<InvalidOperationException>(() => Provide(new RoleExtension()));
        Assert.Throws<InvalidOperationException>(() => new ViewTemplateExtension().ProvideValue(EmptyServices.Instance));
    }

    [Fact]
    public void Extensions_TakeTheirValueAsContent()
    {
        Assert.Equal(nameof(IsExtension.Type), BlockMatchTests.ContentProperty<IsExtension>());
        Assert.Equal(nameof(ToolExtension.Name), BlockMatchTests.ContentProperty<ToolExtension>());
        Assert.Equal(nameof(RoleExtension.Role), BlockMatchTests.ContentProperty<RoleExtension>());
        Assert.Equal(nameof(ViewTemplateExtension.ViewType), BlockMatchTests.ContentProperty<ViewTemplateExtension>());
    }

    [Fact]
    public void Xaml_LoadedAtRunTime_SelectsTheTemplates() =>
        AssertSelector((BlockTemplateSelector)new ContentView().LoadFromXaml(SelectorXaml).Resources["Blocks"]);

    [Fact]
    public void Xaml_CompiledWithTheApp_SelectsTheTemplates() =>
        AssertSelector((BlockTemplateSelector)new CompiledTemplatesView().Resources["Blocks"]);

    private static void AssertSelector(BlockTemplateSelector selector)
    {
        Assert.Equal(2, selector.Wrappers.Count);
        Assert.Equal(6, selector.Templates.Count);

        var search = Assert.IsType<ToolNameView>(Create(selector, Blocks.ToolCall("search")));
        Assert.Equal("search", search.Label.Text);

        // An approval shows the card of its call inside the chrome of the first wrapper whose When matches the call.
        var approval = Blocks.Approval("search");
        var approvalView = Assert.IsAssignableFrom<ContentView>(Create(selector, approval));
        Assert.Same(approval.InnerBlock, Assert.IsType<ToolNameView>(approvalView.Content).Block);
        var status = Assert.Single(approvalView.GetVisualTreeDescendants().OfType<Label>(), label => label.Parent is not ToolNameView);
        Assert.Equal(nameof(ApprovalStatus.Pending), status.Text);
        approval.Approve();
        Assert.Equal(nameof(ApprovalStatus.Approved), status.Text);

        // The card of a call can follow the approval around it.
        var deletion = Blocks.Approval("delete_file");
        var deletionView = Assert.IsAssignableFrom<ContentView>(Create(selector, deletion));
        Assert.Contains(deletionView.GetVisualTreeDescendants().OfType<Label>(), label => label.Text == "Delete the file?");
        var awaiting = Assert.IsType<Label>(deletionView.Content);
        Assert.Equal("True", awaiting.Text);
        deletion.Reject();
        Assert.Equal("False", awaiting.Text);

        var user = Assert.IsType<Label>(Create(selector, Blocks.Text("Hi", ChatRole.User)));
        Assert.Equal("Hi", user.Text);

        var assistant = Assert.IsType<RawTextLabel>(Create(selector, Blocks.Text("Hello")));
        Assert.Equal("Hello", assistant.Text);

        var reasoning = Assert.IsType<ContentView>(Create(selector, Blocks.Reasoning("why")));
        Assert.Null(reasoning.Content);

        Assert.Equal("media or weather", Assert.IsType<Label>(Create(selector, Blocks.Media(Blocks.Png()))).Text);
        Assert.Equal("media or weather", Assert.IsType<Label>(Create(selector, Blocks.ToolCall("get_weather"))).Text);
        Assert.IsType<ContentView>(Create(selector, Blocks.ToolCall("other")));
    }

    private static View Create(BlockTemplateSelector selector, ContentBlock block)
    {
        var view = (View)selector.SelectTemplate(block, new ContentView()).CreateContent();
        view.BindingContext = block;
        return view;
    }

    private static BlockMatch Provide(IMarkupExtension<BlockMatch> extension) => extension.ProvideValue(EmptyServices.Instance);

    private sealed class EmptyServices : IServiceProvider
    {
        public static readonly EmptyServices Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
