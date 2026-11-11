using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

/// <summary>A dispatcher whose work runs only when the test says so, to observe coalescing.</summary>
internal sealed class TestDispatcher : IDispatcher, IDispatcherProvider, IDisposable
{
    private readonly List<Action> _pending = [];

    private TestDispatcher()
    {
    }

    public bool IsDispatchRequired => false;

    public int PendingCount => _pending.Count;

    public static TestDispatcher Install()
    {
        var dispatcher = new TestDispatcher();
        DispatcherProvider.SetCurrent(dispatcher);
        return dispatcher;
    }

    public bool Dispatch(Action action)
    {
        _pending.Add(action);
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
    {
        _pending.Add(action);
        return true;
    }

    public IDispatcherTimer CreateTimer() => throw new NotSupportedException();

    public IDispatcher? GetForCurrentThread() => this;

    public void RunPending()
    {
        var actions = _pending.ToArray();
        _pending.Clear();
        foreach (var action in actions)
            action();
    }

    public void Dispose() => ImmediateDispatcher.Install();
}

/// <summary>
/// Runs dispatched work inline. Bindings need a dispatcher; with this one the message list flushes synchronously.
/// </summary>
internal sealed class ImmediateDispatcher : IDispatcher, IDispatcherProvider
{
    private static readonly ImmediateDispatcher Instance = new();

    public bool IsDispatchRequired => false;

    public static void Install() => DispatcherProvider.SetCurrent(Instance);

    public bool Dispatch(Action action)
    {
        action();
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
    {
        action();
        return true;
    }

    public IDispatcherTimer CreateTimer() => throw new NotSupportedException();

    public IDispatcher? GetForCurrentThread() => this;
}
