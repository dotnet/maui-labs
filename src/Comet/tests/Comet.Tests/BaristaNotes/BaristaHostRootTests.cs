using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CometBaristaNotes.Services;
using Xunit;

namespace Comet.Tests.BaristaLifecycle;

public class BaristaHostRootTests
{
    [Fact]
    public async Task Teardown_WaitsForOwnedWork_ThenDetachesDisposesAndShutsDownOnce()
    {
        var events = new List<string>();
        var finishWork = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRoot = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var root = new OwnedRoot(async () =>
        {
            events.Add("root");
            disposeEntered.SetResult(true);
            await finishRoot.Task;
        });
        var host = new BaristaHostRoot<OwnedRoot>(
            () => events.Add("native"),
            () => { events.Add("platform"); return Task.CompletedTask; },
            () => finishWork.Task);
        Assert.Same(root, host.Create(() => root));
        var first = host.DisposeAsync().AsTask();
        Assert.Same(first, host.DisposeAsync().AsTask());
        Assert.Empty(events);
        Assert.Throws<ObjectDisposedException>(() => host.Create(() => root));
        finishWork.SetResult(true);
        await disposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { "native", "root" }, events);
        Assert.False(first.IsCompleted);
        finishRoot.SetResult(true);
        await first;
        Assert.Equal(new[] { "native", "root", "platform" }, events);
        Assert.Null(host.Root);
        Assert.Equal(1, root.DisposeCount);
    }

    [Theory]
    [InlineData("work")]
    [InlineData("native")]
    [InlineData("root")]
    public async Task TeardownFailure_StillReleasesAllOwnedResources_AndPropagates(string stage)
    {
        var failure = new IOException(stage);
        var events = new List<string>();
        var root = new OwnedRoot(() =>
        {
            events.Add("root");
            return stage == "root" ? Task.FromException(failure) : Task.CompletedTask;
        });
        var host = new BaristaHostRoot<OwnedRoot>(
            () => { events.Add("native"); if (stage == "native") throw failure; },
            () => { events.Add("platform"); return Task.CompletedTask; },
            () => stage == "work" ? Task.FromException(failure) : Task.CompletedTask);
        host.Create(() => root);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => host.DisposeAsync().AsTask()));
        Assert.Equal(new[] { "native", "root", "platform" }, events);
        Assert.Equal(1, root.DisposeCount);
    }

    sealed class OwnedRoot(Func<Task> release) : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return new ValueTask(release());
        }
    }
}
