namespace Microsoft.Maui.AI.Indexer.Tests;

/// <summary>Tests for control-specific rendering: switches, pickers, images, progress, etc.</summary>
public class ControlRenderingTests
{
    [Fact]
    public void Render_SwitchWithBinding()
    {
        var xaml = MakeXaml("<Switch IsToggled=\"{Binding DarkMode}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("Switch: \"{DarkMode}\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Render_CheckBoxWithBinding()
    {
        var xaml = MakeXaml("<CheckBox IsChecked=\"{Binding Agreed}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("CheckBox: \"{Agreed}\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Render_PickerWithTitle()
    {
        var xaml = MakeXaml("<Picker Title=\"Select size\" SelectedItem=\"{Binding Size}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        var src = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("Picker: \"Select size\"", src);
        Assert.Contains("{Size}", src);
    }

    [Fact]
    public void Render_DatePicker()
    {
        var xaml = MakeXaml("<DatePicker Date=\"{Binding DeliveryDate}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("DatePicker: \"{DeliveryDate}\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Render_TimePicker()
    {
        var xaml = MakeXaml("<TimePicker Time=\"{Binding SelectedTime}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("TimePicker: \"{SelectedTime}\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Render_ImageWithSource()
    {
        var xaml = MakeXaml("<Image Source=\"logo.png\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("Image: \"logo.png\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Render_ImageWithDescription()
    {
        var xaml = MakeXaml("<Image Source=\"hero.png\" SemanticProperties.Description=\"Product hero image\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("Image: \"Product hero image\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Render_ActivityIndicator()
    {
        var xaml = MakeXaml("<ActivityIndicator IsRunning=\"{Binding IsBusy}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("ActivityIndicator", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Render_ProgressBar()
    {
        var xaml = MakeXaml("<ProgressBar Progress=\"{Binding Download}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("ProgressBar: \"{Download}\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Render_RadioButton()
    {
        var xaml = MakeXaml("<RadioButton Content=\"Option A\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("RadioButton: \"Option A\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Render_SearchBar()
    {
        var xaml = MakeXaml("<SearchBar Placeholder=\"Search products...\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("SearchBar: placeholder \"Search products...\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Render_Stepper()
    {
        var xaml = MakeXaml("<Stepper Minimum=\"0\" Maximum=\"10\" Value=\"{Binding Quantity}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        Assert.Contains("Stepper: 0–10 → \"{Quantity}\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Render_ButtonWithCommandParameter()
    {
        var xaml = MakeXaml("<Button Text=\"Delete\" Command=\"{Binding DeleteCommand}\" CommandParameter=\"{Binding Id}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        var src = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("Button: \"Delete\" → DeleteCommand", src);
    }

    [Fact]
    public void Render_ImageButtonWithSource()
    {
        var xaml = MakeXaml("<ImageButton Source=\"heart.png\" Command=\"{Binding LikeCommand}\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        var src = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("ImageButton: \"heart.png\"", src);
        Assert.Contains("LikeCommand", src);
    }

    [Fact]
    public void Render_EditorWithPlaceholderAndBinding()
    {
        var xaml = MakeXaml("<Editor Text=\"{Binding Comment}\" Placeholder=\"Write something\" />");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        // Binding takes precedence over placeholder
        Assert.Contains("\"{Comment}\"", sources["TestPage_UiIndex.g.cs"]);
    }

    [Fact]
    public void Render_PromotedStructuralElement()
    {
        // A Border with SemanticProperties.Description should be promoted to semantic
        var xaml = MakeXaml("<Border SemanticProperties.Description=\"Product card\"><Label Text=\"Inside\" /></Border>");
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        var src = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("\"Product card\"", src);
    }

    [Fact]
    public void Render_CollectionViewEmptyView()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    <CollectionView ItemsSource=""{Binding Items}"">
        <CollectionView.ItemTemplate>
            <DataTemplate><Label Text=""{Binding Name}"" /></DataTemplate>
        </CollectionView.ItemTemplate>
        <CollectionView.EmptyView>
            <Label Text=""No items found"" />
        </CollectionView.EmptyView>
    </CollectionView>
</ContentPage>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("Test.xaml", xaml));
        var src = sources["TestPage_UiIndex.g.cs"];
        Assert.Contains("Empty view:", src);
        Assert.Contains("\"No items found\"", src);
    }

    [Fact]
    public void Parse_InvalidXaml_ReturnsNoOutput()
    {
        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("Bad.xaml", "this is not xml at all!!!"));
        Assert.DoesNotContain(sources.Keys, k => k.Contains("Bad"));
    }

    [Fact]
    public void Parse_EmptyXaml_ReturnsNoOutput()
    {
        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("Empty.xaml", ""));
        Assert.DoesNotContain(sources.Keys, k => k.Contains("Empty"));
    }

    [Fact]
    public void Parse_NullContent_ReturnsNoOutput()
    {
        // This tests the parser with whitespace-only content
        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("Whitespace.xaml", "   \n  "));
        Assert.DoesNotContain(sources.Keys, k => k.Contains("Whitespace"));
    }

    private static string MakeXaml(string content) => $@"<?xml version=""1.0"" encoding=""utf-8"" ?>
<ContentPage xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""MyApp.TestPage"">
    {content}
</ContentPage>";
}
