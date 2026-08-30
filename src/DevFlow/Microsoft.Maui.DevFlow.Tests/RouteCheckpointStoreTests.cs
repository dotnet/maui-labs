using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Driver;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class RouteCheckpointStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "route-checkpoint-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RouteCheckpointStatus_IsSupportedByCliJsonContext()
    {
        var json = CliJson.SerializeUntyped(new RouteCheckpointStatus
        {
            Ok = true,
            Connected = true,
            HasCheckpoint = true,
            Checkpoint = new RouteCheckpoint
            {
                AgentId = "agent",
                Route = "//home",
                SavedUtc = DateTimeOffset.Parse("2026-07-31T00:00:00Z")
            }
        });

        Assert.Contains("\"checkpoint\"", json, StringComparison.Ordinal);
        Assert.Contains("//home", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ResumeOutput_RedactsRouteQueryValuesWithoutChangingStoredRoute()
    {
        var status = new RouteCheckpointStatus
        {
            HasCheckpoint = true,
            Checkpoint = new RouteCheckpoint
            {
                AgentId = "agent",
                Route = "//pay?token=SECRET&id=42#receipt",
                SavedUtc = DateTimeOffset.Parse("2026-07-31T00:00:00Z"),
                LastRestore = new RouteRestoreResult
                {
                    ObservedRoute = "//pay?token=OTHER"
                }
            }
        };

        var output = DevFlowCommands.RedactCheckpointStatusForOutput(status);
        var json = CliJson.SerializeUntyped(output);
        using var document = JsonDocument.Parse(json);
        var route = document.RootElement
            .GetProperty("checkpoint")
            .GetProperty("route")
            .GetString();

        Assert.DoesNotContain("SECRET", json, StringComparison.Ordinal);
        Assert.DoesNotContain("OTHER", json, StringComparison.Ordinal);
        Assert.Equal("//pay?token=<redacted>&id=<redacted>", route);
        Assert.Equal("//pay?token=SECRET&id=42#receipt", status.Checkpoint.Route);
    }

    [Fact]
    public void Save_ReloadAndClear_UsesAtomicLocalState()
    {
        var store = new RouteCheckpointStore(_root);
        var saved = store.Save("agent", "session", "/orders?id=42", "Demo", "Windows", "Demo.csproj");

        var reloaded = new RouteCheckpointStore(_root).Get("agent", "session", connected: true);
        Assert.True(reloaded.HasCheckpoint);
        Assert.Equal(saved.Route, reloaded.Checkpoint!.Route);
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp", SearchOption.TopDirectoryOnly));

        Assert.True(store.Clear("agent", "session"));
        Assert.False(store.Get("agent", "session").HasCheckpoint);
    }

    [Fact]
    public void Get_CorruptFile_WarnsAndStartsClean()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "route-checkpoints.v1.json"), "{broken");

        var status = new RouteCheckpointStore(_root).Get("agent");

        Assert.False(status.HasCheckpoint);
        Assert.NotNull(status.Warning);
        Assert.NotEmpty(Directory.EnumerateFiles(_root, "*.corrupt-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Get_ExpiredCheckpoint_PrunesIt()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "route-checkpoints.v1.json"),
            """{"schema":1,"checkpoints":[{"schema":1,"agentId":"agent","route":"/old","savedUtc":"2000-01-01T00:00:00+00:00"}]}""");

        var status = new RouteCheckpointStore(_root).Get("agent");

        Assert.False(status.HasCheckpoint);
    }

    [Fact]
    public void Save_CapsEntries()
    {
        var store = new RouteCheckpointStore(_root, FrozenClock());
        for (var index = 0; index <= RouteCheckpointStore.MaxEntries; index++)
            store.Save("agent-" + index, null, "/route-" + index, null, null, null);

        Assert.False(store.Get("agent-0").HasCheckpoint);
        Assert.True(store.Get("agent-" + RouteCheckpointStore.MaxEntries).HasCheckpoint);
    }

    // Save_CapsEntries only triggers one trim. A second trim reads the persisted order, so if the
    // trim wrote its survivors newest-first the positional tie-break would then evict the
    // second-newest entry instead of the oldest.
    [Fact]
    public void Save_RepeatedTrims_KeepEvictingTheOldestEntry()
    {
        var store = new RouteCheckpointStore(_root, FrozenClock());
        var total = RouteCheckpointStore.MaxEntries * 2;
        for (var index = 0; index < total; index++)
            store.Save("agent-" + index, null, "/route-" + index, null, null, null);

        for (var index = 0; index < total - RouteCheckpointStore.MaxEntries; index++)
            Assert.False(store.Get("agent-" + index).HasCheckpoint);

        for (var index = total - RouteCheckpointStore.MaxEntries; index < total; index++)
            Assert.True(store.Get("agent-" + index).HasCheckpoint);
    }

    // The session-less lookup is the coordinator's fallback (RouteCheckpointCoordinator uses it
    // when the current session has no checkpoint of its own), and one agent legitimately keeps
    // checkpoints for several sessions. Two saves inside one clock tick share a SavedUtc, so the
    // fallback has to resolve the tie to the newer session rather than to whichever was stored first.
    [Fact]
    public void Get_WithoutSession_PrefersTheMostRecentlySavedSession()
    {
        var store = new RouteCheckpointStore(_root, FrozenClock());
        store.Save("agent", "older-session", "/older", null, null, null);
        store.Save("agent", "newer-session", "/newer", null, null, null);

        var fallback = store.Get("agent");

        Assert.True(fallback.HasCheckpoint);
        Assert.Equal("newer-session", fallback.Checkpoint?.SessionId);
        Assert.Equal("/newer", fallback.Checkpoint?.Route);
        Assert.True(store.Get("agent", "older-session").HasCheckpoint);
    }

    // These three tests exist for the case where several saves land on the same SavedUtc, which is
    // what actually happens on Windows: TimeProvider.System has ~15.6 ms granularity, so a burst of
    // saves ties and the store has to break the tie by insertion order. A frozen clock makes that
    // tie certain instead of leaving it to the host clock's resolution, which is finer on Linux and
    // would let these pass even with the tie-break reverted.
    private static TimeProvider FrozenClock() => new FrozenTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private sealed class FrozenTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Theory]
    [InlineData("/ok")]
    [InlineData("  /trimmed  ")]
    public void NormalizeRoute_ValidRoute_Accepts(string route)
        => Assert.True(RouteCheckpointStore.TryNormalizeRoute(route, out _, out _));

    [Fact]
    public void NormalizeRoute_ControlCharacter_Rejects()
        => Assert.False(RouteCheckpointStore.TryNormalizeRoute("/bad\nroute", out _, out _));

    [Fact]
    public void Coordinator_Clear_RemovesCurrentAndFallbackCheckpoints()
    {
        var store = new RouteCheckpointStore(_root);
        var registration = Registration("current");
        var agentId = RouteCheckpointCoordinator.StableAgentId(registration);
        store.Save(agentId, "older", "/older", "Demo", "Windows", registration.Project);
        store.Save(agentId, "current", "/current", "Demo", "Windows", registration.Project);
        var coordinator = new RouteCheckpointCoordinator(store);

        Assert.True(coordinator.Clear(registration));

        Assert.False(store.Get(agentId, "current").HasCheckpoint);
        Assert.False(store.Get(agentId).HasCheckpoint);
    }

    [Fact]
    public async Task Coordinator_Restore_RecordsResultAgainstFallbackCheckpointSession()
    {
        await using var agent = new CheckpointAgent("/older");
        var store = new RouteCheckpointStore(_root);
        var registration = Registration("current") with { Port = agent.Port };
        var agentId = RouteCheckpointCoordinator.StableAgentId(registration);
        store.Save(agentId, "older", "/older", "Demo", "Windows", registration.Project);
        var coordinator = new RouteCheckpointCoordinator(
            store,
            current => new AgentClient("127.0.0.1", current.Port)
            {
                AutoAcquireMutationLease = false
            });

        var result = await coordinator.RestoreAsync(registration);

        Assert.True(result.Ok, result.Warning);
        var fallback = store.Get(agentId, "older").Checkpoint;
        Assert.NotNull(fallback?.LastRestore);
        Assert.Equal("restored", fallback!.LastRestore!.Kind);
    }

    [Fact]
    public async Task Coordinator_Restore_PropagatesCallerCancellation()
    {
        await using var agent = new CheckpointAgent("/older");
        var store = new RouteCheckpointStore(_root);
        var registration = Registration("current") with { Port = agent.Port };
        var agentId = RouteCheckpointCoordinator.StableAgentId(registration);
        store.Save(agentId, "current", "/older", "Demo", "Windows", registration.Project);
        var coordinator = new RouteCheckpointCoordinator(
            store,
            current => new AgentClient("127.0.0.1", current.Port));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.RestoreAsync(registration, cts.Token));
    }

    [Fact]
    public void StableAgentId_UsesPackageIdentityForSameNamedProjects()
    {
        var first = Registration("same-session") with
        {
            Project = "App.csproj",
            PackageId = "com.example.first"
        };
        var second = first with { PackageId = "com.example.second" };

        Assert.NotEqual(
            RouteCheckpointCoordinator.StableAgentId(first),
            RouteCheckpointCoordinator.StableAgentId(second));
    }

    private static AgentRegistration Registration(string sessionId) => new()
    {
        Project = @"C:\src\Demo\Demo.csproj",
        Tfm = "net10.0-windows",
        Platform = "Windows",
        AppName = "Demo",
        SessionId = sessionId,
    };

    private sealed class CheckpointAgent : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private readonly string _route;

        public CheckpointAgent(string route)
        {
            _route = route;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _loop = AcceptLoopAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
                catch { break; }
                _ = HandleAsync(client);
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                var buffer = new byte[8_192];
                var read = await stream.ReadAsync(buffer, _cts.Token);
                if (read <= 0) return;
                var request = Encoding.UTF8.GetString(buffer, 0, read);
                var firstLine = request.Split("\r\n", 2)[0];
                var body = firstLine.StartsWith("GET ", StringComparison.Ordinal)
                    ? $$"""{"route":"{{_route}}"}"""
                    : """{"success":true}""";
                var payload = Encoding.UTF8.GetBytes(body);
                var headers = Encoding.UTF8.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, _cts.Token);
                await stream.WriteAsync(payload, _cts.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            try { await _loop; } catch { }
            _cts.Dispose();
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
