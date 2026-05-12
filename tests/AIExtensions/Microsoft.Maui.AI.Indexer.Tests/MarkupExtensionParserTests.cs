namespace Microsoft.Maui.AI.Indexer.Tests;

/// <summary>Tests for markup extension parsing — bindings, converters, StringFormat, etc.</summary>
public class MarkupExtensionParserTests
{
    [Fact]
    public void Parse_SimpleBinding()
    {
        var xaml = MakeXaml("<Label Text=\"{Binding UserName}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("\"{UserName}\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Parse_BindingWithPath()
    {
        var xaml = MakeXaml("<Label Text=\"{Binding Path=Item.Name}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("\"{Item.Name}\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Parse_BindingWithMode()
    {
        var xaml = MakeXaml("<Entry Text=\"{Binding Email, Mode=TwoWay}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("\"{Email}\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Parse_BindingWithConverter()
    {
        var xaml = MakeXaml("<Label Text=\"{Binding Price, Converter={StaticResource CurrencyConverter}}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("\"{Price}\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Parse_BindingWithStringFormat()
    {
        var xaml = MakeXaml("<Label Text=\"{Binding Total, StringFormat='Total: {0}'}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("\"{Total}\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Parse_SelfBinding()
    {
        var xaml = MakeXaml("<Label Text=\"{Binding}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("\"{.}\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Parse_LiteralTextNotBinding()
    {
        var xaml = MakeXaml("<Label Text=\"Hello World\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("\"Hello World\"", sources["TestPage_UiIndex.g.cs"]);
        Assert.DoesNotContain("{", sources["TestPage_UiIndex.g.cs"].Split("Markdown")[1].Split("PageName")[0]);
    }

    [Fact]
    public void Parse_TemplateBinding()
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
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("\"{Name}\"", sources["TestPage_UiIndex.g.cs"]);
    }

    private static string MakeXaml(string content) => $@"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    {content}
</ContentPage>";
}
