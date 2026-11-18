using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

/// <summary>
/// Guards how <see cref="WebFixture"/> finds the dev server, addresses the page and builds the
/// sample. Pure string handling, so these need no browser.
/// </summary>
public class WebFixtureTests
{
    [Theory]
    [InlineData("App url: http://127.0.0.1:55822/", "http://127.0.0.1:55822/")]
    [InlineData("App url: http://127.0.0.1:55822/?arg=--urls&arg=x", "http://127.0.0.1:55822/")]
    [InlineData("App url: https://127.0.0.1:55823/", null)]
    [InlineData("Debug at url: http://127.0.0.1:55822/_framework/debug", null)]
    public void ParseAppUrl_ReadsTheHttpAddress(string line, string? expected)
        => Assert.Equal(expected, WebFixture.ParseAppUrl(line));

    [Fact]
    public void CreatePageUrl_PassesTheAgentPortInTheQuery()
        => Assert.Equal("http://127.0.0.1:5000/?devflowPort=10500", WebFixture.CreatePageUrl("http://127.0.0.1:5000/", 10500));

    [Fact]
    public void CreateBuildArguments_UsesPackageBackendByDefault()
        => Assert.Equal("", WebFixture.CreateBuildArguments(backendProject: null));

    [Fact]
    public void CreateBuildArguments_BuildsLocalBackendForNet10()
    {
        var project = Path.Combine(Path.GetTempPath(), "mauiplatforms", "Platform.Maui.Web.csproj");

        var arguments = WebFixture.CreateBuildArguments(project);

        Assert.Contains($"\"-p:PlatformMauiWebProject={project}\"", arguments);
        Assert.Contains("-p:BaseTargetFramework=net10.0", arguments);
        Assert.Contains("-p:MauiVersion=10.0.120", arguments);
    }
}
