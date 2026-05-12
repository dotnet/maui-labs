namespace Microsoft.Maui.AI.Indexer.Tests;

/// <summary>Tests for the aggregate UiIndex search and discovery features.</summary>
public class AggregateIndexTests
{
    [Fact]
    public void Search_FindsPageByContent()
    {
        var page1 = MakeXaml("MyApp.CartPage", "<Button Text=\"Checkout\" />");
        var page2 = MakeXaml("MyApp.ProfilePage", "<Label Text=\"Username\" />");

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("CartPage.xaml", page1),
            ("ProfilePage.xaml", page2));

        var aggregate = sources["UiIndex.g.cs"];
        Assert.Contains("Search(string query)", aggregate);
        Assert.Contains("CartPage", aggregate);
        Assert.Contains("ProfilePage", aggregate);
    }

    [Fact]
    public void FindByRoute_GeneratesMethod()
    {
        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("Test.xaml", MakeXaml("MyApp.TestPage", "<Label Text=\"Hello\" />")));

        var aggregate = sources["UiIndex.g.cs"];
        Assert.Contains("FindByRoute(string route)", aggregate);
    }

    [Fact]
    public void FindByName_GeneratesMethod()
    {
        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("Test.xaml", MakeXaml("MyApp.TestPage", "<Label Text=\"Hello\" />")));

        var aggregate = sources["UiIndex.g.cs"];
        Assert.Contains("FindByName(string name)", aggregate);
    }

    [Fact]
    public void AllMarkdown_GeneratesProperty()
    {
        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("Test.xaml", MakeXaml("MyApp.TestPage", "<Label Text=\"Hello\" />")));

        var aggregate = sources["UiIndex.g.cs"];
        Assert.Contains("AllMarkdown", aggregate);
    }

    [Fact]
    public void PageEntry_HasAllFields()
    {
        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("Pages/MyPage.xaml", MakeXaml("MyApp.Pages.MyPage", "<Label Text=\"Hello\" />")));

        var aggregate = sources["UiIndex.g.cs"];
        Assert.Contains("\"MyPage\"", aggregate);
        Assert.Contains("global::MyApp.Pages.MyPage_UiIndex.Markdown", aggregate);
    }

    [Fact]
    public void Aggregate_UsesFullyQualifiedNames()
    {
        var page1 = MakeXaml("MyApp.Pages.Page1", "<Label Text=\"One\" />");
        var page2 = MakeXaml("MyApp.Views.View1", "<Label Text=\"Two\" />");

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("Pages/Page1.xaml", page1),
            ("Views/View1.xaml", page2));

        var aggregate = sources["UiIndex.g.cs"];
        Assert.Contains("global::MyApp.Pages.Page1_UiIndex", aggregate);
        Assert.Contains("global::MyApp.Views.View1_UiIndex", aggregate);
    }

    private static string MakeXaml(string xClass, string content) => $@"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""{xClass}"">
    {content}
</ContentPage>";
}
