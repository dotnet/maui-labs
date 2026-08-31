using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The device layer is optional. Most machines running DevFlow will not have a device host, and a
/// desktop MAUI app never has a virtual device at all. These tests pin the guarantee that its
/// absence is an ordinary, describable state rather than an error path.
/// </summary>
public class DeviceSurfaceDegradationTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), $"devflow-devices-{Guid.NewGuid():N}");

    public DeviceSurfaceDegradationTests() => MobileCanvasHost.HomeOverride = _home;

    public void Dispose()
    {
        MobileCanvasHost.HomeOverride = null;
        if (Directory.Exists(_home))
            Directory.Delete(_home, recursive: true);
    }

    private void WriteHostState(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MobileCanvasHost.StateFilePath)!);
        File.WriteAllText(MobileCanvasHost.StateFilePath, json);
    }

    [Fact]
    public async Task NullSurface_ReportsUnavailableRatherThanThrowing()
    {
        var surface = NullDeviceSurface.Instance;

        var health = await surface.GetHealthAsync();

        Assert.False(health.Available);
        Assert.NotNull(health.Reason);
    }

    [Fact]
    public async Task NullSurface_ListsNoDevices()
    {
        var devices = await NullDeviceSurface.Instance.ListAsync();

        Assert.NotNull(devices);
        Assert.Empty(devices);
    }

    [Fact]
    public async Task NullSurface_RefusesEveryOperationWithAReason()
    {
        var surface = NullDeviceSurface.Instance;

        var boot = await surface.BootAsync("anything");
        var shutdown = await surface.ShutdownAsync("anything");
        var tap = await surface.TapAsync("anything", new DevicePoint(1, 1));

        Assert.False(boot.Success);
        Assert.False(shutdown.Success);
        Assert.False(tap.Success);
        Assert.All([boot, shutdown, tap], r => Assert.False(string.IsNullOrWhiteSpace(r.Reason)));
    }

    [Fact]
    public async Task NullSurface_ReturnsNoScreenshot()
    {
        Assert.Null(await NullDeviceSurface.Instance.ScreenshotAsync("anything"));
    }

    [Fact]
    public void HostDiscovery_FindsNothing_WhenNotInstalled()
    {
        Assert.Null(MobileCanvasHost.TryRead());
        Assert.False(MobileCanvasHost.IsPresent());
    }

    [Fact]
    public void HostDiscovery_FindsAnInstalledHost()
    {
        // Field names mirror the external host's own contract exactly. Getting these wrong is
        // silent: every request 401s or 404s and the whole layer reports as simply absent.
        WriteHostState("""{"schemaVersion":"1.0","port":54321,"processId":42,"version":"0.1.6","controlToken":"abc"}""");

        var state = MobileCanvasHost.TryRead();

        Assert.NotNull(state);
        Assert.Equal(54321, state!.Port);
        Assert.Equal(42, state.ProcessId);
        Assert.Equal("abc", state.ControlToken);
        Assert.Equal("1.0", state.SchemaVersion);
        Assert.Equal("http://127.0.0.1:54321", state.BaseUrl);
    }

    [Fact]
    public void HostDiscovery_UsesTheProtocolVersionedUpstreamPath()
    {
        Assert.EndsWith(
            Path.Combine("hosts", "v1.0", "host.json"),
            MobileCanvasHost.StateFilePath,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HostDiscovery_FallsBackToTheLegacyUnversionedPath()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(
            MobileCanvasHost.LegacyStateFilePath,
            """{"schemaVersion":"1.0","port":54321,"processId":42,"version":"0.1.6","controlToken":"abc"}""");

        Assert.Equal(
            MobileCanvasHostStateOrigin.Legacy,
            MobileCanvasHost.TryRead()!.Origin);
    }

    [Fact]
    public void HostDiscovery_IgnoresAStateFileWithNoUsablePort()
    {
        WriteHostState("""{"processId":42}""");

        Assert.Null(MobileCanvasHost.TryRead());
    }

    [Fact]
    public void HostDiscovery_IgnoresAnOutOfRangePort()
    {
        WriteHostState("""{"port":99999}""");

        Assert.Null(MobileCanvasHost.TryRead());
    }

    [Fact]
    public void HostDiscovery_DegradesToNothing_ForMalformedJson()
    {
        // The file is owned by another product; a torn or foreign write must not throw into a
        // caller that was only asking whether device control was available.
        WriteHostState("{ not json");

        Assert.Null(MobileCanvasHost.TryRead());
    }

    [Fact]
    public void HostDiscovery_ToleratesUnknownFields()
    {
        WriteHostState("""{"port":54321,"somethingWeDoNotModel":{"nested":true}}""");

        Assert.NotNull(MobileCanvasHost.TryRead());
    }

    [Fact]
    public async Task MobileCanvasSurface_ReportsUnavailable_WhenNoHostIsInstalled()
    {
        using var surface = new MobileCanvasDeviceSurface(stateProvider: () => null);

        var health = await surface.GetHealthAsync();

        Assert.False(health.Available);
        // Null, not empty: with no host we could not enumerate at all, which is different from
        // having enumerated and found nothing.
        Assert.Null(await surface.ListAsync());
        Assert.Null(await surface.GetAsync("ios:A1B2"));
    }

    [Fact]
    public async Task MobileCanvasSurface_RefusesOperations_WhenNoHostIsInstalled()
    {
        using var surface = new MobileCanvasDeviceSurface(stateProvider: () => null);

        var result = await surface.BootAsync("ios:A1B2");

        Assert.False(result.Success);
        Assert.Contains("device host", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MobileCanvasSurface_RejectsUnversionedHostStateBeforeCallingIt()
    {
        // A stale host.json outlives a crashed host. That is a normal state and must produce a
        // description, not an exception.
        var stale = new MobileCanvasHostState { Port = 1, ProcessId = 0 };
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(250) };
        using var surface = new MobileCanvasDeviceSurface(http, () => stale);

        var health = await surface.GetHealthAsync();

        Assert.Equal(DeviceHostAvailability.Incompatible, health.Availability);
        Assert.NotNull(health.Reason);
        // A dead host means enumeration failed, so callers can keep serving their last good list
        // rather than showing the user an empty one.
        Assert.Null(await surface.ListAsync());
        Assert.Null(await surface.ScreenshotAsync("ios:A1B2"));
    }

    [Fact]
    public async Task MobileCanvasSurface_RefusesACompatibleLegacyStateFile()
    {
        var legacy = new MobileCanvasHostState
        {
            SchemaVersion = "1.0",
            Version = MobileCanvasProtocol.ValidatedHostVersion,
            Port = 1,
            ProcessId = 42,
            ControlToken = "token",
            Origin = MobileCanvasHostStateOrigin.Legacy,
        };
        using var surface = new MobileCanvasDeviceSurface(stateProvider: () => legacy);

        var health = await surface.GetHealthAsync();
        var operation = await surface.BootAsync("ios:A1B2");

        Assert.Equal(DeviceHostAvailability.Incompatible, health.Availability);
        Assert.False(operation.Success);
        Assert.Equal(DeviceOperationFailureKind.Unavailable, operation.FailureKind);
    }

    [Fact]
    public async Task MobileCanvasSurface_RejectsAnEmptyDeviceId()
    {
        using var surface = new MobileCanvasDeviceSurface(stateProvider: () => new MobileCanvasHostState { Port = 1 });

        var result = await surface.TapAsync("", new DevicePoint(1, 1));

        Assert.False(result.Success);
    }

    [Fact]
    public void CapabilitiesNone_HasEverythingDisabled()
    {
        var none = DeviceCapabilities.None;

        Assert.False(none.Tap);
        Assert.False(none.LiveStream);
        Assert.False(none.Screenshot);
        Assert.False(none.Recording);
        Assert.False(none.Boot);
    }

    [Fact]
    public void DeviceTarget_DefaultsToNoCapabilities()
    {
        // A device deserialised from a host that did not report capabilities must read as
        // incapable rather than as capable-by-default.
        Assert.False(new DeviceTarget().Capabilities.Tap);
        Assert.False(new DeviceTarget().IsBooted);
    }
}
