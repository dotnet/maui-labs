using BespokeCollections.Core;
using Xunit;

namespace BespokeCollections.Tests;

public sealed class AsyncWorkGateTests
{
    [Fact]
    public async Task Run_AsyncWork_KeepsSlotUntilNativeCompletionAndDrainsAfterDispose()
    {
        using var gate = new AsyncWorkGate(2);
        using var waiting = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        async Task<int> Work(int value)
        {
            if (Interlocked.Increment(ref count) == 2) started.TrySetResult();
            await finish.Task;
            return value;
        }
        var first = gate.RunAsync<int>(() => Work(1), CancellationToken.None);
        var second = gate.RunAsync<int>(() => Work(2), CancellationToken.None);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var cancelled = gate.RunAsync<int>(() => Work(3), waiting.Token);
            Assert.Equal(2, Volatile.Read(ref count));
            waiting.Cancel();
            gate.Dispose();
            finish.TrySetResult();
            Assert.Equal(new[] { 1, 2 }, await Task.WhenAll(first, second));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
            Assert.Equal(2, Volatile.Read(ref count));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => gate.RunAsync<int>(() => Work(4), CancellationToken.None));
        }
        finally { finish.TrySetResult(); }
    }

    [Fact]
    public async Task Run_DisposeDuringActiveWork_DrainsWithoutDisposingLiveSemaphore()
    {
        using var gate = new AsyncWorkGate(2);
        using var started = new CountdownEvent(2);
        using var finish = new ManualResetEventSlim();
        using var waiting = new CancellationTokenSource();
        var first = gate.RunAsync(() => { started.Signal(); finish.Wait(); return 1; }, CancellationToken.None);
        var second = gate.RunAsync(() => { started.Signal(); finish.Wait(); return 2; }, CancellationToken.None);
        try
        {
            Assert.True(await Task.Run(() => started.Wait(TimeSpan.FromSeconds(5))), "The two permitted decoders must start.");
            var cancelled = gate.RunAsync(() => 3, waiting.Token);
            waiting.Cancel();
            gate.Dispose();
            finish.Set();
            Assert.Equal(new[] { 1, 2 }, await Task.WhenAll(first, second));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => gate.RunAsync(() => 4, CancellationToken.None));
        }
        finally { finish.Set(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void Create_UnboundedOrEmptyConcurrency_Throws(int concurrency) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AsyncWorkGate(concurrency));
}
