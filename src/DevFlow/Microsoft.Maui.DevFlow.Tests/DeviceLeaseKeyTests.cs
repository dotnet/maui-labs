using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// How the mutation lease covers device-level input.
/// <para>
/// A stable device-derived key governs app and companion input before launch, while paired, across
/// reconnects, and after a crash. Moving between agent and device namespaces would briefly admit
/// two writers during every pairing transition.
/// </para>
/// </summary>
public class DeviceLeaseKeyTests
{
    private sealed class OneDeviceSurface(DeviceTarget device) : IDeviceSurface
    {
        public Task<DeviceHostHealth> GetHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceHostHealth { Availability = DeviceHostAvailability.Available });
        public Task<IReadOnlyList<DeviceTarget>?> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceTarget>?>([device]);
        public Task<DeviceTarget?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<DeviceTarget?>(device);
        public Task<DeviceOperationResult> BootAsync(string id, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.Ok());
        public Task<DeviceOperationResult> ShutdownAsync(string id, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.Ok());
        public Task<DeviceOperationResult> TapAsync(string id, DevicePoint p, CancellationToken ct = default) => Task.FromResult(DeviceOperationResult.Ok());
        public Task<byte[]?> ScreenshotAsync(string id, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
    }

    private static DeviceTarget Device() => new()
    {
        Id = "ios:A1B2",
        Platform = DevicePlatforms.Ios,
        Name = "iPhone 16",
        NativeId = "A1B2",
        Udid = "A1B2",
        State = DeviceStates.Booted,
    };

    [Fact]
    public async Task UsesTheStableDeviceKey_SoPairingChangesDoNotMoveTheLease()
    {
        // The point of the whole design: a device tap and a maui_tap on the same screen must
        // compete for one lease, not hold two independent ones.
        var registry = new DeviceRegistry(new OneDeviceSurface(Device()));
        var agents = new[]
        {
            new AgentRegistration { Id = "agent-42", Port = 9223, DeviceId = "platform=ios;udid=A1B2" },
        };

        var key = await registry.ResolveLeaseKeyAsync("ios:A1B2", agents);

        Assert.Equal(DeviceLeaseKeys.FromTarget(Device()), key);
        Assert.Equal(
            DeviceLeaseKeys.FromIdentity(DeviceIdentity.Parse(agents[0].DeviceId)),
            key);
        Assert.Equal(
            key,
            BrokerServer.LeaseKeyForRegistration(agents[0]));
    }

    [Fact]
    public async Task FallsBackToADeviceScopedKey_WhenNoAppIsRunning()
    {
        // Before launch or after a crash there is no agent to key on, but the tap still has to be
        // arbitrated against other sessions driving the same device.
        var registry = new DeviceRegistry(new OneDeviceSurface(Device()));

        var key = await registry.ResolveLeaseKeyAsync("ios:A1B2", []);

        Assert.Equal(DeviceLeaseKeys.FromTarget(Device()), key);
    }

    [Fact]
    public async Task FallsBackToADeviceScopedKey_WhenTheAgentIsOnAnotherDevice()
    {
        var registry = new DeviceRegistry(new OneDeviceSurface(Device()));
        var agents = new[]
        {
            new AgentRegistration { Id = "elsewhere", Port = 9223, DeviceId = "platform=android;serial=emulator-5554" },
        };

        var key = await registry.ResolveLeaseKeyAsync("ios:A1B2", agents);

        Assert.Equal(DeviceLeaseKeys.FromTarget(Device()), key);
    }

    [Fact]
    public async Task FallsBackToADeviceScopedKey_ForAnUnknownDevice()
    {
        // Distinct from the no-app case: here an app IS running and paired, but the caller asked
        // about a different device, so the agent's key must not be borrowed for it.
        var registry = new DeviceRegistry(new OneDeviceSurface(Device()));
        var agents = new[]
        {
            new AgentRegistration { Id = "agent-42", Port = 9223, DeviceId = "platform=ios;udid=A1B2" },
        };

        var key = await registry.ResolveLeaseKeyAsync("ios:UNKNOWN", agents);

        Assert.Equal("device:ios:UNKNOWN", key);
    }

    [Fact]
    public void AndroidAvdIdentityAndCompanionTargetDeriveTheSameStableKey()
    {
        var device = new DeviceTarget
        {
            Id = "android:emulator:emulator-5554",
            Platform = DevicePlatforms.Android,
            NativeId = "emulator-5554",
            AvdName = "Pixel_8_API_35",
        };
        var identity = DeviceIdentity.Parse("platform=android;serial=ro-serial;avd=Pixel 8 API 35");

        Assert.Equal(DeviceLeaseKeys.FromTarget(device), DeviceLeaseKeys.FromIdentity(identity));
    }

    [Fact]
    public void DeviceScopedKeysAreNamespaced_SoTheyCannotCollideWithAnAgentId()
    {
        // Agent ids are hex digests, so a "device:" prefix cannot be mistaken for one.
        Assert.StartsWith("device:", DeviceRegistry.DeviceLeaseKey("anything"));
    }

    [Fact]
    public void TheHolderIsAdmitted_AndEveryoneElseIsRefused()
    {
        // The behaviour that matters, and the one an earlier version of this test missed: probing
        // without a lease id reports "held by other" even to the session that holds it, so a
        // presence check would refuse exactly the caller that should be allowed. Only passing the
        // caller's own lease id distinguishes them.
        var leases = new MutationLeaseRegistry();
        leases.Control("agent-42", "claim", "lease-holder", "inspector", "Inspector", force: false);

        var holder = leases.Control("agent-42", "validate", "lease-holder", null, null, force: false);
        var other = leases.Control("agent-42", "validate", "lease-other", null, null, force: false);
        var anonymous = leases.Control("agent-42", "validate", null, null, null, force: false);

        Assert.False(holder.HeldByOther);
        Assert.True(other.HeldByOther);
        Assert.True(anonymous.HeldByOther);
    }

    [Fact]
    public void AnUnclaimedKeyAdmitsTheFirstCaller()
    {
        // Before an app launches nobody has claimed the device key. "Nobody holds it" must not be
        // read as "nobody may drive it", or device control would be impossible in exactly the
        // situation it exists for.
        var leases = new MutationLeaseRegistry();

        var first = leases.Control(
            DeviceRegistry.DeviceLeaseKey("ios:A1B2"), "validate", "lease-1", null, null, force: false);

        Assert.False(first.HeldByOther);
    }

    private static AgentRegistration Registration(
        string id = "agent-42",
        string instanceId = "instance-1",
        string deviceId = "platform=ios;udid=A1B2") =>
        new() { Id = id, InstanceId = instanceId, Port = 9223, DeviceId = deviceId };

    /// <summary>
    /// A desktop agent has no device identity, so its lease key is the agent id itself. That key
    /// looked private enough to justify dropping unconditionally, and it is not: the Inspector
    /// claims it, an approved run adopts it, and a relaunch re-registers under the same id.
    /// </summary>
    private static AgentRegistration DesktopRegistration(
        string id = "agent-desktop",
        string instanceId = "instance-1") =>
        new() { Id = id, InstanceId = instanceId, Port = 9223, DeviceId = null };

    /// <summary>
    /// The crash case, and the reason fast recovery exists at all: the app died holding the device
    /// lease, and the next session must not wait out the lease TTL to drive the same device.
    /// </summary>
    [Fact]
    public void DisconnectReleasesTheDeviceLeaseTheDeadAgentOwned()
    {
        var registration = Registration();
        var key = BrokerServer.LeaseKeyForRegistration(registration);
        var owner = BrokerServer.AgentLeaseOwner(registration);
        var leases = new MutationLeaseRegistry();
        leases.Control(key, "claim", "lease-app", "web-inspector", "Inspector", force: false, null, owner);

        Assert.True(leases.RemoveIfOwnedBy(key, owner));
        Assert.False(leases.Control(key, "validate", "lease-other", null, null, force: false).HeldByOther);
    }

    /// <summary>
    /// A second agent on a second device shares nothing, but the failure this guards against is the
    /// one where a crashing agent clears the lease of a live agent on the <em>same</em> device.
    /// </summary>
    [Fact]
    public void ADisconnectingAgentCannotClearAnotherLiveAgentsHoldOnTheSameDevice()
    {
        var dead = Registration(id: "agent-dead", instanceId: "instance-dead");
        var live = Registration(id: "agent-live", instanceId: "instance-live");
        var key = BrokerServer.LeaseKeyForRegistration(dead);
        Assert.Equal(key, BrokerServer.LeaseKeyForRegistration(live));

        var leases = new MutationLeaseRegistry();
        leases.Control(
            key, "claim", "lease-live", "web-inspector", "Inspector",
            force: false, null, BrokerServer.AgentLeaseOwner(live));

        Assert.False(leases.RemoveIfOwnedBy(key, BrokerServer.AgentLeaseOwner(dead)));
        Assert.True(
            leases.Control(key, "validate", "lease-someone-else", null, null, force: false).HeldByOther);
    }

    /// <summary>
    /// A reconnect that reuses the same instance is the same holder. Recovery keyed on the agent id
    /// alone would revoke a lease the reconnected process is still legitimately using.
    /// </summary>
    [Fact]
    public void AReconnectUnderANewInstanceDoesNotInheritTheOldLease()
    {
        var before = Registration(instanceId: "instance-before");
        var after = Registration(instanceId: "instance-after");
        var key = BrokerServer.LeaseKeyForRegistration(before);
        var leases = new MutationLeaseRegistry();
        leases.Control(
            key, "claim", "lease-after", "web-inspector", "Inspector",
            force: false, null, BrokerServer.AgentLeaseOwner(after));

        // The old instance disconnecting must not take the new instance's lease with it.
        Assert.False(leases.RemoveIfOwnedBy(key, BrokerServer.AgentLeaseOwner(before)));
        Assert.Equal(BrokerServer.AgentLeaseOwner(after), leases.OwnerOf(key));
    }

    /// <summary>
    /// Device control and the companion MCP take the same key without an owner, because they are not
    /// an app. An unattributed holder is never recovered by an agent disconnect.
    /// </summary>
    [Fact]
    public void AnUnattributedDeviceHolderSurvivesAnAgentDisconnect()
    {
        var registration = Registration();
        var key = BrokerServer.LeaseKeyForRegistration(registration);
        var leases = new MutationLeaseRegistry();
        leases.ClaimAndBeginExclusive(
            key, "lease-companion", "transaction-1", "mobile-canvas-mcp", "companion", out _);

        Assert.False(leases.RemoveIfOwnedBy(key, BrokerServer.AgentLeaseOwner(registration)));
        Assert.True(
            leases.Control(key, "validate", "lease-other", null, null, force: false).HeldByOther);
    }

    /// <summary>
    /// Recovery is not a privileged path around the transaction rule. A disconnect while a device
    /// mutation is in flight leaves the lease alone — the same answer an explicit <c>release</c>
    /// gets — because clearing it there would hand the hardware to a second writer mid-tap.
    /// </summary>
    [Fact]
    public void ADisconnectDoesNotClearALeaseWithATransactionInFlight()
    {
        var registration = Registration();
        var key = BrokerServer.LeaseKeyForRegistration(registration);
        var owner = BrokerServer.AgentLeaseOwner(registration);
        var leases = new MutationLeaseRegistry();
        leases.Control(key, "claim", "lease-app", "web-inspector", "Inspector", force: false, null, owner);
        var began = leases.Control(
            key, "begin", "lease-app", null, null, force: false, "transaction-1", owner);
        Assert.Equal("transaction-1", began.TransactionId);

        Assert.False(leases.RemoveIfOwnedBy(key, owner));

        // The holder is untouched: still the same lease, still attributed to the dead agent, and
        // still refusing everyone else.
        Assert.Equal(owner, leases.OwnerOf(key));
        Assert.True(
            leases.Control(key, "validate", "lease-other", null, null, force: false).HeldByOther);
        // A release refuses for the same reason, so recovery is no weaker and no stronger.
        Assert.True(
            leases.Control(key, "release", "lease-app", null, null, force: false).YouHold);
    }

    /// <summary>
    /// The wait is bounded by the transaction itself: once it ends, the very next recovery attempt
    /// releases the lease the disconnected agent owned.
    /// </summary>
    [Fact]
    public void RecoverySucceedsOnceTheTransactionEnds()
    {
        var registration = Registration();
        var key = BrokerServer.LeaseKeyForRegistration(registration);
        var owner = BrokerServer.AgentLeaseOwner(registration);
        var leases = new MutationLeaseRegistry();
        leases.Control(key, "claim", "lease-app", "web-inspector", "Inspector", force: false, null, owner);
        leases.Control(key, "begin", "lease-app", null, null, force: false, "transaction-1", owner);
        Assert.False(leases.RemoveIfOwnedBy(key, owner));

        leases.Control(key, "end", "lease-app", null, null, force: false, "transaction-1", owner);

        Assert.True(leases.RemoveIfOwnedBy(key, owner));
        Assert.Null(leases.OwnerOf(key));
        Assert.False(leases.Control(key, "validate", "lease-other", null, null, force: false).HeldByOther);
    }

    /// <summary>
    /// An abandoned transaction must not pin a lease forever. Expiry is what bounds the refusal, so
    /// a crashed agent that never sent <c>end</c> is still recovered after the transaction window.
    /// </summary>
    [Fact]
    public void RecoverySucceedsOnceAnAbandonedTransactionExpires()
    {
        var registration = Registration();
        var key = BrokerServer.LeaseKeyForRegistration(registration);
        var owner = BrokerServer.AgentLeaseOwner(registration);
        var ticks = 0L;
        // A lease window longer than the transaction window isolates what is being measured: the
        // lease is still live at the point the abandoned transaction ages out.
        var leases = new MutationLeaseRegistry(
            leaseDurationMs: 300_000,
            transactionDurationMs: 60_000,
            getTicks: () => ticks);
        leases.Control(key, "claim", "lease-app", "web-inspector", "Inspector", force: false, null, owner);
        leases.Control(key, "begin", "lease-app", null, null, force: false, "transaction-1", owner);

        Assert.False(leases.RemoveIfOwnedBy(key, owner));

        ticks = 61_000;

        Assert.True(leases.RemoveIfOwnedBy(key, owner));
        Assert.Null(leases.OwnerOf(key));
        Assert.False(leases.Control(key, "validate", "lease-other", null, null, force: false).HeldByOther);
    }

    /// <summary>
    /// Releasing a lease is an authority change even though nobody holds the result, so a client
    /// caching an epoch can tell that its hold is gone.
    /// </summary>
    [Fact]
    public void RecoveryAdvancesTheAuthorityEpoch()
    {
        var registration = Registration();
        var key = BrokerServer.LeaseKeyForRegistration(registration);
        var owner = BrokerServer.AgentLeaseOwner(registration);
        var leases = new MutationLeaseRegistry();
        var claimed = leases.Control(
            key, "claim", "lease-app", "web-inspector", "Inspector", force: false, null, owner);

        leases.RemoveIfOwnedBy(key, owner);
        var after = leases.Control(key, "status", null, null, null, force: false);

        Assert.True(after.AuthorityEpoch > claimed.AuthorityEpoch);
    }

    /// <summary>
    /// The owner carries the instance, so two different processes under one agent id are never
    /// confused for each other.
    /// </summary>
    [Fact]
    public void TheOwnerDistinguishesInstancesOfTheSameAgentId()
    {
        Assert.NotEqual(
            BrokerServer.AgentLeaseOwner(Registration(instanceId: "a")),
            BrokerServer.AgentLeaseOwner(Registration(instanceId: "b")));
        Assert.Equal(
            BrokerServer.AgentLeaseOwner(Registration()),
            BrokerServer.AgentLeaseOwner(Registration()));
    }

    /// <summary>
    /// The disconnect policy itself: a socket flap that re-registers the identical instance leaves
    /// the lease alone, while a genuinely dead instance — or a relaunch under a new instance — is
    /// recovered. It is one policy for every lease key shape, because the reasoning that made an
    /// app key look exempt ("only this agent can hold it") was never true.
    /// </summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData("instance-1", false)]
    [InlineData("instance-2", true)]
    public void RecoveryStandsDownForAnExactReplacement(string? replacementInstance, bool recovers)
    {
        var disconnected = Registration(instanceId: "instance-1");
        var replacement = replacementInstance is null
            ? null
            : Registration(instanceId: replacementInstance);

        Assert.Equal(
            recovers,
            BrokerServer.ShouldRecoverLeaseOnDisconnect(disconnected, replacement));
    }

    /// <summary>
    /// The same policy answers for a desktop agent, whose lease key is the agent id. There is no
    /// second, laxer path for app-keyed leases to fall through.
    /// </summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData("instance-1", false)]
    [InlineData("instance-2", true)]
    public void RecoveryStandsDownForAnExactReplacementOfADesktopAgent(
        string? replacementInstance,
        bool recovers)
    {
        var disconnected = DesktopRegistration(instanceId: "instance-1");
        var replacement = replacementInstance is null
            ? null
            : DesktopRegistration(instanceId: replacementInstance);

        Assert.Equal(disconnected.Id, BrokerServer.LeaseKeyForRegistration(disconnected));
        Assert.Equal(
            recovers,
            BrokerServer.ShouldRecoverLeaseOnDisconnect(disconnected, replacement));
    }

    /// <summary>
    /// A desktop agent's lease is keyed on the agent id, and it is still recovered on disconnect —
    /// the convenience the fast path exists for is not lost by making it ownership-aware.
    /// </summary>
    [Fact]
    public void DisconnectReleasesTheAppKeyedLeaseTheDeadAgentOwned()
    {
        var registration = DesktopRegistration();
        var key = BrokerServer.LeaseKeyForRegistration(registration);
        var owner = BrokerServer.AgentLeaseOwner(registration);
        var leases = new MutationLeaseRegistry();
        leases.Control(key, "claim", "lease-app", "web-inspector", "Inspector", force: false, null, owner);

        Assert.True(leases.RemoveIfOwnedBy(key, owner));
        Assert.Null(leases.OwnerOf(key));
        Assert.False(leases.Control(key, "validate", "lease-other", null, null, force: false).HeldByOther);
    }

    /// <summary>
    /// The regression this replaced: an app-keyed lease used to be dropped outright on disconnect,
    /// which cut a transaction that was already in flight. A mutation mid-flight is exactly when a
    /// second writer must not be admitted, whatever the key is shaped like.
    /// </summary>
    [Fact]
    public void ADesktopDisconnectDoesNotClearAnAppKeyedLeaseWithATransactionInFlight()
    {
        var registration = DesktopRegistration();
        var key = BrokerServer.LeaseKeyForRegistration(registration);
        var owner = BrokerServer.AgentLeaseOwner(registration);
        var leases = new MutationLeaseRegistry();
        leases.Control(key, "claim", "lease-app", "web-inspector", "Inspector", force: false, null, owner);
        var began = leases.Control(
            key, "begin", "lease-app", null, null, force: false, "transaction-1", owner);
        Assert.Equal("transaction-1", began.TransactionId);

        Assert.False(leases.RemoveIfOwnedBy(key, owner));

        Assert.Equal(owner, leases.OwnerOf(key));
        Assert.True(
            leases.Control(key, "validate", "lease-other", null, null, force: false).HeldByOther);

        // And the wait is bounded: once the transaction ends, recovery succeeds.
        leases.Control(key, "end", "lease-app", null, null, force: false, "transaction-1", owner);
        Assert.True(leases.RemoveIfOwnedBy(key, owner));
    }

    /// <summary>
    /// A relaunch under a new instance re-registers with the same agent id, so an app-keyed lease is
    /// not private to the process that took it. The old instance disconnecting must not revoke the
    /// new one's hold.
    /// </summary>
    [Fact]
    public void ADesktopReconnectUnderANewInstanceKeepsItsOwnAppKeyedLease()
    {
        var before = DesktopRegistration(instanceId: "instance-before");
        var after = DesktopRegistration(instanceId: "instance-after");
        var key = BrokerServer.LeaseKeyForRegistration(before);
        Assert.Equal(key, BrokerServer.LeaseKeyForRegistration(after));
        var leases = new MutationLeaseRegistry();
        leases.Control(
            key, "claim", "lease-after", "web-inspector", "Inspector",
            force: false, null, BrokerServer.AgentLeaseOwner(after));

        Assert.False(leases.RemoveIfOwnedBy(key, BrokerServer.AgentLeaseOwner(before)));
        Assert.Equal(BrokerServer.AgentLeaseOwner(after), leases.OwnerOf(key));
        Assert.True(
            leases.Control(key, "validate", "lease-someone-else", null, null, force: false).HeldByOther);
    }

    /// <summary>
    /// An unattributed holder on an app key — the Inspector's own hold, taken before any agent run
    /// claimed it — is left alone too. Dropping the entry used to take it with everything else.
    /// </summary>
    [Fact]
    public void AnUnattributedAppKeyedHolderSurvivesAnAgentDisconnect()
    {
        var registration = DesktopRegistration();
        var key = BrokerServer.LeaseKeyForRegistration(registration);
        var leases = new MutationLeaseRegistry();
        leases.Control(key, "claim", "lease-inspector", "web-inspector", "Inspector", force: false);

        Assert.False(leases.RemoveIfOwnedBy(key, BrokerServer.AgentLeaseOwner(registration)));
        Assert.True(
            leases.Control(key, "validate", "lease-other", null, null, force: false).HeldByOther);
    }

    /// <summary>
    /// Recovering an app-keyed lease advances the authority epoch instead of discarding it.
    /// Removing the whole entry reset the counter to zero, so a client that had cached a higher
    /// epoch could be handed a stale-looking-but-lower one after a reclaim and never notice its
    /// hold had changed hands.
    /// </summary>
    [Fact]
    public void AppKeyedRecoveryAdvancesTheAuthorityEpochRatherThanResettingIt()
    {
        var registration = DesktopRegistration();
        var key = BrokerServer.LeaseKeyForRegistration(registration);
        var owner = BrokerServer.AgentLeaseOwner(registration);
        var leases = new MutationLeaseRegistry();
        var claimed = leases.Control(
            key, "claim", "lease-app", "web-inspector", "Inspector", force: false, null, owner);

        Assert.True(leases.RemoveIfOwnedBy(key, owner));
        var afterRecovery = leases.Control(key, "status", null, null, null, force: false);
        Assert.True(afterRecovery.AuthorityEpoch > claimed.AuthorityEpoch);

        // The next holder keeps climbing from there rather than restarting the sequence.
        var reclaimed = leases.Control(
            key, "claim", "lease-next", "web-inspector", "Inspector", force: false, null, owner);
        Assert.True(reclaimed.AuthorityEpoch > afterRecovery.AuthorityEpoch);
    }
}
