using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.DevFlow.Driver;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class DevFlowRecordingDriverTests
{
    [Theory]
    [InlineData("ios")]
    [InlineData("iOS")]
    [InlineData("iossimulator")]
    public void CreateRecordingDriver_IosTarget_PreservesExplicitUdid(string platform)
    {
        const string udid = "11111111-2222-3333-4444-555555555555";
        using var driver = DevFlowCommands.CreateRecordingDriver(platform, udid, starting: true);

        Assert.Equal(udid, Assert.IsAssignableFrom<iOSSimulatorAppDriver>(driver).DeviceUdid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void CreateRecordingDriver_IosStartWithoutTarget_RejectsAmbiguousRecording(string? device)
    {
        var error = Assert.Throws<ArgumentException>(
            () => DevFlowCommands.CreateRecordingDriver("ios", device, starting: true));

        Assert.Contains("--device", error.Message);
    }

    [Fact]
    public void CreateRecordingDriver_IosStopWithoutTarget_UsesExistingRecording()
    {
        using var driver = DevFlowCommands.CreateRecordingDriver("ios", null, starting: false);

        Assert.Null(Assert.IsAssignableFrom<iOSSimulatorAppDriver>(driver).DeviceUdid);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateRecordingDriver_AndroidStartOrStop_PreservesRecordingSerial(bool starting)
    {
        using var driver = DevFlowCommands.CreateRecordingDriver("android", "emulator-5556", starting);

        Assert.Equal("emulator-5556", Assert.IsType<AndroidAppDriver>(driver).Serial);
    }

    [Fact]
    public void CreateRecordingDriver_DesktopWithoutDevice_PreservesDefaultDriver()
    {
        using var driver = DevFlowCommands.CreateRecordingDriver("maccatalyst", null, starting: true);

        Assert.IsType<MacCatalystAppDriver>(driver);
    }

    [Fact]
    public void EnsureRecordingProcessStarted_ExitedProcess_KillsWatchdogDeletesStateAndThrows()
    {
        var state = new RecordingState
        {
            RecordingPid = 1234,
            WatchdogPid = 5678,
            OutputFile = "recording.mp4",
            Platform = "ios",
        };
        int? killedWatchdogPid = null;
        var stateDeleted = false;

        var error = Assert.Throws<InvalidOperationException>(() =>
            DevFlowCommands.EnsureRecordingProcessStarted(
                state,
                _ => false,
                watchdogPid => killedWatchdogPid = watchdogPid,
                () => stateDeleted = true));

        Assert.Equal(5678, killedWatchdogPid);
        Assert.True(stateDeleted);
        Assert.Contains("ios recording process exited during startup", error.Message);
    }

    [Fact]
    public void RecordingIOSSimulatorAppDriver_ImmediateNonzeroExit_ReportsFailure()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            RecordingIOSSimulatorAppDriver.ThrowIfProcessExited(
                hasExited: true,
                exitCode: 149,
                standardError: "Invalid device"));
        Assert.Contains("code 149", error.Message);
        Assert.Contains("Invalid device", error.Message);
    }
}
