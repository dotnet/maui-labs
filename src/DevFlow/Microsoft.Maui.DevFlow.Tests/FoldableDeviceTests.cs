using System.Globalization;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class FoldableDeviceTests
{
    private const string Serial = "emulator-19554";
    private const string Udid = "BEF095D0-5BDD-4CCB-B08B-C0BA630AEE72";

    [Theory]
    [InlineData("android", "physical-device")]
    [InlineData("android", "emulator-5554; echo bad")]
    [InlineData("android", "emulator-5554\n")]
    [InlineData("ios", "booted")]
    [InlineData("ios", "simulator name")]
    [InlineData("windows", "123")]
    public void Constructor_RequiresExplicitSafeVirtualTarget(string platform, string device)
        => Assert.Throws<ArgumentException>(() => new FoldableDevice(platform, device, false, null, NeverRun));

    [Theory]
    [InlineData(FoldMode.Unknown, null, 0, 0, 30)]
    [InlineData(FoldMode.Folded, 90.0, 0, 0, 30)]
    [InlineData(FoldMode.Unfolded, 90.0, 0, 0, 30)]
    [InlineData(FoldMode.PartiallyFolded, 0.0, 0, 0, 30)]
    [InlineData(FoldMode.PartiallyFolded, 180.0, 0, 0, 30)]
    [InlineData(FoldMode.PartiallyFolded, double.NaN, 0, 0, 30)]
    [InlineData(FoldMode.PartiallyFolded, double.PositiveInfinity, 0, 0, 30)]
    [InlineData(FoldMode.PartiallyFolded, 90.0, -1, 0, 30)]
    [InlineData(FoldMode.PartiallyFolded, 90.0, 0, -1, 30)]
    [InlineData(FoldMode.PartiallyFolded, 90.0, 0, double.NaN, 30)]
    [InlineData(FoldMode.PartiallyFolded, 90.0, 0, 31, 60)]
    [InlineData(FoldMode.PartiallyFolded, 90.0, 0, 0, 0)]
    [InlineData(FoldMode.PartiallyFolded, 90.0, 0, 0, 121)]
    [InlineData(FoldMode.PartiallyFolded, 90.0, 0, 3, 3)]
    public async Task Set_InvalidRequest_DoesNotRunCommands(FoldMode mode, double? angle, int index, double duration, int timeout)
    {
        var device = new FoldableDevice("android", Serial, false, null, NeverRun);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => device.SetAsync(mode, angle, index, duration, timeout));
    }

    [Theory]
    [InlineData(FoldMode.Folded, 0)]
    [InlineData(FoldMode.PartiallyFolded, 90)]
    [InlineData(FoldMode.Unfolded, 180)]
    public void ValidateRequest_PresetsHaveExplicitDegrees(FoldMode mode, double angle)
        => Assert.Equal(angle, FoldableDevice.ValidateRequest(mode, null, 0, 0, 30));

    [Fact]
    public void ParseAndroidHinges_OnlyEnabledSensorsAndActualIndices()
    {
        Assert.Equal([0, 2], FoldableDevice.ParseAndroidHinges(
            "acceleration: enabled.\r\nhinge-angle0: enabled.\r\nhinge-angle1: disabled.\r\nhinge-angle2: enabled.\r\nOK\r\n"));
    }

    [Theory]
    [InlineData("hinge-angle0 = 100.25:0:0\r\nOK\r\n", 100.25)]
    [InlineData("hinge-angle0 = 1.8e2\nOK\n", 180)]
    public void ParseAndroidAngle_InvariantAndExactSensor(string output, double expected)
        => Assert.Equal(expected, FoldableDevice.ParseAndroidAngle(output, "hinge-angle0"));

    [Theory]
    [InlineData("OK")]
    [InlineData("hinge-angle1 = 90")]
    [InlineData("hinge-angle0 = NaN")]
    [InlineData("hinge-angle0 = 90junk")]
    public void ParseAndroidAngle_MalformedResponse_IsNotAZeroAngle(string output)
        => Assert.Throws<InvalidOperationException>(() => FoldableDevice.ParseAndroidAngle(output, "hinge-angle0"));

    [Fact]
    public async Task Android_NoEnabledHinge_ReportsUnsupportedAndDoesNotMutate()
    {
        var fake = new AndroidConsole { Enabled = false };
        var device = new FoldableDevice("android", Serial, false, null, fake.Run);
        var capabilities = await device.GetCapabilitiesAsync();
        Assert.False(capabilities.CanSetMode);
        Assert.Empty(capabilities.SupportedModes);
        await Assert.ThrowsAsync<NotSupportedException>(() => device.SetAsync(FoldMode.Folded));
        Assert.DoesNotContain(fake.Calls, args => args.Contains("set"));
    }

    [Theory]
    [InlineData(FoldMode.Folded, null, 0)]
    [InlineData(FoldMode.PartiallyFolded, 112.5, 112.5)]
    [InlineData(FoldMode.Unfolded, null, 180)]
    public async Task Android_Set_ReadsBackRequestedAngle(FoldMode mode, double? angle, double expected)
    {
        var fake = new AndroidConsole();
        var result = await new FoldableDevice("android", Serial, false, "custom adb path", fake.Run).SetAsync(mode, angle);
        Assert.Equal(expected, result.RequestedAngleDegrees);
        Assert.Equal(expected, Assert.Single(result.State.Hinges).AngleDegrees);
        Assert.Equal(mode, result.State.Mode);
        Assert.All(fake.Calls.Where(args => args[0] == "-s"), args => Assert.Equal(Serial, args[1]));
    }

    [Fact]
    public async Task Android_Sweep_UsesObservedStartAndVerifiesFinalAngle()
    {
        var fake = new AndroidConsole { Angle = 120 };
        var result = await new FoldableDevice("android", Serial, false, null, fake.Run)
            .SetAsync(FoldMode.PartiallyFolded, 75.5, durationSeconds: 0.1);
        Assert.Equal(75.5, result.State.Hinges[0].AngleDegrees);
        var writes = fake.Calls.Where(args => args.Contains("set")).ToArray();
        Assert.True(writes.Length >= 2);
        Assert.InRange(double.Parse(writes[0][^1], CultureInfo.InvariantCulture), 75.5, 120);
        Assert.Equal("75.5", writes[^1][^1]);
    }

    [Fact]
    public async Task Android_IgnoredMutation_TimesOutWithObservedState()
    {
        var fake = new AndroidConsole { IgnoreWrites = true };
        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            new FoldableDevice("android", Serial, false, null, fake.Run).SetAsync(FoldMode.Folded, timeoutSeconds: 1));
        Assert.Contains("0=180", error.Message);
        Assert.Contains("partially", error.Message);
    }

    [Fact]
    public async Task Set_MissingHinge_IsRejectedBeforeMutation()
    {
        var fake = new AndroidConsole();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new FoldableDevice("android", Serial, false, null, fake.Run).SetAsync(FoldMode.Folded, hingeIndex: 1));
        Assert.DoesNotContain(fake.Calls, args => args.Contains("set"));
    }

    [Fact]
    public async Task Set_ConcurrentOwner_IsRefusedAndCancellationReleasesLock()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource();
        async Task<string> Block(string _, string[] args, CancellationToken token)
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return "";
        }
        var first = new FoldableDevice("android", Serial, false, null, Block).SetAsync(FoldMode.Folded, cancellationToken: cancel.Token);
        await entered.Task;
        var fake = new AndroidConsole();
        try
        {
            var exception = await Assert.ThrowsAsync<IOException>(() =>
                new FoldableDevice("android", Serial, false, null, fake.Run).SetAsync(FoldMode.Folded));
            Assert.Contains("Another fold operation", exception.Message);
            Assert.Empty(fake.Calls);
        }
        finally { cancel.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await new FoldableDevice("android", Serial, false, null, fake.Run).SetAsync(FoldMode.Folded);
    }

    [Fact]
    public async Task Ios_NoOptIn_DoesNotPrepareOrInvokePrivateHelper()
    {
        var fake = new IosSimulator();
        var device = new FoldableDevice("ios", Udid, false, null, fake.Run,
            _ => throw new InvalidOperationException("Must not compile private helper."));
        var capabilities = await device.GetCapabilitiesAsync();
        Assert.True(capabilities.Experimental);
        Assert.False(capabilities.CanReadAngle);
        Assert.Contains("opt-in", capabilities.Reason);
        await Assert.ThrowsAsync<NotSupportedException>(() => device.GetStateAsync());
        Assert.DoesNotContain(fake.Calls, args => args.Contains("spawn"));
    }

    [Theory]
    [InlineData(FoldMode.Folded, 0)]
    [InlineData(FoldMode.Unfolded, 180)]
    public async Task Ios_Endpoint_VerifiesAngleAndDisplay(FoldMode mode, double angle)
    {
        var fake = new IosSimulator();
        var result = await IosDevice(fake).SetAsync(mode);
        Assert.Equal(angle, result.State.Hinges[0].AngleDegrees);
        Assert.Equal(mode == FoldMode.Folded, Assert.Single(result.State.Displays, display => display.Active).Primary);
        Assert.Equal(3, result.State.Displays[0].Scale);
    }

    [Fact]
    public async Task Ios_AngleChangesButDisplayDoesNot_ReportsFailure()
    {
        var fake = new IosSimulator { SwitchDisplay = false };
        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            IosDevice(fake).SetAsync(FoldMode.Folded, timeoutSeconds: 1));
        Assert.Contains("0=0", exception.Message);
        Assert.Contains("active displays: 3", exception.Message);
    }

    [Fact]
    public async Task Ios_IntermediateAngle_DoesNotInventDisplayThreshold()
    {
        var fake = new IosSimulator { CoverActive = true, SwitchDisplay = false };
        var result = await IosDevice(fake).SetAsync(FoldMode.PartiallyFolded, 85);
        Assert.Equal(85, result.State.Hinges[0].AngleDegrees);
        Assert.True(result.State.Displays[0].Active);
    }

    [Fact]
    public async Task Ios_IgnoredAngleButExpectedScreenAlreadyActive_IsNotSuccess()
    {
        var fake = new IosSimulator { CoverActive = true, IgnoreWrites = true };
        await Assert.ThrowsAsync<TimeoutException>(() => IosDevice(fake).SetAsync(FoldMode.Folded, timeoutSeconds: 1));
    }

    [Fact]
    public void ParseIosDisplays_FiltersIntegratedWithoutRelyingOnNames()
    {
        var displays = FoldableDevice.ParseIosDisplays(IosSimulator.Displays(true));
        Assert.Equal(2, displays.Length);
        Assert.True(displays[0].Active);
        Assert.Equal("arbitrary-inner-name", displays[1].Name);
        Assert.Equal("3", displays[1].Id);
        Assert.Equal(2007, displays[1].NativeWidth);
    }

    [Fact]
    public void ParseIosAngles_UnsupportedDoesNotManufactureAnAngle()
        => Assert.Empty(FoldableDevice.ParseIosAngles("""{"supported":false}"""));

    [Fact]
    public void IosHelperSource_IsEmbeddedInShippingDriver()
    {
        using var source = typeof(FoldableDevice).Assembly.GetManifestResourceStream(
            "Microsoft.Maui.DevFlow.Driver.FoldableSimulator.m");
        Assert.NotNull(source);
        var text = new StreamReader(source).ReadToEnd();
        Assert.Contains("hinge-slider-control", text);
        Assert.DoesNotContain("devicectl device motion", text);
    }

    [Theory]
    [InlineData(FoldMode.Folded, "folded")]
    [InlineData(FoldMode.PartiallyFolded, "partially-folded")]
    [InlineData(FoldMode.Unfolded, "unfolded")]
    [InlineData(FoldMode.Unknown, "unknown")]
    public void Mode_RoundTripsWithStableProtocolSpelling(FoldMode mode, string value)
    {
        Assert.Equal($"\"{value}\"", JsonSerializer.Serialize(mode));
        Assert.Equal(mode, JsonSerializer.Deserialize<FoldMode>($"\"{value}\""));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(190)]
    [InlineData(double.NaN)]
    public void Mode_UnsupportedAnglesAreUnknown(double angle)
        => Assert.Equal(FoldMode.Unknown, FoldableDevice.GetMode([new FoldHinge { AngleDegrees = angle }]));

    [Fact]
    public async Task Process_ConsoleKoWithZeroExit_IsAnError()
    {
        var arguments = OperatingSystem.IsWindows() ? new[] { "/c", "echo KO: unsupported" }
            : ["-c", "printf 'KO: unsupported\\n'"];
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            FoldableDevice.RunProcessAsync(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                arguments, CancellationToken.None));
        Assert.Contains("KO: unsupported", exception.Message);
    }

    [Fact]
    public async Task Process_CancelledCommand_TerminatesWithoutWaitingForItsLifetime()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var arguments = OperatingSystem.IsWindows() ? new[] { "/c", "ping -n 30 127.0.0.1 > nul" }
            : ["-c", "sleep 30"];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FoldableDevice.RunProcessAsync(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                arguments, cancel.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    private static FoldableDevice IosDevice(IosSimulator fake)
        => new("ios", Udid, true, null, fake.Run, _ => Task.FromResult("/packaged helper"));

    private static Task<string> NeverRun(string _, string[] __, CancellationToken ___)
        => throw new InvalidOperationException("Invalid input must not run native commands.");

    private sealed class AndroidConsole
    {
        internal double Angle = 180;
        internal bool Enabled = true;
        internal bool IgnoreWrites;
        internal List<string[]> Calls = [];

        internal Task<string> Run(string executable, string[] args, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls.Add(args);
            if (args.SequenceEqual(["devices"]))
                return Task.FromResult($"List of devices attached\n{Serial}\tdevice\n");
            if (args.Contains("status"))
                return Task.FromResult($"hinge-angle0: {(Enabled ? "enabled" : "disabled")}.\nOK\n");
            if (args.Contains("get"))
                return Task.FromResult($"hinge-angle0 = {Angle.ToString("R", CultureInfo.InvariantCulture)}:0:0\nOK\n");
            if (args.Contains("set"))
            {
                if (!IgnoreWrites) Angle = double.Parse(args[^1], CultureInfo.InvariantCulture);
                return Task.FromResult("OK\n");
            }
            throw new InvalidOperationException($"Unexpected command: {string.Join(" ", args)}");
        }
    }

    private sealed class IosSimulator
    {
        internal double Angle = 180;
        internal bool CoverActive;
        internal bool SwitchDisplay = true;
        internal bool IgnoreWrites;
        internal List<string[]> Calls = [];

        internal Task<string> Run(string executable, string[] args, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls.Add(args);
            if (args.Contains("list"))
                return Task.FromResult($$$"""{"devices":{"runtime":[{"udid":"{{{Udid}}}","state":"Booted"}]}}""");
            if (args.Contains("displays"))
                return Task.FromResult(Displays(CoverActive));
            if (args.Contains("get"))
                return Task.FromResult($$$"""{"supported":true,"angleDegrees":{{{Angle.ToString("R", CultureInfo.InvariantCulture)}}}}""");
            if (args.Contains("set"))
            {
                if (!IgnoreWrites) Angle = double.Parse(args[^2], CultureInfo.InvariantCulture);
                if (SwitchDisplay) CoverActive = Angle == 0;
                return Task.FromResult("");
            }
            throw new InvalidOperationException($"Unexpected command: {string.Join(" ", args)}");
        }

        internal static string Displays(bool cover) => """
            {"result":{"displays":[
                {"type":{"integrated":{}},"displayId":1,"uniqueId":"cover-uuid","name":"arbitrary-cover-name",
                 "active":COVER_ACTIVE,"primary":true,"pointScale":3,"nativeSize":[1398,2034],"currentOrientation":"rot0"},
                {"type":{"integrated":{}},"displayId":3,"uniqueId":"inner-uuid","name":"arbitrary-inner-name",
                 "active":INNER_ACTIVE,"primary":false,"pointScale":3,"nativeSize":[2007,2853],"currentOrientation":"rot90"},
                {"type":{"external":{}}}
            ]}}
            """.Replace("COVER_ACTIVE", cover.ToString().ToLowerInvariant())
                .Replace("INNER_ACTIVE", (!cover).ToString().ToLowerInvariant());
    }
}
