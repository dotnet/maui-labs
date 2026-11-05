using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.DevFlow.Tests;

public class ShellSearchHandlerCoordinatorTests
{
    [Theory]
    [InlineData(SearchBoxVisibility.Hidden, false, false)]
    [InlineData(SearchBoxVisibility.Collapsible, true, true)]
    [InlineData(SearchBoxVisibility.Expanded, true, false)]
    public void VisibilityMode_MapsToNativePresentation(
        SearchBoxVisibility visibility,
        bool isVisible,
        bool isCollapsible)
    {
        var searchHandler = new SearchHandler
        {
            SearchBoxVisibility = visibility
        };

        Assert.Equal(isVisible, ShellSearchHandlerCoordinator.IsVisible(searchHandler));
        Assert.Equal(isCollapsible, ShellSearchHandlerCoordinator.IsCollapsible(searchHandler));
    }

    [Fact]
    public void ConfirmQuery_UpdatesQueryAndExecutesCommand()
    {
        var executed = false;
        var searchHandler = new SearchHandler
        {
            Command = new Command(() => executed = true)
        };

        ShellSearchHandlerCoordinator.ConfirmQuery(searchHandler, "maui");

        Assert.Equal("maui", searchHandler.Query);
        Assert.True(executed);
    }
}
