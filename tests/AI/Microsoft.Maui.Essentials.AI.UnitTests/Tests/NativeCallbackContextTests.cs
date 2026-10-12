using System.Diagnostics;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class NativeCallbackContextTests
{
    [Fact]
    public async Task RunAsync_RestoresRequestActivityAcrossAwaitsWithoutLeakingIntoCallbackThread()
    {
        using var request = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        var context = new NativeCallbackContext();
        Task nativeCallback;
        using (ExecutionContext.SuppressFlow())
        {
            nativeCallback = Task.Run(async () =>
            {
                Assert.Null(Activity.Current);
                var pending = context.RunAsync(async () =>
                {
                    Assert.Same(request, Activity.Current);
                    await Task.Yield();
                    Assert.Equal(request.TraceId, Activity.Current!.TraceId);
                    Assert.Equal(request.SpanId, Activity.Current.SpanId);
                });
                Assert.Null(Activity.Current);
                await pending;
                Assert.Null(Activity.Current);
            });
        }
        await nativeCallback;
        Assert.Same(request, Activity.Current);
    }

    [Fact]
    public async Task RunAsync_ConcurrentRequestsAndRepeatedCallbacksKeepTheirOwnContext()
    {
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            using var request = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
            var context = new NativeCallbackContext();
            Task[] callbacks;
            using (ExecutionContext.SuppressFlow())
            {
                callbacks = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
                {
                    Assert.Null(Activity.Current);
                    await context.RunAsync(async () =>
                    {
                        Assert.Same(request, Activity.Current);
                        using var tool = new Activity("tool").Start();
                        Assert.Equal(request.TraceId, tool.TraceId);
                        Assert.Equal(request.SpanId, tool.ParentSpanId);
                        await Task.Yield();
                        Assert.Same(tool, Activity.Current);
                    });
                    Assert.Null(Activity.Current);
                })).ToArray();
            }
            await Task.WhenAll(callbacks);
            Assert.Same(request, Activity.Current);
        })));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_PropagatesFailureAndCancellationWithoutLeakingContext(bool canceled)
    {
        using var request = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        var context = new NativeCallbackContext();
        Task nativeCallback;
        using (ExecutionContext.SuppressFlow())
        {
            nativeCallback = Task.Run(async () =>
            {
                var failure = await Record.ExceptionAsync(() => context.RunAsync(async () =>
                {
                    await Task.Yield();
                    Assert.Same(request, Activity.Current);
                    if (canceled)
                        throw new OperationCanceledException();
                    throw new InvalidOperationException("tool failure");
                }));
                if (canceled)
                    Assert.IsType<OperationCanceledException>(failure);
                else
                    Assert.IsType<InvalidOperationException>(failure);
                Assert.Null(Activity.Current);
            });
        }
        await nativeCallback;
        Assert.Same(request, Activity.Current);
    }

    [Fact]
    public async Task RunAsync_WithoutRequestActivityDoesNotInventCorrelation()
    {
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            var context = new NativeCallbackContext();
            await context.RunAsync(async () =>
            {
                Assert.Null(Activity.Current);
                await Task.Yield();
                Assert.Null(Activity.Current);
            });
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Fact]
    public async Task RunAsync_SuppressedCaptureRunsCallbackWithoutRestoringRequest()
    {
        using var request = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        NativeCallbackContext context;
        Task nativeCallback;
        using (ExecutionContext.SuppressFlow())
        {
            context = new NativeCallbackContext();
            nativeCallback = Task.Run(() => context.RunAsync(() =>
            {
                Assert.Null(Activity.Current);
                return Task.CompletedTask;
            }));
        }
        await nativeCallback;
        Assert.Same(request, Activity.Current);
    }
}
