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

        Assert.True(sources.Keys.Any(k => k.Contains("P1_UiIndex")));
        Assert.True(sources.Keys.Any(k => k.Contains("P2_UiIndex")));

        // Aggregate class follows {AssemblyName}UiIndex pattern
        var aggKey = sources.Keys.FirstOrDefault(k => k.Contains("UiIndex.g.cs") && !k.Contains("P1") && !k.Contains("P2"));
        Assert.NotNull(aggKey);

        var agg = sources[aggKey!];
        Assert.Contains("UiPageIndex", agg); // inherits from base
        Assert.Contains("Default", agg); // has Default singleton
        Assert.Contains("Pages", agg); // has Pages override
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
}
