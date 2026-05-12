namespace Microsoft.Maui.AI.Indexer.Tests;

/// <summary>
/// Tests for every issue and gap identified by the Opus 4.7 and GPT 5.5 code reviews.
/// Each test targets a specific finding number.
/// </summary>
public class ReviewFindingTests
{
    private static string Page(string xClass, string content, string extraXmlns = "") =>
        $"""
        <?xml version="1.0" encoding="utf-8" ?>
        <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                     {extraXmlns}
                     x:Class="{xClass}">
            {content}
        </ContentPage>
        """;

    // ──────────────────────────────────────────────
    // Issue #1/#2: Duplicate x:Class simple names
    // ──────────────────────────────────────────────

    [Fact]
    public void DuplicateClassName_DifferentNamespaces_BothGenerated()
    {
        var page1 = Page("MyApp.Admin.SettingsPage", "<Label Text=\"Admin Settings\" />");
        var page2 = Page("MyApp.User.SettingsPage", "<Label Text=\"User Settings\" />");

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("Admin/SettingsPage.xaml", page1),
            ("User/SettingsPage.xaml", page2));

        // Both should generate unique files (no crash)
        var keys = sources.Keys.Where(k => k.Contains("SettingsPage_UiIndex")).ToList();
        Assert.Equal(2, keys.Count);

        var md1 = GeneratorTestHarness.GetMarkdown("SettingsPage",
            ("Admin/SettingsPage.xaml", page1));
        Assert.NotNull(md1);
        Assert.Contains("Admin Settings", md1);
    }

    // ──────────────────────────────────────────────
    // Issue #3: CollectionView conditional — no double brackets
    // ──────────────────────────────────────────────

    [Fact]
    public void CollectionView_WithCondition_SingleBrackets()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <CollectionView ItemsSource="{Binding Items}" IsVisible="{Binding HasItems}">
                    <CollectionView.ItemTemplate>
                        <DataTemplate>
                            <Label Text="{Binding Name}" />
                        </DataTemplate>
                    </CollectionView.ItemTemplate>
                </CollectionView>
                """)));

        Assert.Equal(
            """
            # T

            File: T.xaml

            - CollectionView: "{Items}" [visible when HasItems = true]
              - Each item:
                - Label: "{Name}"
            """,
            md);
    }

    [Fact]
    public void CollectionView_Grouped_WithCondition()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <CollectionView ItemsSource="{Binding Groups}" IsGrouped="True" IsVisible="{Binding ShowList}">
                    <CollectionView.GroupHeaderTemplate>
                        <DataTemplate><Label Text="{Binding Key}" /></DataTemplate>
                    </CollectionView.GroupHeaderTemplate>
                    <CollectionView.ItemTemplate>
                        <DataTemplate><Label Text="{Binding Name}" /></DataTemplate>
                    </CollectionView.ItemTemplate>
                </CollectionView>
                """)));

        Assert.Equal(
            """
            # T

            File: T.xaml

            - CollectionView: "{Groups}" [grouped, visible when ShowList = true]
              - Group header (each group):
                - Label: "{Key}"
              - Each item:
                - Label: "{Name}"
            """,
            md);
    }

    // ──────────────────────────────────────────────
    // Issue #4: SemanticProperties on root ContentPage
    // ──────────────────────────────────────────────

    [Fact]
    public void RootPage_WithSemanticDescription_ChildrenPreserved()
    {
        var xaml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         x:Class="MyApp.TestPage"
                         SemanticProperties.Description="Settings page">
                <Label Text="Setting 1" />
                <Button Text="Save" />
            </ContentPage>
            """;
        var md = GeneratorTestHarness.GetMarkdown("TestPage", ("TestPage.xaml", xaml));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Label: "Setting 1"
            - Button: "Save"
            """,
            md);
    }

    // ──────────────────────────────────────────────
    // Issue #5: Conditional structural layouts propagate to children
    // ──────────────────────────────────────────────

    [Fact]
    public void ConditionalLayout_PropagatedToChildren()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <StackLayout IsVisible="{Binding IsLoaded}">
                    <Label Text="Name" />
                    <Button Text="Save" />
                </StackLayout>
                """)));

        Assert.Equal(
            """
            # T

            File: T.xaml

            - When [visible when IsLoaded = true]:
              - Label: "Name"
              - Button: "Save"
            """,
            md);
    }

    // ──────────────────────────────────────────────
    // Issue #6: Property-element content preserved
    // ──────────────────────────────────────────────

    [Fact]
    public void ContentPage_ContentPropertyElement()
    {
        var xaml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         x:Class="MyApp.TestPage">
                <ContentPage.Content>
                    <VerticalStackLayout>
                        <Label Text="Inside Content" />
                    </VerticalStackLayout>
                </ContentPage.Content>
            </ContentPage>
            """;
        var md = GeneratorTestHarness.GetMarkdown("TestPage", ("TestPage.xaml", xaml));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Label: "Inside Content"
            """,
            md);
    }

    [Fact]
    public void ScrollView_ContentPropertyElement()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <ScrollView>
                    <ScrollView.Content>
                        <VerticalStackLayout>
                            <Label Text="Scrollable content" />
                        </VerticalStackLayout>
                    </ScrollView.Content>
                </ScrollView>
                """)));

        Assert.Equal(
            """
            # T

            File: T.xaml

            - Label: "Scrollable content"
            """,
            md);
    }

    // ──────────────────────────────────────────────
    // Issue #8: Promoted containers keep children
    // ──────────────────────────────────────────────

    [Fact]
    public void PromotedContainer_KeepsActionableChildren()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <Border SemanticProperties.Description="Product card">
                    <Button Text="Buy" Command="{Binding BuyCommand}" />
                </Border>
                """)));

        Assert.Equal(
            """
            # T

            File: T.xaml

            - Border: "Product card"
              - Button: "Buy" → BuyCommand
            """,
            md);
    }

    // ──────────────────────────────────────────────
    // Issue #9: Unresolved controls with SemanticProperties kept
    // ──────────────────────────────────────────────

    [Fact]
    public void UnresolvedControl_WithSemantics_KeptAsPlaceholder()
    {
        var page = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         xmlns:tk="clr-namespace:Telerik.Controls"
                         x:Class="MyApp.TestPage">
                <tk:RadButton SemanticProperties.Description="Checkout" SemanticProperties.Hint="Completes order" />
            </ContentPage>
            """;
        var md = GeneratorTestHarness.GetMarkdown("TestPage", ("T.xaml", page));

        // Third-party controls are kept as placeholders with their semantic info
        Assert.NotNull(md);
        Assert.Contains("[RadButton]:", md!);
    }

    // ──────────────────────────────────────────────
    // Issue #10: DataTrigger IsVisible=False is inverted
    // ──────────────────────────────────────────────

    [Fact]
    public void DataTrigger_HidesElement()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <Label Text="Loading">
                    <Label.Triggers>
                        <DataTrigger TargetType="Label" Binding="{Binding IsBusy}" Value="True">
                            <Setter Property="IsVisible" Value="False" />
                        </DataTrigger>
                    </Label.Triggers>
                </Label>
                """)));

        Assert.Equal(
            "# T\n\nFile: T.xaml\n\n- Label: \"Loading\" [hidden when IsBusy = True]",
            md);
    }

    [Fact]
    public void DataTrigger_ShowsElement()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <Label Text="Ready">
                    <Label.Triggers>
                        <DataTrigger TargetType="Label" Binding="{Binding IsReady}" Value="True">
                            <Setter Property="IsVisible" Value="True" />
                        </DataTrigger>
                    </Label.Triggers>
                </Label>
                """)));

        Assert.Equal(
            "# T\n\nFile: T.xaml\n\n- Label: \"Ready\" [visible when IsReady = True]",
            md);
    }

    // ──────────────────────────────────────────────
    // Issue #11: IsVisible=False elements skipped
    // ──────────────────────────────────────────────

    [Fact]
    public void AlwaysHidden_Skipped()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <Label Text="Debug only" IsVisible="False" />
                <Label Text="Visible" />
                """)));

        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"Visible\"", md);
    }

    [Fact]
    public void AlwaysHidden_LayoutSkipped()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <StackLayout IsVisible="False">
                    <Label Text="Hidden section" />
                </StackLayout>
                <Label Text="Visible" />
                """)));

        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"Visible\"", md);
    }

    // ──────────────────────────────────────────────
    // Issue #13: global:: for no-namespace pages in aggregate
    // ──────────────────────────────────────────────

    [Fact]
    public void Aggregate_NoNamespacePage_UsesGlobal()
    {
        var page = Page("RootPage", "<Label Text=\"Hi\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("RootPage.xaml", page));

        var agg = sources["UiIndex.g.cs"];
        Assert.Contains("global::RootPage_UiIndex.Markdown", agg);
    }

    // ──────────────────────────────────────────────
    // Issue #14: BindingRegex doesn't match non-bindings
    // ──────────────────────────────────────────────

    [Fact]
    public void BindingSource_NotParsedAsBinding()
    {
        // {BindingSource} should NOT be parsed as a binding to path "Source"
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                "<Label Text=\"{BindingSource}\" />")));

        // Should show raw markup, not a parsed binding
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"{BindingSource}\"", md);
    }

    // ──────────────────────────────────────────────
    // Issue #15: Indirect cycle in cross-file resolution
    // ──────────────────────────────────────────────

    [Fact]
    public void CrossFile_IndirectCycle_NoInfiniteLoop()
    {
        var pageA = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         xmlns:v="clr-namespace:X.V"
                         x:Class="X.V.WidgetA">
                <Label Text="A" />
                <v:WidgetB />
            </ContentView>
            """;
        var pageB = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         xmlns:v="clr-namespace:X.V"
                         x:Class="X.V.WidgetB">
                <Label Text="B" />
                <v:WidgetA />
            </ContentView>
            """;
        var main = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         xmlns:v="clr-namespace:X.V"
                         x:Class="X.P.Main">
                <v:WidgetA />
            </ContentPage>
            """;

        // Should not throw or hang — cycles are broken
        var md = GeneratorTestHarness.GetMarkdown("Main",
            ("P/Main.xaml", main), ("V/WidgetA.xaml", pageA), ("V/WidgetB.xaml", pageB));

        Assert.NotNull(md);
        Assert.Contains("[WidgetA]:", md!);
    }

    // ──────────────────────────────────────────────
    // Triple-quote in user strings (raw literal escaping)
    // ──────────────────────────────────────────────

    [Fact]
    public void TripleQuote_InSemanticDescription()
    {
        // A Description containing """ should produce valid C# with extended delimiters
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """<Label Text="normal" SemanticProperties.Description='Contains triple &quot;&quot;&quot; quotes' />""")));

        Assert.NotNull(md);
        // The markdown itself should have the triple quotes
        Assert.Contains("Contains triple", md!);
    }

    // ──────────────────────────────────────────────
    // Escaped {} markup extension
    // ──────────────────────────────────────────────

    [Fact]
    public void EscapedMarkup_NotParsedAsBinding()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                "<Label Text=\"{}{not a binding}\" />")));

        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"{}{not a binding}\"", md);
    }
}
