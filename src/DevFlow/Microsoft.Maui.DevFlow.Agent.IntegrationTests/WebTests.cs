using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

/// <summary>
/// Drives samples/DevFlow.Sample.Web in a headless Chromium browser through the broker tunnel. Run
/// with <c>DEVFLOW_TEST_PLATFORM=web dotnet test --filter Category=Web</c>.
/// </summary>
[Collection("AgentIntegration")]
[Trait("Category", "Web")]
public class WebTests : IntegrationTestBase
{
    public WebTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [WebFact]
    public async Task Status_ReportsBrowserPlatform()
    {
        var status = await Client.GetStatusAsync();

        Assert.NotNull(status);
        Assert.Equal("Browser", status.Platform);
        Assert.Equal("DevFlow Web", status.AppName);
        Assert.True(status.Running);
    }

    [WebFact]
    public async Task Broker_ListsAgentAsTunneled()
    {
        var brokerPort = await BrokerClient.GetRunningBrokerPortAsync();
        Assert.NotNull(brokerPort);

        var agents = await BrokerClient.ListAgentsAsync(brokerPort.Value);

        var agent = Assert.Single(agents!, agent => agent.Port == App.AgentPort);
        Assert.Equal("tunnel", agent.Transport);
    }

    [WebFact]
    public async Task Tree_ReportsDomBoundsForSampleControls()
    {
        await ReturnToMainPageAsync();

        foreach (var automationId in new[] { "TitleLabel", "CounterButton", "NameEntry", "SampleSwitch" })
        {
            var element = await FindElementAsync(automationId);
            Assert.True(element.WindowBounds is { Width: > 0, Height: > 0 },
                $"{automationId} should report on-screen bounds.");
            Assert.NotNull(element.NativeProperties);
            Assert.True(element.NativeProperties!.ContainsKey("tagName"), $"{automationId} should describe its DOM element.");
        }
    }

    [WebFact]
    public async Task Tap_Button_RaisesClicked()
    {
        await ReturnToMainPageAsync();
        var before = (await FindElementAsync("CounterLabel")).Text;

        Assert.True(await Client.TapAsync((await FindElementAsync("CounterButton")).Id));

        await WaitForAsync(async () => (await FindElementAsync("CounterLabel")).Text != before);
        Assert.StartsWith("Pressed ", (await FindElementAsync("CounterLabel")).Text);
    }

    [WebFact]
    public async Task Fill_Entry_UpdatesText()
    {
        await ReturnToMainPageAsync();
        var entry = await FindElementAsync("NameEntry");

        Assert.True(await Client.FillAsync(entry.Id, "Browser"));

        await WaitForAsync(async () => (await FindElementAsync("GreetingLabel")).Text == "Hello, Browser!");
        Assert.Equal("Browser", (await FindElementAsync("NameEntry")).Text);
    }

    [WebFact]
    public async Task Screenshot_RendersViewport()
    {
        await ReturnToMainPageAsync();
        var status = await Client.GetStatusAsync();

        var png = await Client.ScreenshotAsync();

        var (width, height) = ReadPngSize(png);
        Assert.Equal(Math.Round(status!.Device!.WindowWidth!.Value), width);
        Assert.Equal(Math.Round(status.Device.WindowHeight!.Value), height);
    }

    [WebFact]
    public async Task Screenshot_RendersElementAtItsBounds()
    {
        await ReturnToMainPageAsync();
        var button = await FindElementAsync("CounterButton");

        var png = await Client.ScreenshotAsync(elementId: button.Id);

        var (width, height) = ReadPngSize(png);
        Assert.Equal(Math.Round(button.WindowBounds!.Width), width);
        Assert.Equal(Math.Round(button.WindowBounds.Height), height);
    }

    [WebFact]
    public async Task Tap_PushesPage_AndBackReturns()
    {
        await ReturnToMainPageAsync();

        Assert.True(await Client.TapAsync((await FindElementAsync("DetailsButton")).Id));
        await FindElementAsync("DetailsLabel");

        Assert.True(await Client.BackAsync());
        await WaitForAsync(async () => await TryFindElementAsync("DetailsLabel") is null);
        await FindElementAsync("CounterButton");
    }

    [WebFact]
    public async Task WebSocketStreams_AreRejectedByTheTunnel()
    {
        using var socket = new System.Net.WebSockets.ClientWebSocket();

        var error = await Assert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(() =>
            socket.ConnectAsync(new Uri($"ws://127.0.0.1:{App.AgentPort}/ws/logs"), CancellationToken.None));

        Output.WriteLine(error.Message);
    }

    async Task ReturnToMainPageAsync()
    {
        if (await TryFindElementAsync("DetailsLabel") is not null)
        {
            await Client.BackAsync();
            await WaitForAsync(async () => await TryFindElementAsync("DetailsLabel") is null);
        }
    }

    static (int Width, int Height) ReadPngSize(byte[]? png)
    {
        Assert.NotNull(png);
        Assert.True(png.Length > 24, "Screenshot is too small to be a PNG.");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        // IHDR is always the first chunk: width and height are big-endian at offsets 16 and 20.
        return (
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }
}
