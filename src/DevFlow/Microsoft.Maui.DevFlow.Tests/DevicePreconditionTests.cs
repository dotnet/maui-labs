using System.Text.Json;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Executable device preconditions.
/// <para>
/// The behaviour these pin hardest is refusal. A precondition that cannot be established must stop
/// the run, because continuing would produce a green test in the wrong environment — confidence
/// that was never earned, and strictly worse than not having the feature.
/// </para>
/// </summary>
public class DevicePreconditionTests
{
    private static DeviceTarget BootedDevice() => new()
    {
        Id = "ios:A1B2",
        Name = "iPhone 16",
        NativeId = "A1B2",
        State = DeviceStates.Booted,
    };

    /// <summary>A surface that records what was asked of it and can refuse selected operations.</summary>
    private sealed class RecordingSurface : IDeviceSurface
    {
        public List<string> Calls { get; } = [];
        public HashSet<string> Refuse { get; } = [];

        private Task<DeviceOperationResult> Record(string call)
        {
            Calls.Add(call);
            return Task.FromResult(Refuse.Contains(call.Split(' ')[0])
                ? DeviceOperationResult.Unsupported(call)
                : DeviceOperationResult.Ok());
        }

        public Task<DeviceHostHealth> GetHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceHostHealth { Availability = DeviceHostAvailability.Available });
        public Task<IReadOnlyList<DeviceTarget>?> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceTarget>?>([]);
        public Task<DeviceTarget?> GetAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<DeviceTarget?>(null);
        public Task<DeviceOperationResult> BootAsync(string id, CancellationToken ct = default) => Record("boot");
        public Task<DeviceOperationResult> ShutdownAsync(string id, CancellationToken ct = default) => Record("shutdown");
        public Task<DeviceOperationResult> TapAsync(string id, DevicePoint p, CancellationToken ct = default) => Record("tap");
        public Task<byte[]?> ScreenshotAsync(string id, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);

        public Task<DeviceOperationResult> SetPermissionAsync(string id, string permission, string state, CancellationToken ct = default)
            => Record($"permission {permission}={state}");
        public Task<DeviceOperationResult> SetPermissionAsync(
            string id,
            string appPackageId,
            string permission,
            string state,
            CancellationToken ct = default)
            => Record($"permission {appPackageId}:{permission}={state}");
        public Task<DeviceOperationResult> SetLocationAsync(string id, DeviceLocation? location, CancellationToken ct = default)
            => Record(location is null ? "location clear" : $"location {location.Latitude},{location.Longitude}");
        public Task<DeviceOperationResult> SetNetworkAsync(string id, string condition, CancellationToken ct = default)
            => Record($"network {condition}");
        public Task<DeviceOperationResult> SetBatteryAsync(string id, int percentage, CancellationToken ct = default)
            => Record($"battery {percentage}");
        public Task<DeviceOperationResult> RotateAsync(string id, string orientation, CancellationToken ct = default)
            => Record($"orientation {orientation}");
    }

    [Fact]
    public async Task Empty_PreconditionsAreANoOp()
    {
        var surface = new RecordingSurface();

        var result = await DevicePreconditionApplier.ApplyAsync(surface, BootedDevice(), new DevicePreconditions());

        Assert.True(result.Success);
        Assert.Empty(surface.Calls);
    }

    [Fact]
    public async Task Applies_EveryRequestedPrecondition()
    {
        var surface = new RecordingSurface();
        var preconditions = new DevicePreconditions
        {
            Permissions = new Dictionary<string, string> { ["location"] = "denied" },
            Network = "offline",
            Battery = 5,
            Orientation = DeviceOrientations.Portrait,
        };

        var result = await DevicePreconditionApplier.ApplyAsync(surface, BootedDevice(), preconditions);

        Assert.True(result.Success);
        Assert.Contains("permission location=denied", surface.Calls);
        Assert.Contains("network offline", surface.Calls);
        Assert.Contains("battery 5", surface.Calls);
        Assert.Equal(4, result.Applied.Count);
    }

    [Fact]
    public async Task AppliesPermissions_BeforeOrientation()
    {
        // Orientation goes last so the app observes a stable display; permissions go first because
        // they must be settled before anything can launch.
        var surface = new RecordingSurface();
        var preconditions = new DevicePreconditions
        {
            Permissions = new Dictionary<string, string> { ["camera"] = "granted" },
            Orientation = DeviceOrientations.LandscapeLeft,
        };

        await DevicePreconditionApplier.ApplyAsync(surface, BootedDevice(), preconditions);

        Assert.Equal(
            ["permission camera=granted", $"orientation {DeviceOrientations.LandscapeLeft}"],
            surface.Calls);
    }

    [Fact]
    public async Task Refuses_WhenAPreconditionIsUnsupported()
    {
        var surface = new RecordingSurface();
        surface.Refuse.Add("network");
        var preconditions = new DevicePreconditions { Network = "offline" };

        var result = await DevicePreconditionApplier.ApplyAsync(surface, BootedDevice(), preconditions);

        Assert.False(result.Success);
        Assert.Contains("network", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stopped", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stops_AtTheFirstRefusal_RatherThanContinuing()
    {
        // Continuing past a refusal would run the flow in a partly-prepared environment and report
        // a result that looks trustworthy and is not.
        var surface = new RecordingSurface();
        surface.Refuse.Add("network");
        var preconditions = new DevicePreconditions
        {
            Network = "offline",
            Battery = 5,
            Orientation = DeviceOrientations.Portrait,
        };

        var result = await DevicePreconditionApplier.ApplyAsync(surface, BootedDevice(), preconditions);

        Assert.False(result.Success);
        Assert.DoesNotContain("battery 5", surface.Calls);
        Assert.DoesNotContain($"orientation {DeviceOrientations.Portrait}", surface.Calls);
    }

    [Fact]
    public async Task Refuses_WhenTheDeviceIsNotBooted()
    {
        var surface = new RecordingSurface();
        var device = BootedDevice() with { State = DeviceStates.Shutdown };

        var result = await DevicePreconditionApplier.ApplyAsync(
            surface, device, new DevicePreconditions { Network = "offline" });

        Assert.False(result.Success);
        Assert.Contains("not booted", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(surface.Calls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Refuses_AnOutOfRangeBatteryLevel_BeforeTouchingTheDevice(int battery)
    {
        // A preceding precondition is included deliberately: without it this test would pass even
        // if validation ran after permissions and network had already been applied, leaving the
        // device half-prepared while reporting a refusal.
        var surface = new RecordingSurface();
        var preconditions = new DevicePreconditions
        {
            Permissions = new Dictionary<string, string> { ["camera"] = "granted" },
            Network = "offline",
            Battery = battery,
        };

        var result = await DevicePreconditionApplier.ApplyAsync(surface, BootedDevice(), preconditions);

        Assert.False(result.Success);
        Assert.Empty(surface.Calls);
        Assert.Empty(result.Applied);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sometimes")]
    public async Task Refuses_InvalidPermissionStates_BeforeTouchingTheDevice(string state)
    {
        var surface = new RecordingSurface();
        var preconditions = new DevicePreconditions
        {
            Permissions = new Dictionary<string, string>
            {
                ["camera"] = "granted",
                ["microphone"] = state,
            },
        };

        var result = await DevicePreconditionApplier.ApplyAsync(surface, BootedDevice(), preconditions);

        Assert.False(result.Success);
        Assert.Empty(surface.Calls);
        Assert.Empty(result.Applied);
    }

    [Fact]
    public async Task Refuses_NullPermissionState_BeforeTouchingTheDevice()
    {
        var surface = new RecordingSurface();
        var preconditions = new DevicePreconditions
        {
            Permissions = new Dictionary<string, string>
            {
                ["camera"] = "granted",
                ["microphone"] = null!,
            },
        };

        var result = await DevicePreconditionApplier.ApplyAsync(surface, BootedDevice(), preconditions);

        Assert.False(result.Success);
        Assert.Empty(surface.Calls);
    }

    [Fact]
    public async Task Refuses_EmptyPermissionName_BeforeTouchingTheDevice()
    {
        var surface = new RecordingSurface();
        var preconditions = new DevicePreconditions
        {
            Permissions = new Dictionary<string, string>
            {
                ["camera"] = "granted",
                [" "] = "denied",
            },
        };

        var result = await DevicePreconditionApplier.ApplyAsync(surface, BootedDevice(), preconditions);

        Assert.False(result.Success);
        Assert.Empty(surface.Calls);
    }

    [Fact]
    public async Task Refuses_InvalidOrientation_BeforeTouchingTheDevice()
    {
        var surface = new RecordingSurface();
        var preconditions = new DevicePreconditions
        {
            Permissions = new Dictionary<string, string> { ["camera"] = "granted" },
            Orientation = "diagonal",
        };

        var result = await DevicePreconditionApplier.ApplyAsync(surface, BootedDevice(), preconditions);

        Assert.False(result.Success);
        Assert.Empty(surface.Calls);
    }

    [Fact]
    public async Task RefusesUnverifiableLocationBeforeTouchingTheDevice()
    {
        var surface = new RecordingSurface();

        var result = await DevicePreconditionApplier.ApplyAsync(
            surface, BootedDevice(), new DevicePreconditions { ClearLocation = true });

        Assert.False(result.Success);
        Assert.Contains("cannot be verified", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(surface.Calls);
    }

    [Fact]
    public async Task PackageAwarePermissionTargetsTheExactApp()
    {
        var surface = new RecordingSurface();

        var result = await DevicePreconditionApplier.ApplyAsync(
            surface,
            BootedDevice(),
            new DevicePreconditions
            {
                Permissions = new Dictionary<string, string> { ["camera"] = "denied" },
            },
            "com.example.app");

        Assert.True(result.Success);
        Assert.Contains("permission com.example.app:camera=denied", surface.Calls);
    }

    [Fact]
    public async Task DefaultSurface_RefusesEnvironmentOperations()
    {
        // A backend that has not adopted these must refuse rather than silently succeed, or a
        // caller would believe an environment was prepared when nothing happened.
        var result = await DevicePreconditionApplier.ApplyAsync(
            NullDeviceSurface.Instance, BootedDevice(), new DevicePreconditions { Network = "offline" });

        Assert.False(result.Success);
    }

    [Fact]
    public void ParsesFromFlowExtensionData()
    {
        var json = """
            {"devicePreconditions":{"network":"offline","battery":5,
             "permissions":{"location":"denied"},
             "location":{"latitude":51.5,"longitude":-0.12}}}
            """;
        var extensionData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        var preconditions = DevicePreconditions.FromExtensionData(extensionData);

        Assert.NotNull(preconditions);
        Assert.Equal("offline", preconditions!.Network);
        Assert.Equal(5, preconditions.Battery);
        Assert.Equal("denied", preconditions.Permissions!["location"]);
        Assert.Equal(51.5, preconditions.Location!.Latitude);
    }

    [Fact]
    public void ReturnsNull_WhenAFlowDeclaresNoDevicePreconditions()
    {
        // The overwhelmingly common case: existing flows carry no device block and must be
        // unaffected.
        Assert.Null(DevicePreconditions.FromExtensionData(null));
        Assert.Null(DevicePreconditions.FromExtensionData(new Dictionary<string, JsonElement>()));
    }
}
