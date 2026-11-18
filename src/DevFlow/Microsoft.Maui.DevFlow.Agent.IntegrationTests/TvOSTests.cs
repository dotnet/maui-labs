using System.Buffers.Binary;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

/// <summary>
/// Drives samples/DevFlow.Sample.TvOS on an Apple TV simulator. Run with
/// <c>DEVFLOW_TEST_PLATFORM=tvos dotnet test --filter Category=TvOS</c>.
/// </summary>
[Collection("AgentIntegration")]
[Trait("Category", "TvOS")]
public class TvOSTests : IntegrationTestBase
{
    public TvOSTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [TvOSFact]
    public async Task Status_ReportsTvOSDeviceAndBundleIdentity()
    {
        var status = await Client.GetStatusAsync();

        Assert.NotNull(status);
        Assert.Equal("tvOS", status.Platform);
        Assert.Equal("TV", status.Idiom);
        Assert.Equal("Virtual", status.DeviceType);
        Assert.Equal("DevFlow tvOS", status.AppName);
        Assert.True(status.Running);
    }

    [TvOSFact]
    public async Task Tree_ContainsSampleControlsWithWindowBounds()
    {
        await ReturnToMainPageAsync();

        foreach (var automationId in new[] { "TitleLabel", "CounterButton", "NameEntry", "SampleSwitch" })
        {
            var element = await FindElementAsync(automationId);
            Assert.True(element.WindowBounds is { Width: > 0, Height: > 0 },
                $"{automationId} should report on-screen bounds.");
        }
    }

    [TvOSFact]
    public async Task Tap_Button_RaisesClicked()
    {
        await ReturnToMainPageAsync();
        var before = (await FindElementAsync("CounterLabel")).Text;

        Assert.True(await Client.TapAsync((await FindElementAsync("CounterButton")).Id));

        await WaitForAsync(async () => (await FindElementAsync("CounterLabel")).Text != before);
        Assert.StartsWith("Pressed ", (await FindElementAsync("CounterLabel")).Text);
    }

    [TvOSFact]
    public async Task Fill_Entry_UpdatesText()
    {
        await ReturnToMainPageAsync();
        var entry = await FindElementAsync("NameEntry");

        Assert.True(await Client.FillAsync(entry.Id, "Apple TV"));

        await WaitForAsync(async () => (await FindElementAsync("GreetingLabel")).Text == "Hello, Apple TV!");
        Assert.Equal("Apple TV", (await FindElementAsync("NameEntry")).Text);
    }

    [TvOSFact]
    public async Task Screenshot_CapturesWindow()
    {
        await ReturnToMainPageAsync();

        var png = await Client.ScreenshotAsync();

        var (width, height) = ReadPngSize(png);
        Assert.True(width >= 1280 && height >= 720, $"Expected a TV-sized capture, got {width}x{height}.");
    }

    [TvOSFact]
    public async Task Screenshot_CapturesElement()
    {
        await ReturnToMainPageAsync();
        var button = await FindElementAsync("CounterButton");

        var png = await Client.ScreenshotAsync(elementId: button.Id, scale: "native");

        var (width, height) = ReadPngSize(png);
        var bounds = button.WindowBounds!;
        // A native-scale capture is the element at 1x or more, never the whole window.
        Assert.True(width >= bounds.Width && height >= bounds.Height,
            $"Element capture {width}x{height} is smaller than its bounds {bounds.Width}x{bounds.Height}.");
        Assert.True(height < 1080 || width < 1920, "Element capture should not be the whole window.");
    }

    [TvOSFact]
    public async Task Tap_PushesPage_AndBackReturns()
    {
        await ReturnToMainPageAsync();

        Assert.True(await Client.TapAsync((await FindElementAsync("DetailsButton")).Id));
        await FindElementAsync("DetailsLabel");

        Assert.True(await Client.BackAsync());
        await WaitForAsync(async () => await TryFindElementAsync("DetailsLabel") is null);
        await FindElementAsync("CounterButton");
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
