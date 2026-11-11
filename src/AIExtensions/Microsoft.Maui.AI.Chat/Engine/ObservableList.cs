// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// The engine's observable list of turns or blocks, exposed read-only through a
/// <see cref="ReadOnlyObservableCollection{T}"/>. Replacing its items raises one reset.
/// </summary>
/// <remarks>
/// The read-only wrapper is the list's only subscriber, so the list never rejects a change made by an app
/// handler of its notification; <see cref="AgentContext"/> decides which changes a handler can make.
/// </remarks>
internal sealed class ObservableList<T> : ObservableCollection<T>
{
    private static readonly PropertyChangedEventArgs s_countChanged = new(nameof(Count));
    private static readonly PropertyChangedEventArgs s_indexerChanged = new("Item[]");
    private static readonly NotifyCollectionChangedEventArgs s_reset = new(NotifyCollectionChangedAction.Reset);

    /// <summary>Replaces every item and raises a single <see cref="NotifyCollectionChangedAction.Reset"/>.</summary>
    internal void Reset(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        OnPropertyChanged(s_countChanged);
        OnPropertyChanged(s_indexerChanged);
        OnCollectionChanged(s_reset);
    }
}
