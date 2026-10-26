using Microsoft.Maui.Cli.DevFlow.Broker;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The broker used to hold a per-agent mutex across the whole proxied Inspector request, so one
/// long call (a flow replay runs inline for up to two minutes) serialized every other request —
/// reads, screenshots, heartbeats — and stopped concurrent mutations from ever reaching
/// InspectorServer.RouteAsync, making its replay-in-progress 409 unreachable in broker-hosted
/// mode. Requests now take the shared side; only lifetime changes take the exclusive side.
/// </summary>
public class AgentLifetimeGateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task SharedHolders_RunConcurrently()
    {
        var gate = new AgentLifetimeGate();
        const int holders = 8;
        using var allEntered = new SemaphoreSlim(0, holders);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var running = Enumerable.Range(0, holders).Select(async _ =>
        {
            await using var scope = await gate.EnterSharedAsync(CancellationToken.None);
            allEntered.Release();
            await release.Task;
        }).ToArray();

        // All of them must be inside the gate at once; a mutex would deadlock here.
        for (var i = 0; i < holders; i++)
            Assert.True(await allEntered.WaitAsync(Timeout));

        release.TrySetResult();
        await Task.WhenAll(running).WaitAsync(Timeout);
    }

    [Fact]
    public async Task Exclusive_WaitsForEveryInFlightSharedHolder()
    {
        var gate = new AgentLifetimeGate();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exclusiveTaken = false;

        var shared = Task.Run(async () =>
        {
            await using var scope = await gate.EnterSharedAsync(CancellationToken.None);
            firstEntered.TrySetResult();
            await releaseFirst.Task;
            Assert.False(Volatile.Read(ref exclusiveTaken)); // no replacement mid-request
        });

        await firstEntered.Task.WaitAsync(Timeout);

        var exclusive = Task.Run(async () =>
        {
            await using var scope = await gate.EnterExclusiveAsync();
            Volatile.Write(ref exclusiveTaken, true);
        });

        await Task.Delay(100);
        Assert.False(exclusive.IsCompleted);

        releaseFirst.TrySetResult();
        await Task.WhenAll(shared, exclusive).WaitAsync(Timeout);
        Assert.True(exclusiveTaken);
    }

    [Fact]
    public async Task Shared_WaitsWhileExclusiveIsHeld()
    {
        var gate = new AgentLifetimeGate();
        var exclusive = await gate.EnterExclusiveAsync();

        var shared = Task.Run(async () =>
        {
            await using var scope = await gate.EnterSharedAsync(CancellationToken.None);
        });

        await Task.Delay(100);
        Assert.False(shared.IsCompleted);

        await exclusive.DisposeAsync();
        await shared.WaitAsync(Timeout);
    }

    [Fact]
    public async Task ExclusiveHolders_AreMutuallyExclusive()
    {
        var gate = new AgentLifetimeGate();
        var first = await gate.EnterExclusiveAsync();

        var second = Task.Run(async () =>
        {
            await using var scope = await gate.EnterExclusiveAsync();
        });

        await Task.Delay(100);
        Assert.False(second.IsCompleted);

        await first.DisposeAsync();
        await second.WaitAsync(Timeout);
    }

    [Fact]
    public async Task LastSharedHolderOut_ReleasesTheGate()
    {
        var gate = new AgentLifetimeGate();

        // Churn the shared side repeatedly; a leaked count would strand the exclusive side.
        for (var i = 0; i < 20; i++)
        {
            var scopes = new List<IAsyncDisposable>();
            for (var j = 0; j < 4; j++)
                scopes.Add(await gate.EnterSharedAsync(CancellationToken.None));
            foreach (var scope in scopes)
                await scope.DisposeAsync();
        }

        var exclusive = await gate.EnterExclusiveAsync().WaitAsync(Timeout);
        await exclusive.DisposeAsync();
    }

    [Fact]
    public async Task DisposingASharedScopeTwice_DoesNotCorruptTheCount()
    {
        var gate = new AgentLifetimeGate();
        var keepOpen = await gate.EnterSharedAsync(CancellationToken.None);
        var doubled = await gate.EnterSharedAsync(CancellationToken.None);

        await doubled.DisposeAsync();
        await doubled.DisposeAsync(); // must be a no-op, not a second decrement

        // keepOpen is still in flight, so the exclusive side must remain blocked.
        var exclusive = Task.Run(async () =>
        {
            await using var scope = await gate.EnterExclusiveAsync();
        });
        await Task.Delay(100);
        Assert.False(exclusive.IsCompleted);

        await keepOpen.DisposeAsync();
        await exclusive.WaitAsync(Timeout);
    }

    [Fact]
    public async Task CancellingAWaitingSharedHolder_LeavesTheGateUsable()
    {
        var gate = new AgentLifetimeGate();
        var exclusive = await gate.EnterExclusiveAsync();

        using var cts = new CancellationTokenSource();
        var cancelled = gate.EnterSharedAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        await exclusive.DisposeAsync();

        await using var shared = await gate.EnterSharedAsync(CancellationToken.None).WaitAsync(Timeout);
    }
}
