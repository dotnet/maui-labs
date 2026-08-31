using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The broker's device cache.
/// <para>
/// A review found three defects here that no test covered, all of the same shape: state that is
/// briefly stale is acceptable, state that is <em>permanently wrong</em> is not. These pin the
/// distinction.
/// </para>
/// </summary>
public class DeviceRegistryTests
{
    private sealed class ScriptedSurface : IDeviceSurface
    {
        public Queue<IReadOnlyList<DeviceTarget>?> Results { get; } = new();
        public int ListCalls { get; private set; }
        public bool ShutdownSucceeds { get; set; } = true;

        public Task<DeviceHostHealth> GetHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceHostHealth { Availability = DeviceHostAvailability.Available });

        public Task<IReadOnlyList<DeviceTarget>?> ListAsync(CancellationToken ct = default)
        {
            ListCalls++;
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : null);
        }

        public Task<DeviceTarget?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<DeviceTarget?>(null);
        public Task<DeviceOperationResult> BootAsync(string id, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.Ok());
        public Task<DeviceOperationResult> ShutdownAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(ShutdownSucceeds ? DeviceOperationResult.Ok() : DeviceOperationResult.Failed("no"));
        public Task<DeviceOperationResult> TapAsync(string id, DevicePoint p, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.Ok());
        public Task<byte[]?> ScreenshotAsync(string id, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
    }

    private static DeviceTarget Device(string id = "ios:A1B2", string state = DeviceStates.Booted) => new()
    {
        Id = id,
        Platform = DevicePlatforms.Ios,
        Name = "iPhone 16",
        NativeId = "A1B2",
        Udid = "A1B2",
        State = state,
    };

    [Fact]
    public void CapabilityProjection_AdvertisesOnlyBrokerExecutableOperations()
    {
        var projected = DeviceCapabilityProjection.Create(new DeviceCapabilities
        {
            Boot = true,
            Shutdown = true,
            Tap = true,
            Screenshot = true,
            LiveStream = true,
            Rotate = true,
            Recording = true,
        });

        Assert.True(projected["boot"]!.GetValue<bool>());
        Assert.True(projected["shutdown"]!.GetValue<bool>());
        Assert.True(projected["tap"]!.GetValue<bool>());
        Assert.True(projected["rotate"]!.GetValue<bool>());
        Assert.True(projected["recording"]!.GetValue<bool>());
        Assert.False(projected["scroll"]!.GetValue<bool>());
    }

    [Fact]
    public async Task AnEmptyEnumerationIsCached_SoAShutDownDeviceDisappears()
    {
        // The bug this pins: treating "enumerated, none present" as a failure meant the last
        // device stayed listed forever, still reporting itself as booted.
        var surface = new ScriptedSurface();
        surface.Results.Enqueue(new[] { Device() });
        surface.Results.Enqueue([]);
        var registry = new DeviceRegistry(surface);

        Assert.Single(await registry.ListAsync());
        Assert.Empty(await registry.ListAsync(forceRefresh: true));
    }

    [Fact]
    public async Task AFailedEnumerationServesTheLastGoodAnswer()
    {
        // Devices blinking out of the Inspector because a host was momentarily busy is worse than
        // briefly stale state — but only for genuine failures, which are now signalled by null.
        var surface = new ScriptedSurface();
        surface.Results.Enqueue(new[] { Device() });
        surface.Results.Enqueue(null);
        var registry = new DeviceRegistry(surface);

        Assert.Single(await registry.ListAsync());
        Assert.Single(await registry.ListAsync(forceRefresh: true));
    }

    [Fact]
    public async Task RepeatedFailuresDoNotHammerTheHost()
    {
        // The failure path used to skip stamping the cache timestamp, so every later call took the
        // slow path and hit the host again with no backoff.
        var surface = new ScriptedSurface();
        surface.Results.Enqueue(new[] { Device() });
        var registry = new DeviceRegistry(surface);

        await registry.ListAsync();
        await registry.ListAsync(forceRefresh: true);
        var callsAfterFailure = surface.ListCalls;

        await registry.ListAsync();
        await registry.ListAsync();

        Assert.Equal(callsAfterFailure, surface.ListCalls);
    }

    [Fact]
    public async Task ResultsAreCachedWithinTheCacheLifetime()
    {
        var surface = new ScriptedSurface();
        surface.Results.Enqueue(new[] { Device() });
        var registry = new DeviceRegistry(surface);

        await registry.ListAsync();
        await registry.ListAsync();
        await registry.ListAsync();

        Assert.Equal(1, surface.ListCalls);
    }

    [Fact]
    public async Task AShutdownInvalidatesTheCache()
    {
        var surface = new ScriptedSurface();
        surface.Results.Enqueue(new[] { Device() });
        surface.Results.Enqueue(new[] { Device(state: DeviceStates.Shutdown) });
        var registry = new DeviceRegistry(surface);

        await registry.ListAsync();
        var result = await registry.ShutdownAsync("ios:A1B2");
        var devices = await registry.ListAsync();

        Assert.True(result.Success);
        Assert.False(devices.Single().IsBooted);
    }

    [Fact]
    public async Task AFailedMutationDoesNotInvalidateTheCache()
    {
        var surface = new ScriptedSurface { ShutdownSucceeds = false };
        surface.Results.Enqueue(new[] { Device() });
        var registry = new DeviceRegistry(surface);

        await registry.ListAsync();
        await registry.ShutdownAsync("ios:A1B2");
        await registry.ListAsync();

        Assert.Equal(1, surface.ListCalls);
    }

    [Fact]
    public async Task PairsAnAgentToItsDevice()
    {
        var surface = new ScriptedSurface();
        surface.Results.Enqueue(new[] { Device() });
        var registry = new DeviceRegistry(surface);

        var paired = await registry.ListPairedAsync([
            new AgentRegistration { Id = "agent-1", Port = 9223, DeviceId = "platform=ios;udid=A1B2" },
        ]);

        var entry = Assert.Single(paired);
        Assert.Equal("agent-1", entry.AgentId);
        Assert.Equal(9223, entry.AgentPort);
        Assert.Equal(DeviceMatchConfidence.Exact, entry.MatchConfidence);
    }

    [Fact]
    public async Task LeavesADeviceUnpaired_WhenNoAgentClaimsIt()
    {
        var surface = new ScriptedSurface();
        surface.Results.Enqueue(new[] { Device() });
        var registry = new DeviceRegistry(surface);

        var paired = await registry.ListPairedAsync([
            new AgentRegistration { Id = "desktop", Port = 9223, DeviceId = null },
        ]);

        Assert.Null(Assert.Single(paired).AgentId);
    }

    [Fact]
    public async Task RefusesToPairAnAgentAcrossPlatforms()
    {
        var surface = new ScriptedSurface();
        surface.Results.Enqueue(new[] { Device() });
        var registry = new DeviceRegistry(surface);

        var paired = await registry.ListPairedAsync([
            new AgentRegistration
            {
                Id = "android-app",
                Port = 9223,
                DeviceId = "platform=android;serial=A1B2;avd=iPhone 16",
            },
        ]);

        var entry = Assert.Single(paired);
        Assert.Null(entry.AgentId);
        Assert.Equal(DeviceMatchConfidence.None, entry.MatchConfidence);
    }

    [Fact]
    public async Task RefusesToAttributeADevice_WhenTwoAgentsClaimItEqually()
    {
        // Attributing the device to the wrong app is silent, and every coordinate afterwards would
        // target the wrong window.
        var surface = new ScriptedSurface();
        surface.Results.Enqueue(new[] { Device() });
        var registry = new DeviceRegistry(surface);

        var paired = await registry.ListPairedAsync([
            new AgentRegistration { Id = "a", Port = 1, DeviceId = "platform=ios;udid=A1B2" },
            new AgentRegistration { Id = "b", Port = 2, DeviceId = "platform=ios;udid=A1B2" },
        ]);

        Assert.Null(Assert.Single(paired).AgentId);
    }

    [Fact]
    public async Task PrefersAnExactClaimOverAWeakerOne()
    {
        var surface = new ScriptedSurface();
        surface.Results.Enqueue(new[]
        {
            Device() with { AvdName = "Pixel_8", Name = "Pixel 8" },
        });
        var registry = new DeviceRegistry(surface);

        var paired = await registry.ListPairedAsync([
            new AgentRegistration { Id = "weak", Port = 1, DeviceId = "platform=android;avd=Pixel_8" },
            new AgentRegistration { Id = "exact", Port = 2, DeviceId = "platform=ios;udid=A1B2" },
        ]);

        Assert.Equal("exact", Assert.Single(paired).AgentId);
    }
}
