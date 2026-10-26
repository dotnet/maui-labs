using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace AndroidX.Compose;

/// <summary>
/// A managed <see cref="IDictionary{TKey, TValue}"/> wrapper that
/// participates in Compose's recomposition system without needing a
/// Kotlin <c>SnapshotStateMap</c> binding. C# parity for Kotlin's
/// <c>mutableStateMapOf&lt;K, V&gt;()</c>: every read touches an
/// internal <see cref="MutableNumberState{Int32}"/> version counter
/// so the composition scope subscribes; every mutation increments
/// that counter, triggering a recomposition.
///
/// <code>
/// var prefs = Remember(() => new MutableStateMap&lt;string, bool&gt;());
/// // In a Render body:
/// new Text(prefs.TryGetValue("dark", out var v) &amp;&amp; v ? "Dark" : "Light");
/// // Anywhere — triggers recomposition:
/// prefs["dark"] = true;
/// </code>
///
/// Reads MUST happen during composition for the subscription to take
/// effect. Mutations from a UI button handler (which run outside
/// composition) are fine — the next recomposition will see the new
/// version. Not thread-safe; intended for UI/composition-thread use.
/// </summary>
/// <remarks>
/// This is a *managed* observable map, not a true Kotlin
/// <c>SnapshotStateMap</c>. It tracks change-vs-no-change at the
/// map level (single tick counter), not per-key. Snapshot isolation,
/// custom <c>SnapshotMutationPolicy</c>, and per-read fine-grained
/// dependency tracking are not provided. For UI maps where "any
/// mutation triggers recomposition of any reader" is the desired
/// behaviour — which covers ~all typical Compose UI map patterns —
/// that's exactly the contract you want.
/// </remarks>
public sealed class MutableStateMap<TKey, TValue>
    : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    readonly Dictionary<TKey, TValue> _items;
    readonly MutableNumberState<int> _tick = new(0);

    /// <summary>Creates an empty observable map.</summary>
    public MutableStateMap() => _items = new Dictionary<TKey, TValue>();

    /// <summary>Creates an observable map pre-populated with <paramref name="initial"/>.</summary>
    public MutableStateMap(IEnumerable<KeyValuePair<TKey, TValue>> initial)
    {
        _items = new Dictionary<TKey, TValue>();
        foreach (var kv in initial) _items[kv.Key] = kv.Value;
    }

    void Track() { _ = _tick.Value; }
    void Bump() => _tick.Value++;

    /// <inheritdoc/>
    public int Count { get { Track(); return _items.Count; } }

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public TValue this[TKey key]
    {
        get { Track(); return _items[key]; }
        set { _items[key] = value; Bump(); }
    }

    /// <inheritdoc/>
    public ICollection<TKey> Keys { get { Track(); return _items.Keys; } }
    /// <inheritdoc/>
    public ICollection<TValue> Values { get { Track(); return _items.Values; } }

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys { get { Track(); return _items.Keys; } }
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values { get { Track(); return _items.Values; } }

    /// <inheritdoc/>
    public void Add(TKey key, TValue value) { _items.Add(key, value); Bump(); }

    /// <inheritdoc/>
    public void Add(KeyValuePair<TKey, TValue> item) { _items.Add(item.Key, item.Value); Bump(); }

    /// <inheritdoc/>
    public bool ContainsKey(TKey key) { Track(); return _items.ContainsKey(key); }

    /// <inheritdoc/>
    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        Track();
        return _items.TryGetValue(item.Key, out var v)
            && EqualityComparer<TValue>.Default.Equals(v, item.Value);
    }

    /// <inheritdoc/>
    public bool Remove(TKey key)
    {
        if (_items.Remove(key)) { Bump(); return true; }
        return false;
    }

    /// <inheritdoc/>
    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        if (_items.TryGetValue(item.Key, out var v)
            && EqualityComparer<TValue>.Default.Equals(v, item.Value)
            && _items.Remove(item.Key))
        {
            Bump();
            return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Bump();
    }

    /// <inheritdoc/>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        Track();
        return _items.TryGetValue(key, out value);
    }

    /// <inheritdoc/>
    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        Track();
        ((ICollection<KeyValuePair<TKey, TValue>>)_items).CopyTo(array, arrayIndex);
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        Track();
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
