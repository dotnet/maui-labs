using Microsoft.Maui.DevFlow.Blazor;

namespace Microsoft.Maui.DevFlow.Tests;

public class WebViewNavigationTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/counter")]
    [InlineData("/counter?value=1#details")]
    [InlineData("counter")]
    [InlineData("#details")]
    public void IsAbsoluteUrl_BlazorRoute_ReturnsFalse(string url)
    {
        Assert.False(WebViewNavigation.IsAbsoluteUrl(url));
    }

    [Theory]
    [InlineData("https://example.test/counter")]
    [InlineData("http://example.test/")]
    [InlineData("file:///tmp/index.html")]
    [InlineData("data:text/html,test")]
    public void IsAbsoluteUrl_ExplicitAbsoluteUrl_ReturnsTrue(string url)
    {
        Assert.True(WebViewNavigation.IsAbsoluteUrl(url));
    }

    [Fact]
    public void CreateRelativeNavigationScript_EscapesTheRoute()
    {
        var script = WebViewNavigation.CreateRelativeNavigationScript("/counter?value='\\\r\n");

        Assert.Contains(@"const target = '/counter?value=\'\\\r\n';", script);
        Assert.Contains("window.Blazor.navigateTo(target)", script);
        Assert.Contains("history.pushState({}, '', target)", script);
    }
}
