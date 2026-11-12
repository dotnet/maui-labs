using Microsoft.Maui.DevFlow.Driver;
using System.Diagnostics;
using System.Globalization;
using Xamarin.Android.Tools;

namespace Microsoft.Maui.DevFlow.Tests;

public class AndroidAppDriverAdbTests
{
    [Fact]
    public void CreateAdbProcessStartInfo_UntrustedValues_RemainSeparateArguments()
    {
        var arguments = AndroidAppDriver.BuildAdbArguments(
            "emulator-5554\" shell rm /sdcard/victim \"",
            "pull",
            "/sdcard/recording.mp4",
            "output file\" & unwanted-command.mp4");
        var processStartInfo = AndroidAppDriver.CreateAdbProcessStartInfo("adb", arguments);

        Assert.Equal(
            [
                "-s",
                "emulator-5554\" shell rm /sdcard/victim \"",
                "pull",
                "/sdcard/recording.mp4",
                "output file\" & unwanted-command.mp4",
            ],
            processStartInfo.ArgumentList);
        Assert.Empty(processStartInfo.Arguments);
    }

    [Fact]
    public async Task PressKeyAsync_ShellMetacharacters_ThrowsBeforeLaunchingAdb()
    {
        using var driver = new AndroidAppDriver();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => driver.PressKeyAsync("HOME; rm /sdcard/victim"));

        Assert.Equal("key", exception.ParamName);
    }

    [Theory]
    [InlineData(-1, 0, "x")]
    [InlineData(0, -1, "y")]
    public async Task TapCoordinateAsync_NegativeCoordinates_ThrowsBeforeDeviceDiscovery(
        int x, int y, string parameter)
    {
        using var driver = new AndroidAppDriver(_ =>
            throw new InvalidOperationException("Device discovery must not run for invalid coordinates."));

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => driver.TapCoordinateAsync(x, y));

        Assert.Equal(parameter, exception.ParamName);
    }

    [Fact]
    public void BuildCoordinateTapArguments_UsesInvariantSeparateArguments()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal(
                ["shell", "input", "tap", "1234", "5678"],
                AndroidAppDriver.BuildCoordinateTapArguments(1234, 5678));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void BuildCoordinateTapArguments_AllowsScreenOrigin()
    {
        Assert.Equal(
            ["shell", "input", "tap", "0", "0"],
            AndroidAppDriver.BuildCoordinateTapArguments(0, 0));
    }

    [Fact]
    public async Task TapCoordinateAsync_UnsafeSerial_ThrowsBeforeLaunchingAdb()
    {
        using var driver = new AndroidAppDriver { Serial = "emulator-5554; unwanted" };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => driver.TapCoordinateAsync(10, 20));

        Assert.Equal("Serial", exception.ParamName);
    }

    [Fact]
    public async Task TapCoordinateAsync_MultipleDevices_RequiresSelection()
    {
        var runner = new RecordingAdbRunner(
        [
            new AdbDeviceInfo { Serial = "emulator-5554", Status = AdbDeviceStatus.Online },
            new AdbDeviceInfo { Serial = "emulator-5556", Status = AdbDeviceStatus.Online },
        ]);
        using var driver = new TestAndroidAppDriver(_ => runner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => driver.TapCoordinateAsync(10, 20));

        Assert.Contains("More than one Android device", exception.Message);
    }

    [Fact]
    public async Task SetupPlatformAsync_UnsafeSerial_ThrowsBeforeLaunchingAdb()
    {
        var adbRunner = new RecordingAdbRunner();
        using var driver = new TestAndroidAppDriver(_ => adbRunner)
        {
            Serial = "emulator-5554\" shell rm /sdcard/victim \"",
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => driver.SetupAsync(19223));

        Assert.Equal("Serial", exception.ParamName);
        Assert.Empty(adbRunner.ReversePortCalls);
    }

    [Fact]
    public async Task SetupPlatformAsync_WithSerial_UsesAdbRunnerReversePort()
    {
        var adbRunner = new RecordingAdbRunner();
        using var driver = new TestAndroidAppDriver(_ => adbRunner)
        {
            Serial = "emulator-5554",
        };

        await driver.SetupAsync(19223);

        var call = Assert.Single(adbRunner.ReversePortCalls);
        Assert.Equal("emulator-5554", call.Serial);
        Assert.Equal(new AdbPortSpec(AdbProtocol.Tcp, 19223), call.Remote);
        Assert.Equal(new AdbPortSpec(AdbProtocol.Tcp, 19223), call.Local);
    }

    [Fact]
    public async Task SetupPlatformAsync_WithoutSerial_UsesOnlyConnectedDevice()
    {
        var adbRunner = new RecordingAdbRunner(
        [
            new AdbDeviceInfo
            {
                Serial = "emulator-5554",
                Status = AdbDeviceStatus.Online,
            },
        ]);
        using var driver = new TestAndroidAppDriver(_ => adbRunner);

        await driver.SetupAsync(19223);

        Assert.Equal("emulator-5554", Assert.Single(adbRunner.ReversePortCalls).Serial);
    }

    [Fact]
    public async Task SetupPlatformAsync_WithAndroidSerial_UsesEnvironmentSelection()
    {
        var adbRunner = new RecordingAdbRunner();
        using var driver = new TestAndroidAppDriver(_ => adbRunner, () => "emulator-5556");

        await driver.SetupAsync(19223);

        Assert.Equal("emulator-5556", Assert.Single(adbRunner.ReversePortCalls).Serial);
    }

    [Fact]
    public async Task SetupPlatformAsync_UnsafeAndroidSerial_ReportsEnvironmentVariable()
    {
        var adbRunner = new RecordingAdbRunner();
        using var driver = new TestAndroidAppDriver(
            _ => adbRunner,
            () => "emulator-5554\" shell rm /sdcard/victim \"");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => driver.SetupAsync(19223));

        Assert.Equal("ANDROID_SERIAL", exception.ParamName);
        Assert.Empty(adbRunner.ReversePortCalls);
    }

    [Fact]
    public async Task EnsureRecordingProcessStartedAsync_ImmediateNonzeroExit_ReportsFailure()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AndroidAppDriver.EnsureRecordingProcessStartedAsync(
                Task.FromResult((1, string.Empty, "device offline")),
                "adb screenrecord"));

        Assert.Contains("code 1", exception.Message);
        Assert.Contains("device offline", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task RunAdbWithOutputAsync_LargeOutput_CapturesBothStreamsAndChecksExitCode(int exitCode)
    {
        using var driver = new AndroidAppDriver
        {
            AdbPath = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
        };
        string[] arguments = OperatingSystem.IsWindows()
            ? ["/d", "/c", $"(for /l %i in (1,1,8192) do @echo KO: output) & (for /l %i in (1,1,8192) do @echo error) 1>&2 & exit /b {exitCode}"]
            : ["-c", $"i=0; while [ \"$i\" -lt 8192 ]; do printf 'KO: output\\n'; printf 'error\\n' >&2; i=$((i + 1)); done; exit {exitCode}"];

        var command = driver.RunAdbWithOutputAsync(arguments);
        if (exitCode == 0)
        {
            var output = await command.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(string.Concat(Enumerable.Repeat($"KO: output{Environment.NewLine}", 8192)), output);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => command.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.EndsWith(string.Concat(Enumerable.Repeat($"error{Environment.NewLine}", 8192)), exception.Message);
        }
    }

    [Fact]
    public async Task CaptureAsync_PreCancelledToken_DoesNotStartProcess()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        int? pid = null;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HostProcess.CaptureAsync(
                new ProcessStartInfo("must-not-start"),
                cancel.Token,
                killEntireProcessTree: false,
                onStarted: value => pid = value));

        Assert.Null(pid);
    }

    [Fact]
    public async Task EnsureRecordingProcessStartedAsync_RunningProcess_RemainsAliveUntilCancelled()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        int? pid = null;
        var startInfo = AndroidAppDriver.CreateAdbProcessStartInfo(
            OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            OperatingSystem.IsWindows()
                ? ["/d", "/c", "for /l %i in (1,1,1000000000) do @rem"]
                : ["-c", "exec sleep 30"]);
        var command = HostProcess.CaptureAsync(
            startInfo,
            cancel.Token,
            killEntireProcessTree: false,
            onStarted: value => pid = value);
        Assert.NotNull(pid);
        using var process = Process.GetProcessById(pid.Value);
        try
        {
            await AndroidAppDriver.EnsureRecordingProcessStartedAsync(command, "adb screenrecord");

            Assert.False(process.HasExited);
            Assert.False(command.IsCompleted);
        }
        finally
        {
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command);
        }

        Assert.True(process.HasExited);
    }

    private sealed class TestAndroidAppDriver(
        Func<string, AdbRunner> createAdbRunner,
        Func<string?>? getDefaultSerial = null)
        : AndroidAppDriver(createAdbRunner, getDefaultSerial)
    {
        public Task SetupAsync(int port) => SetupPlatformAsync("localhost", port);
    }

    private sealed class RecordingAdbRunner(IReadOnlyList<AdbDeviceInfo>? devices = null)
        : AdbRunner("adb")
    {
        public List<(string Serial, AdbPortSpec Remote, AdbPortSpec Local)> ReversePortCalls { get; } = [];

        public override Task<IReadOnlyList<AdbDeviceInfo>> ListDevicesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(devices ?? (IReadOnlyList<AdbDeviceInfo>)[]);

        public override Task ReversePortAsync(
            string serial,
            AdbPortSpec remote,
            AdbPortSpec local,
            CancellationToken cancellationToken = default)
        {
            ReversePortCalls.Add((serial, remote, local));
            return Task.CompletedTask;
        }
    }
}
