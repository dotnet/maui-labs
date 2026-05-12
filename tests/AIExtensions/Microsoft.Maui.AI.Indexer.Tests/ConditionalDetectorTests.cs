namespace Microsoft.Maui.AI.Indexer.Tests;

/// <summary>Tests for conditional visibility detection.</summary>
public class ConditionalDetectorTests
{
    [Fact]
    public void Detect_IsVisibleBinding()
    {
        var xaml = MakeXaml("<Label Text=\"Admin\" IsVisible=\"{Binding IsAdmin}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("visible when IsAdmin = true", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Detect_InvertedBinding()
    {
        var xaml = MakeXaml("<Label Text=\"Loading\" IsVisible=\"{Binding IsLoaded, Converter={StaticResource InverseBoolConverter}}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("visible when IsLoaded = false", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Detect_AlwaysHidden()
    {
        var xaml = MakeXaml("<Label Text=\"Hidden\" IsVisible=\"False\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        // Always-hidden elements should still appear with condition noted
        Assert.Contains("Hidden", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Detect_NoCondition()
    {
        var xaml = MakeXaml("<Label Text=\"Always visible\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.DoesNotContain("visible when", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Detect_DataTrigger()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <Label Text=""Discount"">
        <Label.Triggers>
            <DataTrigger TargetType=""Label"" Binding=""{Binding HasDiscount}"" Value=""True"">
                <Setter Property=""IsVisible"" Value=""True"" />
            </DataTrigger>
        </Label.Triggers>
    </Label>
</ContentPage>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("visible when HasDiscount = True", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Detect_NotConverter()
    {
        var xaml = MakeXaml("<Button Text=\"Show\" IsVisible=\"{Binding IsBusy, Converter={StaticResource NotConverter}}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("visible when IsBusy = false", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Detect_NegateConverter()
    {
        var xaml = MakeXaml("<Button Text=\"Show\" IsVisible=\"{Binding IsEmpty, Converter={StaticResource NegateBoolConverter}}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("visible when IsEmpty = false", sources["TestPage_UiIndex.g.cs"]);
    }

    private static string MakeXaml(string content) => $@"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    {content}
</ContentPage>";
}
