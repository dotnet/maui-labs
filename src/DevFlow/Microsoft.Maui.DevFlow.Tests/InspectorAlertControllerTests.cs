using Microsoft.Maui.Cli.DevFlow.Android;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class InspectorAlertControllerTests
{
    [Fact]
    public async Task DetectAndDismiss_UseExistingPlatformDriver()
    {
        var driver = new FakeAlertDriver(new AlertInfo(
            "Permission",
            [new AlertButton("Allow", 0, 0, 10, 10), new AlertButton("Deny", 10, 0, 10, 10)]));
        string? targetedSerial = null;
        var controller = new InspectorAlertController(
            "localhost",
            9223,
            "Sample",
            "android",
            createDriver: (_, _, _, serial, adbPath) =>
            {
                targetedSerial = serial;
                Assert.Equal("C:\\android-sdk\\platform-tools\\adb.exe", adbPath);
                return driver;
            },
            resolveAndroidDevice: _ => Task.FromResult(AndroidDevFlowDeviceResolution.Resolved(
                "emulator-5554",
                "C:\\android-sdk\\platform-tools\\adb.exe")));

        var detected = await controller.DetectAsync();
        var dismissed = await controller.DismissAsync("Allow");

        Assert.True(detected.Ok);
        Assert.True(detected.Supported);
        Assert.Equal("Permission", detected.Alert?.Title);
        Assert.True(dismissed.Ok);
        Assert.True(dismissed.Dismissed);
        Assert.Equal("Allow", driver.DismissedLabel);
        Assert.Equal("emulator-5554", targetedSerial);
    }

    [Fact]
    public async Task AndroidWithoutExactForwardOwnership_RefusesToCreateDriver()
    {
        var driverCreated = false;
        var controller = new InspectorAlertController(
            "localhost",
            9223,
            "Sample",
            "android",
            createDriver: (_, _, _, _, _) =>
            {
                driverCreated = true;
                return new FakeAlertDriver(null);
            },
            resolveAndroidDevice: _ => Task.FromResult(AndroidDevFlowDeviceResolution.Failed("Multiple devices own this port.")));

        var result = await controller.DetectAsync();

        Assert.False(result.Ok);
        Assert.True(result.Supported);
        Assert.Contains("Multiple devices", result.Error);
        Assert.False(driverCreated);
    }

    [Fact]
    public async Task PlatformDriverFailure_ReturnsStructuredResult()
    {
        var controller = new InspectorAlertController(
            "localhost",
            9223,
            "Sample",
            "android",
            createDriver: (_, _, _, _, _) => new ThrowingAlertDriver(),
            resolveAndroidDevice: _ => Task.FromResult(AndroidDevFlowDeviceResolution.Resolved("emulator-5554")));

        var result = await controller.DetectAsync();

        Assert.False(result.Ok);
        Assert.True(result.Supported);
        Assert.Contains("Native alert inspection failed", result.Error);
        Assert.Contains("platform tool missing", result.Error);
    }

    [Fact]
    public async Task DesktopStatusUnavailable_DoesNotReuseStaleProcessId()
    {
        var driverCreated = false;
        var controller = new InspectorAlertController(
            "localhost",
            9223,
            "Sample",
            "windows",
            processId: 1234,
            createDriver: (_, _, _, _, _) =>
            {
                driverCreated = true;
                return new FakeAlertDriver(null);
            },
            resolveProcessId: () => Task.FromResult<int?>(null));

        var result = await controller.DetectAsync();

        Assert.False(result.Ok);
        Assert.False(result.Supported);
        Assert.Contains("process identity", result.Error);
        Assert.False(driverCreated);
    }

    [Fact]
    public async Task IosWithoutDeviceIdentity_ReturnsActionableUnsupportedResult()
    {
        var controller = new InspectorAlertController("localhost", 9223, "Sample", "ios");

        var result = await controller.DetectAsync();

        Assert.False(result.Ok);
        Assert.False(result.Supported);
        Assert.Contains("simulator UDID", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DismissRoute_IsMutationProtected()
        => Assert.True(InspectorServer.IsMutation("/api/alerts/dismiss"));

    [Fact]
    public void DismissRoute_IsBlockedDuringReplay_WhileDetectionRemainsLive()
    {
        Assert.True(InspectorServer.IsBlockedDuringReplay("/api/alerts/dismiss"));
        Assert.False(InspectorServer.IsBlockedDuringReplay("/api/alerts"));
    }

    private sealed class FakeAlertDriver(AlertInfo? alert) : IAlertDriver
    {
        public string? DismissedLabel { get; private set; }

        public Task<AlertInfo?> DetectAlertAsync() => Task.FromResult(alert);

        public Task DismissAlertAsync(string? buttonLabel = null)
        {
            DismissedLabel = buttonLabel;
            return Task.CompletedTask;
        }

        public Task<AlertInfo?> HandleAlertIfPresentAsync(string? buttonLabel = null)
        {
            DismissedLabel = buttonLabel;
            return Task.FromResult(alert);
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingAlertDriver : IAlertDriver
    {
        public Task<AlertInfo?> DetectAlertAsync() => throw new System.ComponentModel.Win32Exception("platform tool missing");

        public Task DismissAlertAsync(string? buttonLabel = null) => throw new System.ComponentModel.Win32Exception("platform tool missing");

        public Task<AlertInfo?> HandleAlertIfPresentAsync(string? buttonLabel = null) => throw new System.ComponentModel.Win32Exception("platform tool missing");

        public void Dispose()
        {
        }
    }
}
