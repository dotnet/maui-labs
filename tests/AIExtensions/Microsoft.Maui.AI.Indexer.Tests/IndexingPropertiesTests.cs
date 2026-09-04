namespace Microsoft.Maui.AI.Indexer.Tests;

public sealed class IndexingPropertiesTests
{
    [Fact]
    public void Generator_ExcludedChildSubtree_OmitsGroupAndDescendants()
    {
        var markdown = GeneratorTestHarness.GetMarkdown(
            "MainPage",
            ("MainPage.xaml",
                """
                <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                             xmlns:indexer="clr-namespace:Microsoft.Maui.AI.Indexer;assembly=Microsoft.Maui.AI.Indexer"
                             xmlns:views="clr-namespace:TestApp.Views"
                             x:Class="TestApp.MainPage">
                  <VerticalStackLayout>
                    <Label Text="Before" />
                    <views:AssistantSidebar
                        indexer:IndexingProperties.ExcludeWithChildren="True">
                      <Label Text="Assistant-only text" />
                    </views:AssistantSidebar>
                    <Label Text="After" />
                  </VerticalStackLayout>
                </ContentPage>
                """));

        Assert.Equal(
            """
            # MainPage

            File: MainPage.xaml

            - Label: "Before"
            - Label: "After"
            """,
            markdown);
    }

    [Fact]
    public void Generator_ExcludedRoot_DoesNotIndexDocument()
    {
        var markdown = GeneratorTestHarness.GetMarkdown(
            "AssistantSidebar",
            ("AssistantSidebar.xaml",
                """
                <ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                             xmlns:indexer="clr-namespace:Microsoft.Maui.AI.Indexer;assembly=Microsoft.Maui.AI.Indexer"
                             x:Class="TestApp.AssistantSidebar"
                             indexer:IndexingProperties.ExcludeWithChildren="true">
                  <Label Text="Assistant-only text" />
                </ContentView>
                """));

        Assert.Null(markdown);
    }

    [Fact]
    public void Generator_ExcludedShellItems_OmitsTitlesAndHostedPages()
    {
        var markdown = GeneratorTestHarness.GetMarkdown(
            "AppShell",
            ("AppShell.xaml",
                """
                <Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                       xmlns:indexer="clr-namespace:Microsoft.Maui.AI.Indexer;assembly=Microsoft.Maui.AI.Indexer"
                       xmlns:pages="clr-namespace:TestApp.Pages"
                       x:Class="TestApp.AppShell">
                  <FlyoutItem Title="Private"
                              indexer:IndexingProperties.ExcludeWithChildren="True">
                    <ShellContent Title="Private page"
                                  ContentTemplate="{DataTemplate pages:PrivatePage}" />
                  </FlyoutItem>
                  <Tab Title="Public">
                    <ShellContent Title="Hidden child"
                                  ContentTemplate="{DataTemplate pages:HiddenPage}"
                                  indexer:IndexingProperties.ExcludeWithChildren="True" />
                    <ShellContent Title="Visible child"
                                  ContentTemplate="{DataTemplate pages:VisiblePage}" />
                  </Tab>
                </Shell>
                """));

        Assert.Equal(
            """
            # AppShell

            File: AppShell.xaml

            - Tab: "Public"
              - ShellContent: "Visible child" → VisiblePage  (HOME — the screen the app opens to; users start here)
            """,
            markdown);
    }
}
