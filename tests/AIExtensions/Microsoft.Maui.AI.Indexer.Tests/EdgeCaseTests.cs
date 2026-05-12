namespace Microsoft.Maui.AI.Indexer.Tests;

/// <summary>Tests targeting specific coverage gaps in edge cases.</summary>
public class EdgeCaseTests
{
    [Fact]
    public void AccessibilityExtractor_HeadingLevelAsNumber()
    {
        // Tests plain number parsing for HeadingLevel
        var xaml = MakeXaml("<Label Text=\"Section\" SemanticProperties.HeadingLevel=\"3\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("Heading (level 3)", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void AccessibilityExtractor_InvalidHeadingLevel()
    {
        // Invalid heading level falls back to no heading
        var xaml = MakeXaml("<Label Text=\"Normal\" SemanticProperties.HeadingLevel=\"None\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("Label: \"Normal\"", sources["TestPage_UiIndex.g.cs"]);
        Assert.DoesNotContain("Heading", sources["TestPage_UiIndex.g.cs"].Substring(sources["TestPage_UiIndex.g.cs"].IndexOf("Markdown")));
    }

    [Fact]
    public void AccessibilityExtractor_HintWithDescription()
    {
        var xaml = MakeXaml("<Image Source=\"logo.png\" SemanticProperties.Description=\"Company logo\" SemanticProperties.Hint=\"Decorative image\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        var src = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("\"Company logo\"", src);
        Assert.Contains("[hint: Decorative image]", src);
    }

    [Fact]
    public void BindingInfo_ToDisplayString_WithRawFallback()
    {
        // StaticResource expressions that aren't bindings
        var xaml = MakeXaml("<Label Text=\"{StaticResource AppTitle}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        // Should show the raw markup expression
        Assert.Contains("StaticResource", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void MarkupExtensionParser_BindingWithRelativeSource()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <CollectionView ItemsSource=""{Binding Items}"">
        <CollectionView.ItemTemplate>
            <DataTemplate>
                <Button Text=""Add"" Command=""{Binding Source={RelativeSource AncestorType={x:Type ContentPage}}, Path=BindingContext.AddCommand}"" />
            </DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>
</ContentPage>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        // Should parse even complex bindings
        Assert.Contains("Button: \"Add\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void MarkdownBuilder_MultipleCollectionViews()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <CollectionView ItemsSource=""{Binding Active}"">
        <CollectionView.ItemTemplate>
            <DataTemplate><Label Text=""{Binding Title}"" /></DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>
    <CollectionView ItemsSource=""{Binding Archived}"">
        <CollectionView.ItemTemplate>
            <DataTemplate><Label Text=""{Binding Title}"" /></DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>
</ContentPage>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        var src = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("\"{Active}\"", src);
        Assert.Contains("\"{Archived}\"", src);
    }

    [Fact]
    public void MarkdownBuilder_CollectionViewWithHeaderAndFooter()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <CollectionView ItemsSource=""{Binding Items}"">
        <CollectionView.HeaderTemplate>
            <DataTemplate><Label Text=""Top of list"" /></DataTemplate>
        </CollectionView.HeaderTemplate>
        <CollectionView.ItemTemplate>
            <DataTemplate><Label Text=""{Binding Name}"" /></DataTemplate>
        </CollectionView.ItemTemplate>
        <CollectionView.FooterTemplate>
            <DataTemplate><Label Text=""End of list"" /></DataTemplate>
        </CollectionView.FooterTemplate>
    </CollectionView>
</ContentPage>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        var src = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("Header:", src);
        Assert.Contains("\"Top of list\"", src);
        Assert.Contains("Footer:", src);
        Assert.Contains("\"End of list\"", src);
    }

    [Fact]
    public void MarkdownBuilder_GroupedWithGroupFooter()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <CollectionView ItemsSource=""{Binding Groups}"" IsGrouped=""True"">
        <CollectionView.GroupHeaderTemplate>
            <DataTemplate><Label Text=""{Binding Name}"" /></DataTemplate>
        </CollectionView.GroupHeaderTemplate>
        <CollectionView.ItemTemplate>
            <DataTemplate><Label Text=""{Binding Item}"" /></DataTemplate>
        </CollectionView.ItemTemplate>
        <CollectionView.GroupFooterTemplate>
            <DataTemplate><Label Text=""End of group"" /></DataTemplate>
        </CollectionView.GroupFooterTemplate>
    </CollectionView>
</ContentPage>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        var src = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("Group footer (each group):", src);
        Assert.Contains("\"End of group\"", src);
    }

    [Fact]
    public void PageCodeEmitter_PageWithRoute()
    {
        // ShellContent should propagate route info (currently routes come from Shell only)
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<Shell xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
       xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
       x:Class=""MyApp.AppShell"">
    <ShellContent Route=""home"" Title=""Home"" />
</Shell>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("AppShell.xaml", xaml));
        Assert.Contains("AppShell_UiIndex", sources["AppShell_UiIndex.g.cs"]);
    }

    [Fact]
    public void PageCodeEmitter_EmptyNamespace()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""RootPage"">
    <Label Text=""No namespace"" />
</ContentPage>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("RootPage.xaml", xaml));
        var src = sources["RootPage_UiIndex.g.cs"];
        Assert.DoesNotContain("namespace", src.Split("class")[0]);
    }

    [Fact]
    public void XamlFileParser_NestedLayouts()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <ScrollView>
        <Grid>
            <VerticalStackLayout>
                <HorizontalStackLayout>
                    <FlexLayout>
                        <AbsoluteLayout>
                            <Frame>
                                <Label Text=""Deep inside"" />
                            </Frame>
                        </AbsoluteLayout>
                    </FlexLayout>
                </HorizontalStackLayout>
            </VerticalStackLayout>
        </Grid>
    </ScrollView>
</ContentPage>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        var src = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("\"Deep inside\"", src);
        // None of the layout containers should appear
        var md = src.Substring(src.IndexOf("Markdown"));
        Assert.DoesNotContain("ScrollView", md);
        Assert.DoesNotContain("Grid", md);
        Assert.DoesNotContain("FlexLayout", md);
        Assert.DoesNotContain("AbsoluteLayout", md);
        Assert.DoesNotContain("Frame", md);
    }

    [Fact]
    public void XamlFileParser_SkipsResourceDictionary()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <ContentPage.Resources>
        <ResourceDictionary>
            <Style TargetType=""Label"">
                <Setter Property=""FontSize"" Value=""14"" />
            </Style>
        </ResourceDictionary>
    </ContentPage.Resources>
    <Label Text=""Visible"" />
</ContentPage>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        var src = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("\"Visible\"", src);
        Assert.DoesNotContain("ResourceDictionary", src.Substring(src.IndexOf("Markdown")));
    }

    [Fact]
    public void XamlFileParser_UnknownCustomControl()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             xmlns:local=""clr-namespace:MyApp""
             x:Class=""MyApp.TestPage"">
    <local:CustomWidget>
        <Label Text=""Inside custom"" />
    </local:CustomWidget>
</ContentPage>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        var src = sources["TestPage_UiIndex.g.cs"];
        // Should walk into unknown control and find the Label
        Assert.Contains("\"Inside custom\"", src);
    }

    [Fact]
    public void MarkdownBuilder_BindableLayoutConditional()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <VerticalStackLayout BindableLayout.ItemsSource=""{Binding Items}"" IsVisible=""{Binding HasItems}"">
        <BindableLayout.ItemTemplate>
            <DataTemplate><Label Text=""{Binding Name}"" /></DataTemplate>
        </BindableLayout.ItemTemplate>
    </VerticalStackLayout>
</ContentPage>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        var src = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("visible when HasItems = true", src);
        Assert.Contains("items from \"{Items}\"", src);
    }

    [Fact]
    public void MarkdownBuilder_SliderWithSemanticDescription()
    {
        var xaml = MakeXaml("<Slider Minimum=\"0\" Maximum=\"100\" Value=\"{Binding Volume}\" SemanticProperties.Description=\"Volume control\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        // Description overrides the range display
        Assert.Contains("\"Volume control\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void MarkdownBuilder_PickerWithoutTitle()
    {
        var xaml = MakeXaml("<Picker SelectedItem=\"{Binding Choice}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("Picker:", sources["TestPage_UiIndex.g.cs"]);
        Assert.Contains("{Choice}", sources["TestPage_UiIndex.g.cs"]);
    }

    private static string MakeXaml(string content) => $@"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    {content}
</ContentPage>";
}
