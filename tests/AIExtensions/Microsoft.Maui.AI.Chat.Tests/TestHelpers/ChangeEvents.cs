// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Microsoft.Maui.AI.Chat.Tests.TestHelpers;

/// <summary>
/// Observes a conversation through its standard change events, the way a bound view does. Each helper only
/// filters the events; it raises nothing of its own, so the order it reports is the order the engine raises.
/// </summary>
internal static class ChangeEvents
{
    /// <summary>
    /// Calls <paramref name="handler"/> with <see cref="AgentContext.Status"/> each time the context raises
    /// <see cref="INotifyPropertyChanged.PropertyChanged"/> for it.
    /// </summary>
    /// <returns>A subscription that stops the calls when disposed.</returns>
    internal static IDisposable OnStatusChanged(this AgentContext context, Action<ConversationStatus> handler)
    {
        PropertyChangedEventHandler changed = (_, e) =>
        {
            if (e.PropertyName == nameof(AgentContext.Status))
                handler(context.Status);
        };
        context.PropertyChanged += changed;
        return new Subscription(() => context.PropertyChanged -= changed);
    }

    /// <summary>
    /// Calls <paramref name="handler"/> for each turn <see cref="AgentContext.Turns"/> adds. A reset, which a
    /// restore raises, reports every turn the list holds after it.
    /// </summary>
    /// <returns>A subscription that stops the calls when disposed.</returns>
    internal static IDisposable OnTurnAdded(this AgentContext context, Action<ConversationTurn> handler)
    {
        NotifyCollectionChangedEventHandler changed = (_, e) =>
        {
            var added = e.Action switch
            {
                NotifyCollectionChangedAction.Add => e.NewItems!.Cast<ConversationTurn>().ToArray(),
                NotifyCollectionChangedAction.Reset => context.Turns.ToArray(),
                _ => [],
            };
            foreach (var turn in added)
                handler(turn);
        };
        Observe(context.Turns).CollectionChanged += changed;
        return new Subscription(() => Observe(context.Turns).CollectionChanged -= changed);
    }

    /// <summary>
    /// Calls <paramref name="handler"/> for each block added to the request or response blocks of a turn the
    /// context holds now or adds later. The blocks a turn already holds when it is added are not reported.
    /// </summary>
    /// <returns>A subscription that stops the calls when disposed.</returns>
    internal static IDisposable OnBlockAdded(this AgentContext context, Action<ConversationTurn, ContentBlock> handler)
    {
        return context.OnEachTurn(turn =>
        {
            NotifyCollectionChangedEventHandler request = (_, e) => ReportAdded(turn, e);
            NotifyCollectionChangedEventHandler response = (_, e) => ReportAdded(turn, e);
            Observe(turn.RequestBlocks).CollectionChanged += request;
            Observe(turn.ResponseBlocks).CollectionChanged += response;
            return () =>
            {
                Observe(turn.RequestBlocks).CollectionChanged -= request;
                Observe(turn.ResponseBlocks).CollectionChanged -= response;
            };
        });

        void ReportAdded(ConversationTurn turn, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add)
                return;

            foreach (ContentBlock block in e.NewItems!)
                handler(turn, block);
        }
    }

    /// <summary>
    /// Calls <paramref name="handler"/> each time the <see cref="ConversationTurn.ResponseBlocks"/> of a turn the
    /// context holds now or adds later raise a reset, which <see cref="AgentContext.RetryAsync"/> does.
    /// </summary>
    /// <returns>A subscription that stops the calls when disposed.</returns>
    internal static IDisposable OnResponseBlocksReset(this AgentContext context, Action<ConversationTurn> handler)
        => context.OnEachTurn(turn =>
        {
            NotifyCollectionChangedEventHandler changed = (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset)
                    handler(turn);
            };
            Observe(turn.ResponseBlocks).CollectionChanged += changed;
            return () => Observe(turn.ResponseBlocks).CollectionChanged -= changed;
        });

    /// <summary>
    /// Records the name of each property <paramref name="source"/> raises, in order. An empty name means every
    /// property changed.
    /// </summary>
    internal static List<string> RecordPropertyChanges(this INotifyPropertyChanged source)
    {
        var names = new List<string>();
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName ?? string.Empty);
        return names;
    }

    internal static INotifyCollectionChanged Observe<T>(ReadOnlyObservableCollection<T> collection) => collection;

    /// <summary>
    /// Runs <paramref name="watch"/> once for each turn the context holds now or adds later, and undoes every
    /// watch when the returned subscription is disposed.
    /// </summary>
    private static IDisposable OnEachTurn(this AgentContext context, Func<ConversationTurn, Action> watch)
    {
        var unwatch = new Dictionary<ConversationTurn, Action>(ReferenceEqualityComparer.Instance);
        void Watch(ConversationTurn turn)
        {
            if (!unwatch.ContainsKey(turn))
                unwatch.Add(turn, watch(turn));
        }

        foreach (var turn in context.Turns)
            Watch(turn);

        var turnAdded = context.OnTurnAdded(Watch);
        return new Subscription(() =>
        {
            turnAdded.Dispose();
            foreach (var undo in unwatch.Values)
                undo();
            unwatch.Clear();
        });
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            _dispose?.Invoke();
            _dispose = null;
        }
    }
}
