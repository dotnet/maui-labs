namespace Microsoft.Maui.AI.Indexer.Tests;

/// <summary>
/// Additional exact-match tests covering more control types, edge cases,
/// and the aggregate index to ensure comprehensive coverage.
/// </summary>
public class AdditionalExactTests
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

    [Fact]
    public void CheckBox_WithBinding()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<CheckBox IsChecked=\"{Binding Agreed}\" />")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - CheckBox: "{Agreed}"
            """,
            md);
    }

    [Fact]
    public void RadioButton_WithContent()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<RadioButton Content=\"Option A\" />")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - RadioButton: "Option A"
            """,
            md);
    }

    [Fact]
    public void DatePicker_WithBinding()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<DatePicker Date=\"{Binding Delivery}\" />")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - DatePicker: "{Delivery}"
            """,
            md);
    }

    [Fact]
    public void TimePicker_WithBinding()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<TimePicker Time=\"{Binding SelectedTime}\" />")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - TimePicker: "{SelectedTime}"
            """,
            md);
    }

    [Fact]
    public void SearchBar_WithPlaceholder()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<SearchBar Placeholder=\"Search...\" />")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - SearchBar: [placeholder: "Search..."]
            """,
            md);
    }

    [Fact]
    public void Stepper_WithRange()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Stepper Minimum=\"0\" Maximum=\"10\" Value=\"{Binding Qty}\" />")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Stepper: 0–10 → "{Qty}"
            """,
            md);
    }

    [Fact]
    public void ActivityIndicator_WithBinding()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<ActivityIndicator IsRunning=\"{Binding IsBusy}\" />")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - ActivityIndicator: "{IsBusy}"
            """,
            md);
    }

    [Fact]
    public void ProgressBar_WithBinding()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<ProgressBar Progress=\"{Binding Download}\" />")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - ProgressBar: "{Download}"
            """,
            md);
    }

    [Fact]
    public void ImageButton_WithSource()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<ImageButton Source=\"heart.png\" Command=\"{Binding Like}\" />")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - ImageButton: "heart.png" → Like
            """,
            md);
    }

    [Fact]
    public void SemanticDescription_OverridesSlider()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """<Slider Minimum="0" Maximum="100" SemanticProperties.Description="Volume" SemanticProperties.Hint="Adjust volume" />""")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Slider: "Volume" [hint: Adjust volume]
            """,
            md);
    }

    [Fact]
    public void PromotedBorder_WithDescription()
    {
        // Promoted containers now also walk children
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """<Border SemanticProperties.Description="Product card"><Label Text="Inside" /></Border>""")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Border: "Product card"
              - Label: "Inside"
            """,
            md);
    }

    [Fact]
    public void DataTrigger_OnVisibility()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <Label Text="Discount">
                    <Label.Triggers>
                        <DataTrigger TargetType="Label" Binding="{Binding HasDiscount}" Value="True">
                            <Setter Property="IsVisible" Value="True" />
                        </DataTrigger>
                    </Label.Triggers>
                </Label>
                """)));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Label: "Discount" [visible when HasDiscount = True]
            """,
            md);
    }

    [Fact]
    public void NegateConverter()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """<Button Text="Go" IsVisible="{Binding IsBusy, Converter={StaticResource NegateBoolConverter}}" />""")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Button: "Go" [visible when IsBusy = false]
            """,
            md);
    }

    [Fact]
    public void CollectionView_WithHeaderAndFooter()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <CollectionView ItemsSource="{Binding Items}">
                    <CollectionView.HeaderTemplate>
                        <DataTemplate><Label Text="Start" /></DataTemplate>
                    </CollectionView.HeaderTemplate>
                    <CollectionView.ItemTemplate>
                        <DataTemplate><Label Text="{Binding Name}" /></DataTemplate>
                    </CollectionView.ItemTemplate>
                    <CollectionView.FooterTemplate>
                        <DataTemplate><Label Text="End" /></DataTemplate>
                    </CollectionView.FooterTemplate>
                </CollectionView>
                """)));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - CollectionView: "{Items}"
              - Header:
                - Label: "Start"
              - Each item:
                - Label: "{Name}"
              - Footer:
                - Label: "End"
            """,
            md);
    }

    [Fact]
    public void Grouped_WithGroupFooter()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <CollectionView ItemsSource="{Binding G}" IsGrouped="True">
                    <CollectionView.GroupHeaderTemplate>
                        <DataTemplate><Label Text="{Binding Key}" /></DataTemplate>
                    </CollectionView.GroupHeaderTemplate>
                    <CollectionView.ItemTemplate>
                        <DataTemplate><Label Text="{Binding Val}" /></DataTemplate>
                    </CollectionView.ItemTemplate>
                    <CollectionView.GroupFooterTemplate>
                        <DataTemplate><Label Text="---" /></DataTemplate>
                    </CollectionView.GroupFooterTemplate>
                </CollectionView>
                """)));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - CollectionView: "{G}" [grouped]
              - Group header (each group):
                - Label: "{Key}"
              - Each item:
                - Label: "{Val}"
              - Group footer (each group):
                - Label: "---"
            """,
            md);
    }

    [Fact]
    public void BindableLayout_WithCondition()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <VerticalStackLayout BindableLayout.ItemsSource="{Binding Items}" IsVisible="{Binding HasItems}">
                    <BindableLayout.ItemTemplate>
                        <DataTemplate><Label Text="{Binding Name}" /></DataTemplate>
                    </BindableLayout.ItemTemplate>
                </VerticalStackLayout>
                """)));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - VerticalStackLayout with items from "{Items}" [visible when HasItems = true]:
              - Each item:
                - Label: "{Name}"
            """,
            md);
    }

    [Fact]
    public void CrossFile_UnresolvedControl_KeptAsPlaceholder()
    {
        // Unresolved user controls are now kept as placeholders
        var page = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         xmlns:v="clr-namespace:MyApp.Views"
                         x:Class="MyApp.TestPage">
                <Label Text="Before" />
                <v:MissingWidget />
                <Label Text="After" />
            </ContentPage>
            """;

        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", page));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Label: "Before"
            - [MissingWidget]:
            - Label: "After"
            """,
            md);
    }

    [Fact]
    public void MultiplePages_AggregateContainsAll()
    {
        var p1 = Page("A.P1", "<Label Text=\"One\" />");
        var p2 = Page("A.P2", "<Label Text=\"Two\" />");

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("P1.xaml", p1), ("P2.xaml", p2));

        Assert.Contains(sources.Keys, k => k.Contains("P1_Indexed"));
        Assert.Contains(sources.Keys, k => k.Contains("P2_Indexed"));

        // Aggregate class follows {AssemblyName}IndexedPageCatalog pattern
        var aggKey = sources.Keys.FirstOrDefault(k => k.Contains("IndexedPageCatalog.g.cs") && !k.Contains("P1") && !k.Contains("P2"));
        Assert.NotNull(aggKey);

        var agg = sources[aggKey!];
        Assert.Contains("IndexedPageCatalog", agg); // inherits from base
        Assert.Contains("Default", agg); // has Default singleton
        Assert.Contains("Pages", agg); // has Pages override
        Assert.Contains("global::A.P1_Indexed.Markdown", agg);
        Assert.Contains("global::A.P2_Indexed.Markdown", agg);
    }

    [Fact]
    public void Shell_FlyoutItem()
    {
        var xaml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                   xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                   x:Class="MyApp.AppShell">
                <FlyoutItem Title="Dashboard">
                    <ShellContent Route="dash" Title="Dashboard" />
                </FlyoutItem>
            </Shell>
            """;
        var md = GeneratorTestHarness.GetMarkdown("AppShell",
            ("AppShell.xaml", xaml));

        Assert.Equal(
            """
            # AppShell

            File: AppShell.xaml

            - ShellContent: "Dashboard"
            """,
            md);
    }

    [Fact]
    public void Shell_TabWithNestedContent()
    {
        var xaml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                   xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                   x:Class="MyApp.AppShell">
                <TabBar>
                    <Tab Title="Browse" Route="browse">
                        <ShellContent Route="catalog" Title="Catalog" />
                        <ShellContent Route="search" Title="Search" />
                    </Tab>
                </TabBar>
            </Shell>
            """;
        var md = GeneratorTestHarness.GetMarkdown("AppShell",
            ("AppShell.xaml", xaml));

        Assert.Equal(
            """
            # AppShell

            File: AppShell.xaml

            - Tab: "Browse"
              - ShellContent: "Catalog"
              - ShellContent: "Search"
            """,
            md);
    }

    [Fact]
    public void Shell_RoutesAreCatalogMetadata_NotSemanticMarkdown()
    {
        var shell = """
            <?xml version="1.0" encoding="utf-8" ?>
            <Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                   xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                   xmlns:pages="clr-namespace:MyApp.Pages"
                   x:Class="MyApp.AppShell">
                <TabBar Route="main">
                    <ShellContent Route="home"
                                  Title="Home"
                                  ContentTemplate="{DataTemplate pages:HomePage}" />
                    <ShellContent Route="orders"
                                  Title="Orders"
                                  ContentTemplate="{DataTemplate pages:OrdersPage}" />
                    <ShellContent Route="alternate-home"
                                  Title="Alternate Home"
                                  ContentTemplate="{DataTemplate pages:HomePage}" />
                </TabBar>
            </Shell>
            """;
        var home = Page("MyApp.Pages.HomePage", """<Label Text="Welcome" />""");
        var orders = Page("MyApp.Pages.OrdersPage", """<Label Text="Past Orders" />""");

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("AppShell.xaml", shell),
            ("Pages/HomePage.xaml", home),
            ("Pages/OrdersPage.xaml", orders));
        var aggregate = sources["IndexedPageCatalog.g.cs"];
        var shellMarkdown = GeneratorTestHarness.GetMarkdown(
            "AppShell",
            ("AppShell.xaml", shell),
            ("Pages/HomePage.xaml", home),
            ("Pages/OrdersPage.xaml", orders));

        Assert.Contains(
            "\"MyApp.Pages.HomePage\", global::MyApp.Pages.HomePage_Indexed.Title, global::MyApp.Pages.HomePage_Indexed.Elements)",
            aggregate);
        Assert.Contains(
            "\"MyApp.Pages.OrdersPage\", global::MyApp.Pages.OrdersPage_Indexed.Title, global::MyApp.Pages.OrdersPage_Indexed.Elements)",
            aggregate);
        Assert.DoesNotContain("//main", shellMarkdown);
    }

    [Fact]
    public void ShellItemAndShellSection_ContributeRouteSegments()
    {
        var shell = """
            <?xml version="1.0" encoding="utf-8" ?>
            <Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                   xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                   xmlns:pages="clr-namespace:MyApp.Pages"
                   x:Class="MyApp.AppShell">
                <ShellItem Route="shop">
                    <ShellSection Route="browse">
                        <ShellContent Route="products"
                                      ContentTemplate="{DataTemplate pages:ProductsPage}" />
                    </ShellSection>
                </ShellItem>
            </Shell>
            """;
        var products = Page(
            "MyApp.Pages.ProductsPage",
            """<Label Text="Products" />""");

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("AppShell.xaml", shell),
            ("Pages/ProductsPage.xaml", products));

        Assert.Contains(
            """new string[] { "//shop/browse/products" }""",
            sources["IndexedPageCatalog.g.cs"]);
    }

    [Fact]
    public void ShellRouteMetadata_UsesQualifiedPageIdentity()
    {
        var shell = """
            <?xml version="1.0" encoding="utf-8" ?>
            <Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                   xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                   xmlns:a="clr-namespace:AreaA"
                   x:Class="MyApp.AppShell">
                <ShellContent Route="settings"
                              ContentTemplate="{DataTemplate a:SettingsPage}" />
            </Shell>
            """;
        var areaA = Page("AreaA.SettingsPage", """<Label Text="Area A" />""");
        var areaB = Page("AreaB.SettingsPage", """<Label Text="Area B" />""");

        var aggregate = GeneratorTestHarness.GetGeneratedSources(
            ("AppShell.xaml", shell),
            ("AreaA/SettingsPage.xaml", areaA),
            ("AreaB/SettingsPage.xaml", areaB))["IndexedPageCatalog.g.cs"];

        Assert.Contains(
            "\"AreaA.SettingsPage\", global::AreaA.SettingsPage_Indexed.Title, global::AreaA.SettingsPage_Indexed.Elements)",
            aggregate);
        Assert.Contains(
            "\"AreaB.SettingsPage\", global::AreaB.SettingsPage_Indexed.Title, global::AreaB.SettingsPage_Indexed.Elements)",
            aggregate);
        Assert.Contains(
            """public override string? EntryPageTypeName => "AreaA.SettingsPage";""",
            aggregate);
    }

    [Fact]
    public void IndexedPageCatalog_HomeUsesQualifiedTypeIdentity()
    {
        var catalog = new DuplicateNameCatalog();

        var home = catalog.Home;

        Assert.NotNull(home);
        Assert.Equal("AreaB.HomePage", home.TypeName);
        Assert.Equal("# Area B", home.Markdown);
    }

    private sealed class DuplicateNameCatalog : IndexedPageCatalog
    {
        private static readonly IndexedPage[] PagesValue =
        [
            new("HomePage", "AreaA/HomePage.xaml", "# Area A", [], "AreaA.HomePage"),
            new("HomePage", "AreaB/HomePage.xaml", "# Area B", [], "AreaB.HomePage"),
        ];

        public override IReadOnlyList<IndexedPage> Pages => PagesValue;

        public override string? EntryPageName => "HomePage";

        public override string? EntryPageTypeName => "AreaB.HomePage";
    }

    [Fact]
    public void SelfBindingDot()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Label Text=\"{Binding}\" />")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Label: "{.}"
            """,
            md);
    }

    [Fact]
    public void Editor_WithPlaceholder()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Editor Placeholder=\"Write here\" />")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Editor: [placeholder: "Write here"]
            """,
            md);
    }

    [Fact]
    public void Editor_WithBoundText_AndPlaceholder()
    {
        // The important case: a bound value AND a placeholder — both must appear.
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """<Editor Text="{Binding Comment}" Placeholder="Write your review" />""")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Editor: "{Comment}" [placeholder: "Write your review"]
            """,
            md);
    }

    [Fact]
    public void Entry_Placeholder_BeforeHint()
    {
        // Placeholder is the visible label, so it comes first in the bracket group.
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """<Entry Text="{Binding Note}" Placeholder="Add a note" SemanticProperties.Hint="More context" />""")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Entry: "{Note}" [placeholder: "Add a note", hint: More context]
            """,
            md);
    }

    [Fact]
    public void Entry_Placeholder_WithVisibilityCondition()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """<Entry Text="{Binding Coupon}" Placeholder="Coupon code" IsVisible="{Binding HasCoupon}" />""")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Entry: "{Coupon}" [placeholder: "Coupon code", visible when HasCoupon = true]
            """,
            md);
    }

    [Fact]
    public void InvalidXaml_NullResult()
    {
        var md = GeneratorTestHarness.GetMarkdown("Bad",
            ("Bad.xaml", "not xml"));
        Assert.Null(md);
    }

    [Fact]
    public void EmptyString_NullResult()
    {
        var md = GeneratorTestHarness.GetMarkdown("E",
            ("E.xaml", ""));
        Assert.Null(md);
    }

    [Fact]
    public void HeadingLevel_AsNumber()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Label Text=\"Section\" SemanticProperties.HeadingLevel=\"3\" />")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Heading (level 3): "Section"
            """,
            md);
    }

    [Fact]
    public void Picker_WithoutTitle()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Picker SelectedItem=\"{Binding Choice}\" />")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Picker:  → "{Choice}"
            """,
            md);
    }

    [Fact]
    public void Button_WithCommandParameter()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                "<Button Text=\"Delete\" Command=\"{Binding DeleteCommand}\" CommandParameter=\"{Binding Id}\" />")));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Button: "Delete" → DeleteCommand
            """,
            md);
    }

    [Fact]
    public void GraphicsView_PaintedPixelsAreNotSemanticContent()
    {
        var md = GeneratorTestHarness.GetMarkdown(
            "OrdersPage",
            ("Pages/OrdersPage.xaml", Page(
                "MyApp.OrdersPage",
                """
                <Label Text="Orders" SemanticProperties.HeadingLevel="Level1" />
                <GraphicsView AutomationId="OrderInsightsCharts" />
                """)));

        Assert.Equal(
            """
            # OrdersPage

            File: OrdersPage.xaml

            - Heading (level 1): "Orders"
            """,
            md);
    }

    [Fact]
    public void GraphicsView_DescribedWithAutomationId_IsCapturableSemanticContent()
    {
        var md = GeneratorTestHarness.GetMarkdown(
            "OrdersPage",
            ("Pages/OrdersPage.xaml", Page(
                "MyApp.OrdersPage",
                """
                <GraphicsView AutomationId="SpendingChart"
                              SemanticProperties.Description="Where the money went chart" />
                """)));

        Assert.Equal(
            """
            # OrdersPage

            File: OrdersPage.xaml

            - GraphicsView: "Where the money went chart" [automationId: "SpendingChart"]
            """,
            md);
    }

    [Fact]
    public void CrossFile_VisualGroup_ExposesParentAndChildAutomationIds()
    {
        var page = Page(
            "MyApp.OrdersPage",
            """
            <views:OrderInsightsView
                AutomationId="OrderInsightsCharts"
                SemanticProperties.Description="Order insights charts" />
            """,
            "xmlns:views=\"clr-namespace:MyApp.Views\"");
        var visualGroup = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         x:Class="MyApp.Views.OrderInsightsView">
                <GraphicsView
                    AutomationId="SpendingChart"
                    SemanticProperties.Description="Where the money went chart" />
                <GraphicsView
                    AutomationId="ProductsChart"
                    SemanticProperties.Description="Most popular products chart" />
            </ContentView>
            """;

        var md = GeneratorTestHarness.GetMarkdown(
            "OrdersPage",
            ("Pages/OrdersPage.xaml", page),
            ("Views/OrderInsightsView.xaml", visualGroup));

        Assert.Equal(
            """
            # OrdersPage

            File: OrdersPage.xaml

            - [OrderInsightsView]: "Order insights charts" [automationId: "OrderInsightsCharts"]
              - GraphicsView: "Where the money went chart" [automationId: "SpendingChart"]
              - GraphicsView: "Most popular products chart" [automationId: "ProductsChart"]
            """,
            md);
    }

    [Fact]
    public void StructuredElements_UseKnownTypesHeadingAndDirectTapGesture()
    {
        var xaml = Page(
            "MyApp.TestPage",
            """
            <Label Text="Title" SemanticProperties.HeadingLevel="Level1" />
            <Border SemanticProperties.Description="Open details">
                <Border.GestureRecognizers>
                    <TapGestureRecognizer Tapped="OnTapped" />
                </Border.GestureRecognizers>
            </Border>
            <Entry Placeholder="Name" />
            """);

        var source = GeneratorTestHarness.GetGeneratedSources(
            ("TestPage.xaml", xaml))["MyApp_TestPage_Indexed.g.cs"];

        Assert.Contains("IndexedElementKind.Heading", source);
        Assert.Contains("IndexedElementKind.Action", source);
        Assert.Contains("IndexedElementKind.Input", source);
        Assert.Contains("""public const string? Title = "Title";""", source);
    }
}
