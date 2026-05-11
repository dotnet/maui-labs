namespace Microsoft.Maui.AI.Indexer.Tests;

public class XamlFileParserTests
{
    [Fact]
    public void Parse_SimplePage_ExtractsClassAndNamespace()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.Pages.TestPage"">
    <Label Text=""Hello"" />
</ContentPage>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("Pages/TestPage.xaml", xaml));

        Assert.True(sources.ContainsKey("TestPage_UiIndex.g.cs"));
        var source = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("namespace MyApp.Pages;", source);
        Assert.Contains("TestPage_UiIndex", source);
        Assert.Contains("# TestPage", source);
        Assert.Contains("- Label: \"Hello\"", source);
    }

    [Fact]
    public void Parse_SkipsLayoutContainers()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <Grid>
        <VerticalStackLayout>
            <Border>
                <Label Text=""Inside layouts"" />
            </Border>
        </VerticalStackLayout>
    </Grid>
</ContentPage>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("TestPage.xaml", xaml));

        var source = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("- Label: \"Inside layouts\"", source);
        Assert.DoesNotContain("Grid", source.Substring(source.IndexOf("Markdown")));
        Assert.DoesNotContain("VerticalStackLayout", source.Substring(source.IndexOf("Markdown")));
        Assert.DoesNotContain("Border", source.Substring(source.IndexOf("Markdown")));
    }

    [Fact]
    public void Parse_ExtractsBindings()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <Label Text=""{Binding UserName}"" />
</ContentPage>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("TestPage.xaml", xaml));

        var source = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("\"{UserName}\"", source);
    }

    [Fact]
    public void Parse_ExtractsButtonCommand()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <Button Text=""Save"" Command=""{Binding SaveCommand}"" />
</ContentPage>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("TestPage.xaml", xaml));

        var source = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("Button: \"Save\" → SaveCommand", source);
    }

    [Fact]
    public void Parse_ExtractsSemanticDescription()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <Button Text=""◄"" SemanticProperties.Description=""Back"" SemanticProperties.Hint=""Returns to previous page"" />
</ContentPage>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("TestPage.xaml", xaml));

        var source = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("Button: \"Back\"", source);
        Assert.Contains("[hint: Returns to previous page]", source);
    }

    [Fact]
    public void Parse_ExtractsHeadingLevel()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <Label Text=""Welcome"" SemanticProperties.HeadingLevel=""Level1"" />
</ContentPage>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("TestPage.xaml", xaml));

        var source = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("Heading (level 1): \"Welcome\"", source);
    }

    [Fact]
    public void Parse_SkipsDecorativeElements()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <Label Text=""🌿"" SemanticProperties.Description="""" />
    <Label Text=""Visible"" />
</ContentPage>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("TestPage.xaml", xaml));

        var source = sources["TestPage_UiIndex.g.cs"];
        Assert.DoesNotContain("🌿", source);
        Assert.Contains("- Label: \"Visible\"", source);
    }

    [Fact]
    public void Parse_CollectionViewWithItemTemplate()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <CollectionView ItemsSource=""{Binding Items}"">
        <CollectionView.ItemTemplate>
            <DataTemplate>
                <Label Text=""{Binding Name}"" />
            </DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>
</ContentPage>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("TestPage.xaml", xaml));

        var source = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("CollectionView: \"{Items}\"", source);
        Assert.Contains("Each item:", source);
        Assert.Contains("\"{Name}\"", source);
    }

    [Fact]
    public void Parse_GroupedCollectionView()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <CollectionView ItemsSource=""{Binding Groups}"" IsGrouped=""True"">
        <CollectionView.GroupHeaderTemplate>
            <DataTemplate>
                <Label Text=""{Binding CategoryName}"" SemanticProperties.HeadingLevel=""Level2"" />
            </DataTemplate>
        </CollectionView.GroupHeaderTemplate>
        <CollectionView.ItemTemplate>
            <DataTemplate>
                <Label Text=""{Binding Name}"" />
            </DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>
</ContentPage>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("TestPage.xaml", xaml));

        var source = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("grouped", source);
        Assert.Contains("Group header (each group):", source);
        Assert.Contains("Heading (level 2): \"{CategoryName}\"", source);
        Assert.Contains("Each item:", source);
    }

    [Fact]
    public void Parse_BindableLayout()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <VerticalStackLayout BindableLayout.ItemsSource=""{Binding Reviews}"">
        <BindableLayout.ItemTemplate>
            <DataTemplate>
                <Label Text=""{Binding Comment}"" />
            </DataTemplate>
        </BindableLayout.ItemTemplate>
    </VerticalStackLayout>
</ContentPage>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("TestPage.xaml", xaml));

        var source = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("VerticalStackLayout with items from \"{Reviews}\"", source);
        Assert.Contains("Each item:", source);
        Assert.Contains("\"{Comment}\"", source);
    }

    [Fact]
    public void Parse_ConditionalVisibility()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <Label Text=""Admin Panel"" IsVisible=""{Binding IsAdmin}"" />
</ContentPage>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("TestPage.xaml", xaml));

        var source = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("visible when IsAdmin = true", source);
    }

    [Fact]
    public void Parse_SliderWithBinding()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <Slider Minimum=""1"" Maximum=""5"" Value=""{Binding Rating}"" />
</ContentPage>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("TestPage.xaml", xaml));

        var source = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("Slider: 1–5 → \"{Rating}\"", source);
    }

    [Fact]
    public void Parse_EntryWithPlaceholder()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <Entry Placeholder=""Type here..."" />
</ContentPage>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("TestPage.xaml", xaml));

        var source = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("Entry: placeholder \"Type here...\"", source);
    }

    [Fact]
    public void Parse_MultiplePages_GeneratesAggregateIndex()
    {
        var page1 = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.Page1"">
    <Label Text=""Page 1"" />
</ContentPage>";

        var page2 = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.Page2"">
    <Button Text=""Click me"" />
</ContentPage>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("Pages/Page1.xaml", page1),
            ("Pages/Page2.xaml", page2));

        Assert.True(sources.ContainsKey("Page1_UiIndex.g.cs"));
        Assert.True(sources.ContainsKey("Page2_UiIndex.g.cs"));
        Assert.True(sources.ContainsKey("UiIndex.g.cs"));

        var aggregate = sources["UiIndex.g.cs"];
        Assert.Contains("Search(string query)", aggregate);
        Assert.Contains("FindByRoute(string route)", aggregate);
        Assert.Contains("FindByName(string name)", aggregate);
        Assert.Contains("Page1", aggregate);
        Assert.Contains("Page2", aggregate);
    }

    [Fact]
    public void Parse_SkipsXamlWithoutXClass()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ResourceDictionary xmlns=""http://schemas.microsoft.com/dotnet/2021/maui"">
    <Color x:Key=""Primary"">#512BD4</Color>
</ResourceDictionary>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("Resources/Colors.xaml", xaml));

        // Should only generate UiIndex.g.cs (empty pages array)
        // No page-specific file
        Assert.DoesNotContain(sources.Keys, k => k.Contains("Colors"));
    }

    [Fact]
    public void Parse_ShellRoutes()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<Shell xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
       xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
       x:Class=""MyApp.AppShell"">
    <TabBar>
        <ShellContent Route=""home"" Title=""Home"" />
        <ShellContent Route=""settings"" Title=""Settings"" />
    </TabBar>
</Shell>";

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("AppShell.xaml", xaml));

        var source = sources["AppShell_UiIndex.g.cs"];
        Assert.Contains("ShellContent: \"Home\" [route: home]", source);
        Assert.Contains("ShellContent: \"Settings\" [route: settings]", source);
    }
}
