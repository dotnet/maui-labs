namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Moves callbacks from platform services and the engine onto the thread that owns a view. Speech recognizers
/// (and an app that calls the engine off the UI thread) raise them on background threads.
/// </summary>
internal static class DispatchingExtensions
{
    /// <summary>
    /// Gets the dispatcher of <paramref name="owner"/>'s thread when the caller is on another thread, or
    /// <see langword="null"/> on that thread and when there is no dispatcher at all (as in unit tests).
    /// </summary>
    public static IDispatcher? GetDispatcherIfRequired(this BindableObject owner)
    {
        IDispatcher dispatcher;
        try
        {
            // The dispatcher of the thread that created the view, or else the window's or the app's.
            dispatcher = owner.Dispatcher;
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return dispatcher.IsDispatchRequired ? dispatcher : null;
    }

    /// <summary>Runs <paramref name="action"/> now on <paramref name="owner"/>'s thread, or queues it there.</summary>
    public static void RunOnOwnerThread(this BindableObject owner, Action action)
    {
        if (owner.GetDispatcherIfRequired() is { } dispatcher)
            dispatcher.Dispatch(action);
        else
            action();
    }
}
