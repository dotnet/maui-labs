namespace Microsoft.Maui.AI.Indexer.Tests;

/// <summary>
/// Tests for emoji handling, unicode content, and additional edge cases
/// to ensure exhaustive exact-match coverage.
/// </summary>
public class EmojiAndUnicodeTests
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

    // ──────────────────────────────────────────────
    // Emoji in text content
    // ──────────────────────────────────────────────

    [Fact]
    public void Emoji_InLabelText()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Label Text=\"🌿 Welcome\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"🌿 Welcome\"", md);
    }

    [Fact]
    public void Emoji_InButtonText()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Button Text=\"🛒 Cart\" Command=\"{Binding GoCart}\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Button: \"🛒 Cart\" → GoCart", md);
    }

    [Fact]
    public void Emoji_DecorativeSkipped()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <Label Text="🌿" SemanticProperties.Description="" />
                <Label Text="After emoji" />
                """)));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"After emoji\"", md);
    }

    [Fact]
    public void Emoji_WithSemanticOverride()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """<Label Text="🌿" SemanticProperties.Description="Sage logo" />""")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"Sage logo\"", md);
    }

    [Fact]
    public void Emoji_InPlaceholder()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Entry Placeholder=\"🔍 Search products\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Entry: placeholder \"🔍 Search products\"", md);
    }

    [Fact]
    public void Emoji_InHeading()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """<Label Text="📦 Orders" SemanticProperties.HeadingLevel="Level1" />""")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Heading (level 1): \"📦 Orders\"", md);
    }

    [Fact]
    public void Emoji_InHint()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """<Button Text="Buy" SemanticProperties.Hint="🛒 Adds item to your cart" />""")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Button: \"Buy\" [hint: 🛒 Adds item to your cart]", md);
    }

    [Fact]
    public void Unicode_CJK_Characters()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Label Text=\"こんにちは世界\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"こんにちは世界\"", md);
    }

    [Fact]
    public void Unicode_RTL_Arabic()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Label Text=\"مرحبا\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"مرحبا\"", md);
    }

    [Fact]
    public void Emoji_MultipleOnOnePage()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <Label Text="🌿" SemanticProperties.Description="" />
                <Label Text="🌻 Garden Shop" SemanticProperties.HeadingLevel="Level1" />
                <Button Text="🛒" SemanticProperties.Description="Cart" SemanticProperties.Hint="Opens cart" />
                <Label Text="🌹 Roses" />
                """)));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Heading (level 1): "🌻 Garden Shop"
            - Button: "Cart" [hint: Opens cart]
            - Label: "🌹 Roses"
            """,
            md);
    }

    // ──────────────────────────────────────────────
    // Special characters in strings
    // ──────────────────────────────────────────────

    [Fact]
    public void SpecialChars_Quotes()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Label Text=\"Say &quot;hello&quot;\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"Say \"hello\"\"", md);
    }

    [Fact]
    public void SpecialChars_Ampersand()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Label Text=\"Tom &amp; Jerry\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"Tom & Jerry\"", md);
    }

    [Fact]
    public void SpecialChars_LessThan()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Label Text=\"Price &lt; $10\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"Price < $10\"", md);
    }

    // ──────────────────────────────────────────────
    // Additional exhaustive scenarios
    // ──────────────────────────────────────────────

    [Fact]
    public void MultipleBindings_SamePage()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <Label Text="{Binding FirstName}" />
                <Label Text="{Binding LastName}" />
                <Entry Text="{Binding Email}" Placeholder="Enter email" />
                """)));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Label: "{FirstName}"
            - Label: "{LastName}"
            - Entry: "{Email}"
            """,
            md);
    }

    [Fact]
    public void Button_NoText_NoCommand()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Button />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Button:", md);
    }

    [Fact]
    public void Label_NoText()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Label />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label:", md);
    }

    [Fact]
    public void Slider_DefaultRange()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "<Slider />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Slider: 0–1", md);
    }

    [Fact]
    public void NestedLayouts_MultipleChildren()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <Grid>
                    <VerticalStackLayout>
                        <Label Text="First" />
                        <Label Text="Second" />
                    </VerticalStackLayout>
                    <HorizontalStackLayout>
                        <Button Text="A" />
                        <Button Text="B" />
                    </HorizontalStackLayout>
                </Grid>
                """)));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Label: "First"
            - Label: "Second"
            - Button: "A"
            - Button: "B"
            """,
            md);
    }

    [Fact]
    public void Emoji_InCollectionViewTemplate()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <CollectionView ItemsSource="{Binding Products}">
                    <CollectionView.ItemTemplate>
                        <DataTemplate>
                            <VerticalStackLayout>
                                <Label Text="{Binding Emoji}" />
                                <Label Text="{Binding Name}" />
                            </VerticalStackLayout>
                        </DataTemplate>
                    </CollectionView.ItemTemplate>
                </CollectionView>
                """)));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - CollectionView: "{Products}"
              - Each item:
                - Label: "{Emoji}"
                - Label: "{Name}"
            """,
            md);
    }

    [Fact]
    public void CrossFile_EmojiInUserControl()
    {
        var page = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         xmlns:v="clr-namespace:X.V"
                         x:Class="X.P.Main">
                <v:Badge />
            </ContentPage>
            """;
        var badge = """
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         x:Class="X.V.Badge">
                <Label Text="🌟 Featured" />
            </ContentView>
            """;
        var md = GeneratorTestHarness.GetMarkdown("Main",
            ("P/Main.xaml", page), ("V/Badge.xaml", badge));

        Assert.Equal(
            """
            # Main

            File: Main.xaml

            - [Badge]:
              - Label: "🌟 Featured"
            """,
            md);
    }

    [Fact]
    public void Condition_AlwaysHidden()
    {
        // IsVisible="False" elements are now skipped entirely (not accessible)
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <Label Text="Hidden" IsVisible="False" />
                <Label Text="Visible" />
                """)));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"Visible\"", md);
    }

    [Fact]
    public void HeadingLevel_Level9()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                "<Label Text=\"Deep\" SemanticProperties.HeadingLevel=\"Level9\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Heading (level 9): \"Deep\"", md);
    }

    [Fact]
    public void HeadingLevel_None_RendersAsLabel()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                "<Label Text=\"Normal\" SemanticProperties.HeadingLevel=\"None\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"Normal\"", md);
    }

    [Fact]
    public void CollectionView_NoItemsSource()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <CollectionView>
                    <CollectionView.ItemTemplate>
                        <DataTemplate>
                            <Label Text="{Binding Name}" />
                        </DataTemplate>
                    </CollectionView.ItemTemplate>
                </CollectionView>
                """)));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - CollectionView
              - Each item:
                - Label: "{Name}"
            """,
            md);
    }

    [Fact]
    public void BindingPath_Dotted()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                "<Label Text=\"{Binding Path=User.Profile.Name}\" />")));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"{User.Profile.Name}\"", md);
    }

    [Fact]
    public void EmptyPage_NoContent()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T", "")));
        Assert.Equal("# T\n\nFile: T.xaml", md);
    }

    [Fact]
    public void ResourceDictionary_Skipped()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <ContentPage.Resources>
                    <ResourceDictionary>
                        <Color x:Key="Primary">#FF0000</Color>
                    </ResourceDictionary>
                </ContentPage.Resources>
                <Label Text="After resources" />
                """)));
        Assert.Equal("# T\n\nFile: T.xaml\n\n- Label: \"After resources\"", md);
    }

    [Fact]
    public void TwoConditions_SamePage()
    {
        var md = GeneratorTestHarness.GetMarkdown("T",
            ("T.xaml", Page("X.T",
                """
                <Label Text="Normal mode" IsVisible="{Binding IsNormal}" />
                <Label Text="Compact mode" IsVisible="{Binding IsCompact}" />
                """)));
        Assert.Equal(
            """
            # T

            File: T.xaml

            - Label: "Normal mode" [visible when IsNormal = true]
            - Label: "Compact mode" [visible when IsCompact = true]
            """,
            md);
    }
}
