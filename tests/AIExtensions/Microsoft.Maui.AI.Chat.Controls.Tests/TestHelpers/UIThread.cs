using System.Collections.Concurrent;

namespace Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

/// <summary>
/// Runs a test on a single-threaded synchronization context so every continuation (engine callbacks, view updates)
/// runs on the test thread, like the UI thread in an app.
/// </summary>
internal static class UIThread
{
    public static void Run(Func<Task> test)
    {
        var previous = SynchronizationContext.Current;
        var context = new PumpingContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var task = test();
            task.ContinueWith(_ => context.Complete(), TaskScheduler.Default);
            context.Pump();
            task.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>Lets queued continuations run.</summary>
    public static async Task Drain(int turns = 20)
    {
        for (var i = 0; i < turns; i++)
            await Task.Yield();
    }

    /// <summary>Waits (on the test thread) until <paramref name="condition"/> holds.</summary>
    public static async Task Until(Func<bool> condition, int timeoutMs = 5000)
    {
        var started = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - started > timeoutMs)
                throw new TimeoutException("The condition was not met in time.");
            await Task.Delay(5);
        }
    }

    private sealed class PumpingContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                _queue.Add((d, state));
            }
            catch (InvalidOperationException)
            {
                // The test already finished; late continuations are dropped.
            }
        }

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void Pump()
        {
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                callback(state);
        }

        public void Complete() => _queue.CompleteAdding();
    }
}
