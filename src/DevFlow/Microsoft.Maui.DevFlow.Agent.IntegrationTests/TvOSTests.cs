using System.Buffers.Binary;
using System.Text;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
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
    public async Task Screenshot_Fullscreen_CapturesDisplay()
    {
        var png = await Client.FullscreenScreenshotAsync(scale: "native");

        var (width, height) = ReadPngSize(png);
        Assert.True(width >= 1280 && height >= 720, $"Expected a TV-sized capture, got {width}x{height}.");
    }

    [TvOSFact]
    public async Task CliScreenshot_CapturesSelectedSimulatorWithRequestedSize()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"tvos-{Guid.NewGuid():N}.png");
        try
        {
            var result = await InvokeCliCommandAsync("ui", "screenshot", "--output", path, "--max-width", "320");

            Assert.True(result.ExitCode == 0, result.Stderr);
            var (width, height) = ReadPngSize(await File.ReadAllBytesAsync(path));
            Assert.Equal(320, width);
            Assert.Equal(180, height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TvOSFact]
    public async Task CliRecording_StartAndStop_PersistsTvOSTargetAndProducesVideo()
    {
        await ReturnToMainPageAsync();
        Assert.False(RecordingStateManager.IsRecording(), "Another recording is active; do not interrupt it.");
        var path = Path.Combine(AppContext.BaseDirectory, $"tvos-{Guid.NewGuid():N}.mp4");
        try
        {
            var start = await InvokeCliCommandAsync(
                "recording", "start", "--platform", "tvos", "--device", App.DeviceId!,
                "--output", path, "--timeout", "30");
            Assert.True(start.ExitCode == 0, start.Stderr);
            var state = RecordingStateManager.Load();
            Assert.Equal("tvos", state?.Platform);
            Assert.Equal(App.DeviceId, state?.Serial);

            Assert.True(await Client.TapAsync((await FindElementAsync("CounterButton")).Id));
            Assert.NotNull(await Client.ScreenshotAsync());
            var stop = await InvokeCliCommandAsync("recording", "stop");

            Assert.True(stop.ExitCode == 0, stop.Stderr);
            var video = await File.ReadAllBytesAsync(path);
            Assert.True(video.Length > 32, "Recording should contain a video, not an empty file.");
            Assert.Equal("ftyp", Encoding.ASCII.GetString(video, 4, 4));
            Assert.Null(RecordingStateManager.Load());
        }
        finally
        {
            if (RecordingStateManager.Load()?.OutputFile == path)
                await InvokeCliCommandAsync("recording", "stop");
            File.Delete(path);
        }
    }

    [TvOSFact]
    public async Task CliTheme_Auto_ChangesAppTheme()
    {
        var original = await Client.GetThemeAsync();
        Assert.NotNull(original);
        try
        {
            var result = await InvokeCliCommandWithLeaseAsync("theme", "set", "dark");

            Assert.True(result.ExitCode == 0, result.Stderr);
            Assert.Equal(DevFlowTheme.Dark, (await Client.GetThemeAsync())?.Theme);
        }
        finally
        {
            var restored = await Client.SetThemeAsync(original.RequestedTheme ?? original.Theme);
            Assert.True(restored.Success, restored.Message);
        }
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
