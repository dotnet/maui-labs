using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class DevFlowRecordingCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"recording-commands-{Guid.NewGuid():N}");
    private readonly Func<string, IAppDriver> _originalFactory = DevFlowCommands.RecordingDriverFactory;
    private readonly Func<RecordingState?> _originalReader = DevFlowCommands.ReadRecordingState;
    private readonly List<(string Platform, string? Serial)> _stops = [];
    private readonly List<string> _createdPlatforms = [];
    private string StatePath => Path.Combine(_directory, "state.json");

    public DevFlowRecordingCommandTests()
    {
        Directory.CreateDirectory(_directory);
        DevFlowCommands.ReadRecordingState = LoadState;
        DevFlowCommands.RecordingDriverFactory = platform =>
        {
            _createdPlatforms.Add(platform);
            return platform switch
            {
                "android" => new TestAndroidDriver(this),
                "ios" => new TestIosDriver(this),
                _ => throw new InvalidOperationException($"Unexpected recording driver: {platform}"),
            };
        };
    }

    [Theory]
    [InlineData("android", "emulator-5556")]
    [InlineData("ios", "11111111-2222-3333-4444-555555555555")]
    public async Task StartThenStop_WithoutPlatformOrDevice_UsesPersistedRecording(string platform, string device)
    {
        var output = Path.Combine(_directory, "capture.mp4");
        var start = await new CliTestHarness(65534).InvokeAsync(
            "devflow", "recording", "start", "--platform", platform,
            "--device", device, "--output", output);

        Assert.Equal(0, start.ExitCode);
        Assert.Equal(platform, LoadState()!.Platform);

        // Rebuild the command tree: no parsed start options survive into this invocation.
        var stop = await new CliTestHarness(65534).InvokeAsync("devflow", "recording", "stop");

        Assert.Equal(0, stop.ExitCode);
        Assert.Contains("Recording saved:", stop.StdOut);
        Assert.Equal(new[] { platform, platform }, _createdPlatforms);
        Assert.Equal((platform, platform == "android" ? device : null), Assert.Single(_stops));
        Assert.Null(LoadState());
    }

    [Fact]
    public async Task Stop_ConflictingOptions_UsesSavedPlatformAndSerial()
    {
        SaveState("android", "emulator-5556", Path.Combine(_directory, "capture.mp4"));
        var stop = await new CliTestHarness(65534).InvokeAsync(
            "devflow", "recording", "stop", "--platform", "ios", "--device", "other-device");

        Assert.Equal(0, stop.ExitCode);
        Assert.Equal("android", Assert.Single(_createdPlatforms));
        Assert.Equal(("android", "emulator-5556"), Assert.Single(_stops));
    }

    [Fact]
    public async Task Stop_NoSavedRecording_ReportsErrorWithoutCreatingDriver()
    {
        var stop = await new CliTestHarness(65534).InvokeAsync("devflow", "recording", "stop");

        Assert.Equal(1, stop.ExitCode);
        Assert.Contains("No active recording found.", stop.StdErr);
        Assert.Empty(_createdPlatforms);
    }

    [Fact]
    public async Task Stop_UnsupportedSavedPlatform_DoesNotFallBackOrDeleteState()
    {
        SaveState("unsupported", null, Path.Combine(_directory, "capture.mp4"));
        var stop = await new CliTestHarness(65534).InvokeAsync("devflow", "recording", "stop");

        Assert.Equal(1, stop.ExitCode);
        Assert.Equal("unsupported", Assert.Single(_createdPlatforms));
        Assert.NotNull(LoadState());
        Assert.Empty(_stops);
    }

    private RecordingState? LoadState() => File.Exists(StatePath)
        ? JsonSerializer.Deserialize<RecordingState>(File.ReadAllText(StatePath))
        : null;

    private void SaveState(string platform, string? serial, string output) =>
        File.WriteAllText(StatePath, JsonSerializer.Serialize(new RecordingState
        {
            Platform = platform, Serial = serial, OutputFile = output,
        }));

    private string Stop(string platform, string? serial)
    {
        var state = LoadState() ?? throw new InvalidOperationException("Missing test recording");
        Assert.Equal(state.Platform, platform);
        _stops.Add((platform, serial));
        File.WriteAllText(state.OutputFile, "test recording");
        File.Delete(StatePath);
        return state.OutputFile;
    }

    public void Dispose()
    {
        DevFlowCommands.RecordingDriverFactory = _originalFactory;
        DevFlowCommands.ReadRecordingState = _originalReader;
        Directory.Delete(_directory, recursive: true);
    }

    private sealed class TestAndroidDriver(DevFlowRecordingCommandTests test) : AndroidAppDriver
    {
        public override Task StartRecordingAsync(string outputFile, int timeoutSeconds = 30)
        {
            test.SaveState("android", Serial, outputFile);
            return Task.CompletedTask;
        }

        public override Task<string> StopRecordingAsync() => Task.FromResult(test.Stop("android", Serial));
    }

    private sealed class TestIosDriver(DevFlowRecordingCommandTests test) : iOSSimulatorAppDriver
    {
        public override Task StartRecordingAsync(string outputFile, int timeoutSeconds = 30)
        {
            Assert.False(string.IsNullOrWhiteSpace(DeviceUdid));
            // Match the real iOS driver: stop uses the saved PID, not a saved UDID.
            test.SaveState("ios", null, outputFile);
            return Task.CompletedTask;
        }

        public override Task<string> StopRecordingAsync() => Task.FromResult(test.Stop("ios", DeviceUdid));
    }
}
