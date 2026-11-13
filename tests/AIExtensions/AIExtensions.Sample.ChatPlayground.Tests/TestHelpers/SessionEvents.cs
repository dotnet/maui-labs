using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

/// <summary>
/// Follows a session through its standard change notifications, <see cref="INotifyPropertyChanged"/> and
/// <see cref="INotifyCollectionChanged"/>, the way the chat's views do.
/// </summary>
internal static class SessionEvents
{
    /// <summary>Calls <paramref name="added"/> for each turn added to the session.</summary>
    public static IDisposable OnTurnAdded(this AgentContext session, Action<ConversationTurn> added) =>
        OnAdded<ConversationTurn>(session.Turns, added);

    /// <summary>Calls <paramref name="changed"/> with the session's status each time it changes.</summary>
    public static IDisposable OnStatusChanged(this AgentContext session, Action<ConversationStatus> changed)
    {
        void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AgentContext.Status))
                changed(session.Status);
        }

        session.PropertyChanged += OnPropertyChanged;
        return new Subscription(() => session.PropertyChanged -= OnPropertyChanged);
    }

    /// <summary>Calls <paramref name="added"/> for each request or response block added to a turn of the session.</summary>
    public static IDisposable OnBlockAdded(this AgentContext session, Action<ConversationTurn, ContentBlock> added)
    {
        var subscriptions = new List<IDisposable>();

        void Follow(ConversationTurn turn)
        {
            subscriptions.Add(OnAdded<ContentBlock>(turn.RequestBlocks, block => added(turn, block)));
            subscriptions.Add(OnAdded<ContentBlock>(turn.ResponseBlocks, block => added(turn, block)));
        }

        foreach (var turn in session.Turns)
            Follow(turn);
        subscriptions.Add(session.OnTurnAdded(Follow));
        return new Subscription(() => subscriptions.ForEach(subscription => subscription.Dispose()));
    }

    private static IDisposable OnAdded<T>(INotifyCollectionChanged items, Action<T> added)
    {
        void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (T item in e.NewItems!)
                    added(item);
            }
        }

        items.CollectionChanged += OnCollectionChanged;
        return new Subscription(() => items.CollectionChanged -= OnCollectionChanged);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
