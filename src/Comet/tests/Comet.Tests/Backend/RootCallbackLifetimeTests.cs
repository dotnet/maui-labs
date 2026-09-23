using System;
using System.Threading;
using System.Threading.Tasks;
using Comet.Backend;
using Xunit;

namespace Comet.Tests.Backend;

public class RootCallbackLifetimeTests
{
    [Fact]
    public void QueuedCallbacks_AfterRootClose_DoNotTouchReplacementState()
    {
        var old = new RootCallbackLifetime();
        var current = new RootCallbackLifetime();
        var value = 0;
        Action staleFlush = () => old.Run(() => value = -1);
        Action staleNative = () => old.Run(() => value = -2);
        Assert.True(old.Run(() => value = 1));
        old.Dispose();
        Assert.True(current.Run(() => value = 2));
        staleFlush();
        staleNative();
        Assert.Equal(2, value);
        Assert.Throws<ObjectDisposedException>(old.ThrowIfClosed);
        old.Dispose();
        current.Dispose();
    }

    [Fact]
    public async Task Close_WaitsForInFlightFlush_BeforeDisposingRoot()
    {
        var lifetime = new RootCallbackLifetime();
        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var callback = Task.Run(() => lifetime.Run(() =>
        {
            entered.Set();
            Assert.True(finish.Wait(TimeSpan.FromSeconds(10)));
        }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var closeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var close = Task.Run(() =>
        {
            closeStarted.SetResult(true);
            lifetime.Dispose();
        });
        await closeStarted.Task;
        Assert.False(close.IsCompleted);
        finish.Set();
        await Task.WhenAll(callback, close);
        Assert.False(lifetime.Run(() => throw new InvalidOperationException("Stale callback")));
    }
}
