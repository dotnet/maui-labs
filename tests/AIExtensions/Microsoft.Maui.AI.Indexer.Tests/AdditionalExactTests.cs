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
        Assert.Equal("# T\n\nFile: T.xaml\n\n- CheckBox: \"{Agreed}\"", md);
    }

    [Fact]
    public void RadioButton_WithContent()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<RadioButton Content=\"Option A\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- RadioButton: \"Option A\"", md);
    }

    [Fact]
    public void DatePicker_WithBinding()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<DatePicker Date=\"{Binding Delivery}\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- DatePicker: \"{Delivery}\"", md);
    }

    [Fact]
    public void TimePicker_WithBinding()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<TimePicker Time=\"{Binding SelectedTime}\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- TimePicker: \"{SelectedTime}\"", md);
    }

    [Fact]
    public void SearchBar_WithPlaceholder()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<SearchBar Placeholder=\"Search...\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- SearchBar: placeholder \"Search...\"", md);
    }

    [Fact]
    public void Stepper_WithRange()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Stepper Minimum=\"0\" Maximum=\"10\" Value=\"{Binding Qty}\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Stepper: 0–10 → \"{Qty}\"", md);
    }

    [Fact]
    public void ActivityIndicator_WithBinding()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<ActivityIndicator IsRunning=\"{Binding IsBusy}\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- ActivityIndicator: \"{IsBusy}\"", md);
    }

    [Fact]
    public void ProgressBar_WithBinding()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<ProgressBar Progress=\"{Binding Download}\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- ProgressBar: \"{Download}\"", md);
    }

    [Fact]
    public void ImageButton_WithSource()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<ImageButton Source=\"heart.png\" Command=\"{Binding Like}\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- ImageButton: \"heart.png\" → Like", md);
    }

    [Fact]
    public void SemanticDescription_OverridesSlider()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """<Slider Minimum="0" Maximum="100" SemanticProperties.Description="Volume" SemanticProperties.Hint="Adjust volume" />""")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Slider: \"Volume\" [hint: Adjust volume]", md);
    }

    [Fact]
    public void PromotedBorder_WithDescription()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """<Border SemanticProperties.Description="Product card"><Label Text="Inside" /></Border>""")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Border: \"Product card\"", md);
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
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"Discount\" [visible when HasDiscount = True]", md);
    }

    [Fact]
    public void NegateConverter()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """<Button Text="Go" IsVisible="{Binding IsBusy, Converter={StaticResource NegateBoolConverter}}" />""")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Button: \"Go\" [visible when IsBusy = false]", md);
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
    public void CrossFile_UnresolvedControl_Omitted()
    {
        // When a user control isn't found in the parsed files, it's omitted
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

        // Unresolved user controls are kept as markers (no inlined content)
        // But if the control isn't found, it remains as a placeholder
        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Label: "Before"
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

        Assert.True(sources.ContainsKey("P1_UiIndex.g.cs"));
        Assert.True(sources.ContainsKey("P2_UiIndex.g.cs"));
        Assert.True(sources.ContainsKey("UiIndex.g.cs"));

        var agg = sources["UiIndex.g.cs"];
        Assert.Contains("Search(string query)", agg);
        Assert.Contains("FindByRoute(string route)", agg);
        Assert.Contains("FindByName(string name)", agg);
        Assert.Contains("AllMarkdown", agg);
        Assert.Contains("global::A.P1_UiIndex.Markdown", agg);
        Assert.Contains("global::A.P2_UiIndex.Markdown", agg);
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

            - ShellContent: "Dashboard" [route: dash]
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

            - Tab: "Browse" [route: browse]
              - ShellContent: "Catalog" [route: catalog]
              - ShellContent: "Search" [route: search]
            """,
            md);
    }

    [Fact]
    public void SelfBindingDot()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Label Text=\"{Binding}\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"{.}\"", md);
    }

    [Fact]
    public void Editor_WithPlaceholder()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Editor Placeholder=\"Write here\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Editor: placeholder \"Write here\"", md);
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
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Heading (level 3): \"Section\"", md);
    }

    [Fact]
    public void Picker_WithoutTitle()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Picker SelectedItem=\"{Binding Choice}\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Picker:  → \"{Choice}\"", md);
    }

    [Fact]
    public void Button_WithCommandParameter()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                "<Button Text=\"Delete\" Command=\"{Binding DeleteCommand}\" CommandParameter=\"{Binding Id}\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Button: \"Delete\" → DeleteCommand", md);
    }
}
