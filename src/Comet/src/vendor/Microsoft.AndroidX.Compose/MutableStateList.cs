using System.Collections;

namespace AndroidX.Compose;

/// <summary>
/// A managed <see cref="IList{T}"/> wrapper that participates in
/// Compose's recomposition system without needing a Kotlin
/// <c>SnapshotStateList</c> binding. C# parity for Kotlin's
/// <c>mutableStateListOf&lt;T&gt;()</c>: every read (<see cref="Count"/>,
/// the indexer, <see cref="GetEnumerator"/>) touches an internal
/// <see cref="MutableNumberState{Int32}"/> version counter so the
/// composition scope subscribes; every mutation increments that
/// counter, triggering a recomposition.
///
/// <code>
/// var messages = Remember(() => new MutableStateList&lt;Message&gt;());
/// // In a Render body:
/// foreach (var m in messages)
///     new Text(m.Content);
/// // Anywhere — triggers recomposition:
/// messages.Add(new Message(...));
/// </code>
///
/// Reads MUST happen during composition for the subscription to take
/// effect. Mutations from a UI button handler (which run outside
/// composition) are fine — the next recomposition will see the new
/// version. Not thread-safe; intended for UI/composition-thread use.
/// </summary>
/// <remarks>
/// This is a *managed* observable list, not a true Kotlin
/// <c>SnapshotStateList</c>. It tracks change-vs-no-change at the
/// list level (single tick counter), not per-index. Snapshot
/// isolation, custom <c>SnapshotMutationPolicy</c>, and per-read
/// fine-grained dependency tracking are not provided. For UI lists
/// where "any mutation triggers recomposition of any reader" is the
/// desired behaviour — which covers ~all typical Compose UI list
/// patterns — that's exactly the contract you want.
/// </remarks>
public sealed class MutableStateList<T> : IList<T>, IReadOnlyList<T>
{
    readonly List<T> _items;
    readonly MutableNumberState<int> _tick = new(0);

    /// <summary>Creates an empty observable list.</summary>
    public MutableStateList() => _items = new List<T>();

    /// <summary>Creates an observable list pre-populated with <paramref name="initial"/>.</summary>
    public MutableStateList(IEnumerable<T> initial) => _items = new List<T>(initial);

    /// <summary>Creates an observable list pre-populated with <paramref name="elements"/>.</summary>
    public MutableStateList(params T[] elements) => _items = elements is null ? new List<T>() : new List<T>(elements);

    // Subscribe the current composition scope to mutations.
    void Track() { _ = _tick.Value; }
    // Notify all subscribed scopes that the list has changed.
    void Bump() => _tick.Value++;

    /// <inheritdoc/>
    public int Count
    {
        get { Track(); return _items.Count; }
    }

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public T this[int index]
    {
        get { Track(); return _items[index]; }
        set { _items[index] = value; Bump(); }
    }

    /// <inheritdoc/>
    public void Add(T item) { _items.Add(item); Bump(); }

    /// <inheritdoc/>
    public void Insert(int index, T item) { _items.Insert(index, item); Bump(); }

    /// <inheritdoc/>
    public bool Remove(T item)
    {
        if (_items.Remove(item)) { Bump(); return true; }
        return false;
    }

    /// <inheritdoc/>
    public void RemoveAt(int index) { _items.RemoveAt(index); Bump(); }

    /// <inheritdoc/>
    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Bump();
    }

    /// <inheritdoc/>
    public bool Contains(T item) { Track(); return _items.Contains(item); }
    /// <inheritdoc/>
    public int IndexOf(T item)  { Track(); return _items.IndexOf(item); }
    /// <inheritdoc/>
    public void CopyTo(T[] array, int arrayIndex) { Track(); _items.CopyTo(array, arrayIndex); }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() { Track(); return _items.GetEnumerator(); }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
