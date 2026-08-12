using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "Metadata")]
public class AgentMetadataTests : IntegrationTestBase
{
    public AgentMetadataTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [Fact]
    public async Task Status_ReturnsValidAgentInfo()
    {
        var status = await Client.GetStatusAsync();

        Assert.NotNull(status);
        Assert.True(status!.Running);
        Assert.NotNull(status.Agent);
        Assert.NotNull(status.Agent!.Version);
    }

    [Fact]
    public async Task Status_ReportsTheFrameworkUnderTest()
    {
        // This is what tells a client whether it is driving a MAUI app or a plain .NET one, and
        // therefore which capabilities it can expect to be answered.
        var status = await Client.GetStatusAsync();

        Assert.NotNull(status?.Agent);
        Assert.Equal(TestFramework.Name, status!.Agent!.FrameworkId);

        var expectedUi = TestFramework.IsNative
            ? App.Platform switch
            {
                "android" => "android-views",
                "ios" or "maccatalyst" => "uikit",
                "macos" => "appkit",
                var other => throw new InvalidOperationException($"No native UI framework for '{other}'."),
            }
            : "maui-controls";

        Assert.Equal(expectedUi, status.Agent.UiFramework);
    }

    [Fact]
    public async Task Status_ContainsPlatformInfo()
    {
        var status = await Client.GetStatusAsync();

        Assert.NotNull(status);
        Assert.NotNull(status!.Platform);

        var expected = Platform switch
        {
            "maccatalyst" => "catalyst",
            "ios" => "ios",
            "android" => "android",
            "windows" => "win",
            "gtk" => "linux",
            _ => Platform
        };

        Assert.Contains(expected, status.Platform!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Status_ContainsDeviceInfo()
    {
        var status = await Client.GetStatusAsync();

        Assert.NotNull(status);
        Assert.NotNull(status!.Device);
        Assert.NotNull(status.Device!.Platform);
        Assert.NotNull(status.Device.Idiom);
    }

    [Fact]
    public async Task Status_ContainsAppInfo()
    {
        var status = await Client.GetStatusAsync();

        Assert.NotNull(status);
        Assert.NotNull(status!.App);
        Assert.NotNull(status.App!.Name);
        Assert.NotNull(status.App.PackageId);
    }

    [Fact]
    public async Task Status_WithWindow_AcceptsParameter()
    {
        var status = await Client.GetStatusAsync(window: 0);

        Assert.NotNull(status);
        Assert.True(status!.Running);
    }

    [Fact]
    public async Task Capabilities_ReturnsKnownFeatures()
    {
        var json = await Client.GetCapabilitiesAsync();

        var text = json.ToString();
        Assert.Contains("ui", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("webview", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JobsCapability_MatchesStatus()
    {
        var status = await Client.GetStatusAsync();
        var capabilities = await Client.GetCapabilitiesAsync();

        Assert.NotNull(status);
        Assert.NotNull(status!.Capabilities);
        Assert.Equal(
            status.Capabilities!.Jobs,
            capabilities.GetProperty("capabilities").GetProperty("device.jobs").GetProperty("supported").GetBoolean());
    }
}
