namespace Microsoft.Maui.AI.Indexer.Tests;

/// <summary>
/// Exact-match tests for the XAML UI Indexer. Every test compares the full
/// generated markdown string against an expected value — no Contains, no partial
/// matching. This catches whitespace, indentation, ordering, and formatting regressions.
/// </summary>
public class ExactOutputTests
{
    // Helper to build a simple ContentPage XAML
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
    // Basic elements
    // ──────────────────────────────────────────────

    [Fact]
    public void Label_LiteralText()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage", "<Label Text=\"Hello World\" />")));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Label: "Hello World"
            """,
            md);
    }

    [Fact]
    public void Label_BoundText()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage", "<Label Text=\"{Binding UserName}\" />")));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Label: "{UserName}"
            """,
            md);
    }

    [Fact]
    public void Label_WithHeadingLevel()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                "<Label Text=\"Welcome\" SemanticProperties.HeadingLevel=\"Level1\" />")));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Heading (level 1): "Welcome"
            """,
            md);
    }

    [Fact]
    public void Label_WithSemanticDescription_OverridesText()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                "<Label Text=\"🌿\" SemanticProperties.Description=\"App icon\" />")));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Label: "App icon"
            """,
            md);
    }

    [Fact]
    public void Label_Decorative_Skipped()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                """
                <Label Text="🌿" SemanticProperties.Description="" />
                <Label Text="Visible" />
                """)));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Label: "Visible"
            """,
            md);
    }

    // ──────────────────────────────────────────────
    // Buttons and commands
    // ──────────────────────────────────────────────

    [Fact]
    public void Button_WithCommand()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                "<Button Text=\"Save\" Command=\"{Binding SaveCommand}\" />")));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Button: "Save" → SaveCommand
            """,
            md);
    }

    [Fact]
    public void Button_WithDescriptionAndHint()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                """<Button Text="◄" SemanticProperties.Description="Back" SemanticProperties.Hint="Returns to previous page" />""")));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Button: "Back" [hint: Returns to previous page]
            """,
            md);
    }

    [Fact]
    public void Button_WithCommandAndHint()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                """<Button Text="Add to Cart" Command="{Binding AddCommand}" SemanticProperties.Hint="Adds item to cart" />""")));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Button: "Add to Cart" → AddCommand [hint: Adds item to cart]
            """,
            md);
    }

    // ──────────────────────────────────────────────
    // Input controls
    // ──────────────────────────────────────────────

    [Fact]
    public void Entry_WithPlaceholder()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                "<Entry Placeholder=\"Type here...\" />")));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Entry: placeholder "Type here..."
            """,
            md);
    }

    [Fact]
    public void Entry_WithBoundText()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                "<Entry Text=\"{Binding Email}\" Placeholder=\"Email\" />")));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Entry: "{Email}"
            """,
            md);
    }

    [Fact]
    public void Slider_WithRange()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                "<Slider Minimum=\"1\" Maximum=\"5\" Value=\"{Binding Rating}\" />")));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Slider: 1–5 → "{Rating}"
            """,
            md);
    }

    [Fact]
    public void Switch_WithBinding()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                "<Switch IsToggled=\"{Binding DarkMode}\" />")));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Switch: "{DarkMode}"
            """,
            md);
    }

    [Fact]
    public void Picker_WithTitleAndBinding()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                "<Picker Title=\"Select size\" SelectedItem=\"{Binding Size}\" />")));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Picker: "Select size" → "{Size}"
            """,
            md);
    }

    [Fact]
    public void Image_WithSource()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                "<Image Source=\"logo.png\" />")));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Image: "logo.png"
            """,
            md);
    }

    // ──────────────────────────────────────────────
    // Layout container skipping
    // ──────────────────────────────────────────────

    [Fact]
    public void Layouts_AreSkipped_ChildrenPreserved()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                """
                <Grid>
                    <VerticalStackLayout>
                        <Border>
                            <ScrollView>
                                <Label Text="Deep inside" />
                            </ScrollView>
                        </Border>
                    </VerticalStackLayout>
                </Grid>
                """)));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Label: "Deep inside"
            """,
            md);
    }

    // ──────────────────────────────────────────────
    // Conditional visibility
    // ──────────────────────────────────────────────

    [Fact]
    public void Condition_IsVisible_Binding()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                "<Label Text=\"Admin\" IsVisible=\"{Binding IsAdmin}\" />")));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Label: "Admin" [visible when IsAdmin = true]
            """,
            md);
    }

    [Fact]
    public void Condition_InverseConverter()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                """<Label Text="Loading" IsVisible="{Binding IsReady, Converter={StaticResource InverseBoolConverter}}" />""")));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Label: "Loading" [visible when IsReady = false]
            """,
            md);
    }

    // ──────────────────────────────────────────────
    // CollectionView with templates
    // ──────────────────────────────────────────────

    [Fact]
    public void CollectionView_WithItemTemplate()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                """
                <CollectionView ItemsSource="{Binding Items}">
                    <CollectionView.ItemTemplate>
                        <DataTemplate>
                            <Label Text="{Binding Name}" />
                        </DataTemplate>
                    </CollectionView.ItemTemplate>
                </CollectionView>
                """)));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - CollectionView: "{Items}"
              - Each item:
                - Label: "{Name}"
            """,
            md);
    }

    [Fact]
    public void CollectionView_Grouped_WithHeaders()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                """
                <CollectionView ItemsSource="{Binding Groups}" IsGrouped="True">
                    <CollectionView.GroupHeaderTemplate>
                        <DataTemplate>
                            <Label Text="{Binding CategoryName}" SemanticProperties.HeadingLevel="Level2" />
                        </DataTemplate>
                    </CollectionView.GroupHeaderTemplate>
                    <CollectionView.ItemTemplate>
                        <DataTemplate>
                            <Label Text="{Binding Name}" />
                        </DataTemplate>
                    </CollectionView.ItemTemplate>
                </CollectionView>
                """)));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - CollectionView: "{Groups}" [grouped]
              - Group header (each group):
                - Heading (level 2): "{CategoryName}"
              - Each item:
                - Label: "{Name}"
            """,
            md);
    }

    [Fact]
    public void CollectionView_WithEmptyView()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                """
                <CollectionView ItemsSource="{Binding Items}">
                    <CollectionView.ItemTemplate>
                        <DataTemplate>
                            <Label Text="{Binding Name}" />
                        </DataTemplate>
                    </CollectionView.ItemTemplate>
                    <CollectionView.EmptyView>
                        <Label Text="No items found" />
                    </CollectionView.EmptyView>
                </CollectionView>
                """)));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - CollectionView: "{Items}"
              - Each item:
                - Label: "{Name}"
              - Empty view:
                - Label: "No items found"
            """,
            md);
    }

    // ──────────────────────────────────────────────
    // BindableLayout
    // ──────────────────────────────────────────────

    [Fact]
    public void BindableLayout_WithItemTemplate()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                """
                <VerticalStackLayout BindableLayout.ItemsSource="{Binding Reviews}">
                    <BindableLayout.ItemTemplate>
                        <DataTemplate>
                            <Label Text="{Binding Comment}" />
                        </DataTemplate>
                    </BindableLayout.ItemTemplate>
                </VerticalStackLayout>
                """)));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - VerticalStackLayout with items from "{Reviews}":
              - Each item:
                - Label: "{Comment}"
            """,
            md);
    }

    // ──────────────────────────────────────────────
    // Shell routes
    // ──────────────────────────────────────────────

    [Fact]
    public void Shell_TabBar()
    {
        var xaml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                   xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                   x:Class="MyApp.AppShell">
                <TabBar>
                    <ShellContent Route="home" Title="Home" />
                    <ShellContent Route="settings" Title="Settings" />
                </TabBar>
            </Shell>
            """;

        var md = GeneratorTestHarness.GetMarkdown("AppShell",
            ("AppShell.xaml", xaml));

        Assert.Equal(
            """
            # AppShell

            File: AppShell.xaml

            - ShellContent: "Home" [route: home]
            - ShellContent: "Settings" [route: settings]
            """,
            md);
    }

    // ──────────────────────────────────────────────
    // Cross-file resolution
    // ──────────────────────────────────────────────

    [Fact]
    public void CrossFile_UserControl_Inlined()
    {
        var mainPage = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         xmlns:views="clr-namespace:MyApp.Views"
                         x:Class="MyApp.Pages.MainPage">
                <Label Text="Header" />
                <views:MyWidget />
            </ContentPage>
            """;

        var widget = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         x:Class="MyApp.Views.MyWidget">
                <Button Text="Click me" Command="{Binding ClickCommand}" />
            </ContentView>
            """;

        var md = GeneratorTestHarness.GetMarkdown("MainPage",
            ("Pages/MainPage.xaml", mainPage),
            ("Views/MyWidget.xaml", widget));

        Assert.Equal(
            """
            # MainPage

            File: MainPage.xaml

            - Label: "Header"
            - [MyWidget]:
              - Button: "Click me" → ClickCommand
            """,
            md);
    }

    [Fact]
    public void CrossFile_NestedUserControls()
    {
        var page = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         xmlns:v="clr-namespace:MyApp.Views"
                         x:Class="MyApp.Pages.Page1">
                <v:Outer />
            </ContentPage>
            """;

        var outer = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         xmlns:v="clr-namespace:MyApp.Views"
                         x:Class="MyApp.Views.Outer">
                <Label Text="Outer" />
                <v:Inner />
            </ContentView>
            """;

        var inner = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         x:Class="MyApp.Views.Inner">
                <Label Text="Inner" />
            </ContentView>
            """;

        var md = GeneratorTestHarness.GetMarkdown("Page1",
            ("Pages/Page1.xaml", page),
            ("Views/Outer.xaml", outer),
            ("Views/Inner.xaml", inner));

        Assert.Equal(
            """
            # Page1

            File: Page1.xaml

            - [Outer]:
              - Label: "Outer"
              - [Inner]:
                - Label: "Inner"
            """,
            md);
    }

    [Fact]
    public void CrossFile_SameControl_ReusedInTwoPages()
    {
        var page1 = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         xmlns:v="clr-namespace:MyApp.Views"
                         x:Class="MyApp.Pages.Page1">
                <Label Text="Page 1" />
                <v:SharedWidget />
            </ContentPage>
            """;

        var page2 = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         xmlns:v="clr-namespace:MyApp.Views"
                         x:Class="MyApp.Pages.Page2">
                <Label Text="Page 2" />
                <v:SharedWidget />
            </ContentPage>
            """;

        var widget = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         x:Class="MyApp.Views.SharedWidget">
                <Button Text="Shared" Command="{Binding DoIt}" />
            </ContentView>
            """;

        var sources = GeneratorTestHarness.GetGeneratedSources(
            ("Pages/Page1.xaml", page1),
            ("Pages/Page2.xaml", page2),
            ("Views/SharedWidget.xaml", widget));

        var p1Key = sources.Keys.First(k => k.Contains("Page1_UiIndex"));
        var p2Key = sources.Keys.First(k => k.Contains("Page2_UiIndex"));
        var md1 = GeneratorTestHarness.ExtractMarkdownConstant(sources[p1Key]);
        var md2 = GeneratorTestHarness.ExtractMarkdownConstant(sources[p2Key]);

        Assert.Equal(
            """
            # Page1

            File: Page1.xaml

            - Label: "Page 1"
            - [SharedWidget]:
              - Button: "Shared" → DoIt
            """,
            md1);

        Assert.Equal(
            """
            # Page2

            File: Page2.xaml

            - Label: "Page 2"
            - [SharedWidget]:
              - Button: "Shared" → DoIt
            """,
            md2);
    }

    // ──────────────────────────────────────────────
    // Multiple elements on one page
    // ──────────────────────────────────────────────

    [Fact]
    public void CompletePage_MultipleElements()
    {
        var md = GeneratorTestHarness.GetMarkdown("TestPage",
            ("TestPage.xaml", Page("MyApp.TestPage",
                """
                <Grid>
                    <Label Text="Product Details" SemanticProperties.HeadingLevel="Level1" />
                    <Label Text="{Binding Name}" />
                    <Label Text="{Binding Price}" />
                    <Button Text="Buy" Command="{Binding BuyCommand}" SemanticProperties.Hint="Purchase this item" />
                    <Label Text="Reviews" SemanticProperties.HeadingLevel="Level2" />
                    <VerticalStackLayout BindableLayout.ItemsSource="{Binding Reviews}">
                        <BindableLayout.ItemTemplate>
                            <DataTemplate>
                                <Label Text="{Binding Text}" />
                            </DataTemplate>
                        </BindableLayout.ItemTemplate>
                    </VerticalStackLayout>
                </Grid>
                """)));

        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Heading (level 1): "Product Details"
            - Label: "{Name}"
            - Label: "{Price}"
            - Button: "Buy" → BuyCommand [hint: Purchase this item]
            - Heading (level 2): "Reviews"
            - VerticalStackLayout with items from "{Reviews}":
              - Each item:
                - Label: "{Text}"
            """,
            md);
    }

    // ──────────────────────────────────────────────
    // Error resilience
    // ──────────────────────────────────────────────

    [Fact]
    public void InvalidXaml_ProducesNoOutput()
    {
        var md = GeneratorTestHarness.GetMarkdown("Bad",
            ("Bad.xaml", "this is not xml"));

        Assert.Null(md);
    }

    [Fact]
    public void EmptyXaml_ProducesNoOutput()
    {
        var md = GeneratorTestHarness.GetMarkdown("Empty",
            ("Empty.xaml", ""));

        Assert.Null(md);
    }

    [Fact]
    public void XamlWithoutXClass_ProducesNoOutput()
    {
        var md = GeneratorTestHarness.GetMarkdown("Colors",
            ("Resources/Colors.xaml",
                """
                <?xml version="1.0" encoding="utf-8" ?>
                <ResourceDictionary xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                    <Color x:Key="Primary">#512BD4</Color>
                </ResourceDictionary>
                """));

        Assert.Null(md);
    }

    // ──────────────────────────────────────────────
    // Generated C# source structure
    // ──────────────────────────────────────────────

    [Fact]
    public void GeneratedSource_HasNamespaceAndClass()
    {
        var source = GeneratorTestHarness.GetGeneratedSource("TestPage_UiIndex.g.cs",
            ("Pages/TestPage.xaml", Page("MyApp.Pages.TestPage", "<Label Text=\"Hello\" />")));

        Assert.NotNull(source);

        // Verify the generated source starts with the auto-generated header
        Assert.True(source!.StartsWith("// <auto-generated />"), "Should start with auto-generated comment");
        Assert.Contains("namespace MyApp.Pages;", source);

        // Verify it contains the class with attribute
        var classLine = "[global::Microsoft.Maui.AI.Indexer.UiPageIndex(\"TestPage\", FilePath = \"TestPage.xaml\")]";
        Assert.Equal(1, source!.Split(classLine).Length - 1); // exactly once

        // Verify the markdown content via the extracted constant
        var md = GeneratorTestHarness.ExtractMarkdownConstant(source);
        Assert.Equal(
            """
            # TestPage

            File: TestPage.xaml

            - Label: "Hello"
            """,
            md);
    }

    [Fact]
    public void GeneratedSource_NoNamespace()
    {
        var source = GeneratorTestHarness.GetGeneratedSource("RootPage_UiIndex.g.cs",
            ("RootPage.xaml", Page("RootPage", "<Label Text=\"Hi\" />")));

        Assert.NotNull(source);

        // Should NOT have a namespace line
        Assert.DoesNotContain("namespace", source!.Split("class")[0].Split("summary")[0]);

        // Verify the class declaration is present
        Assert.Contains("public static partial class RootPage_UiIndex", source);

        // Verify the markdown
        var md = GeneratorTestHarness.ExtractMarkdownConstant(source);
        Assert.Equal(
            """
            # RootPage

            File: RootPage.xaml

            - Label: "Hi"
            """,
            md);
    }
}
