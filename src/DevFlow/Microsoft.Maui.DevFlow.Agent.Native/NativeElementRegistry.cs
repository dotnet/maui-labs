using System.Runtime.CompilerServices;
#if IOS || MACCATALYST || MACOS
using ObjCRuntime;
#endif

namespace Microsoft.Maui.DevFlow.Agent.Native;

/// <summary>
/// Assigns stable DevFlow element ids to live native views and resolves them back.
/// </summary>
/// <remarks>
/// <para>
/// Ids must survive between requests — a client fetches the tree, then taps by id in a later
/// request. Ids are therefore keyed off the view instance itself rather than its position in the
/// tree, so a re-walk that finds the same view hands back the same id.
/// </para>
/// <para>
/// Recently walked views are held <em>strongly</em>, bounded by <see cref="MaxTrackedElements"/>
/// and evicted least-recently-seen first. Weak holds are not viable on the Apple backends: the
/// managed peer for a framework type such as <c>UILabel</c> is recreated on each marshal and is
/// collectable while the native view lives on, so a weakly held id would evaporate mid-session and
/// break the contract above. Holding the view also pins its native address, which is what makes the
/// <c>objc:{handle}</c> key sound — an address cannot be recycled for an unrelated view while we
/// still map a key to it, so an id can never silently retarget. Eviction drops the view and its key
/// together, leaving no stale key behind.
/// </para>
/// </remarks>
internal sealed class NativeElementRegistry
{
    /// <summary>
    /// Soft cap on tracked elements. Exceeded only while a single walk legitimately sees more, since
    /// views from the current and previous walk are never evicted.
    /// </summary>
    private const int MaxTrackedElements = 512;

    private sealed class Entry
    {
        public required object View { get; set; }
        public required long LastSeenWalk { get; set; }
        public string? ParentId { get; set; }
    }

    private readonly ConditionalWeakTable<object, string> _idsByView = new();
    private readonly Dictionary<string, string> _idsByStableKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _entriesById = new(StringComparer.Ordinal);
    private readonly Func<object, string?> _stableKeySelector;
    private readonly object _gate = new();
    private long _walk;
    private int _next;

    public NativeElementRegistry()
        : this(GetStableKey)
    {
    }

    /// <summary>Test seam: supplies the stable key the ObjC backends derive from a native handle.</summary>
    internal NativeElementRegistry(Func<object, string?> stableKeySelector)
        => _stableKeySelector = stableKeySelector;

    /// <summary>
    /// Opens a new walk generation and evicts stale elements. Called at the start of every tree walk
    /// so the bookkeeping does not grow without bound across navigations.
    /// </summary>
    public void BeginWalk()
    {
        lock (_gate)
        {
            _walk++;
            if (_entriesById.Count <= MaxTrackedElements) return;

            Evict();
        }
    }

    /// <summary>
    /// Returns the id for <paramref name="view"/>, allocating one on first sight.
    /// </summary>
    public string Register(object view, string? parentId)
    {
        lock (_gate)
        {
            var stableKey = _stableKeySelector(view);
            string id;

            if (stableKey != null)
            {
                if (!_idsByStableKey.TryGetValue(stableKey, out id!))
                {
                    id = $"n{++_next}";
                    _idsByStableKey[stableKey] = id;
                }
            }
            else if (!_idsByView.TryGetValue(view, out id!))
            {
                id = $"n{++_next}";
                _idsByView.Add(view, id);
            }

            if (_entriesById.TryGetValue(id, out var entry))
            {
                entry.View = view;
                entry.LastSeenWalk = _walk;
                entry.ParentId = parentId;
            }
            else
            {
                _entriesById[id] = new Entry { View = view, LastSeenWalk = _walk, ParentId = parentId };
            }

            return id;
        }
    }

    /// <summary>Resolves an id back to a tracked view, or <c>null</c> once it has been evicted.</summary>
    public object? Resolve(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        lock (_gate)
        {
            return _entriesById.TryGetValue(id, out var entry) ? entry.View : null;
        }
    }

    /// <summary>Returns the parent id recorded for <paramref name="id"/> during the last walk.</summary>
    public string? ParentOf(string id)
    {
        lock (_gate)
        {
            return _entriesById.TryGetValue(id, out var entry) ? entry.ParentId : null;
        }
    }

    /// <summary>
    /// Trims back to <see cref="MaxTrackedElements"/>, oldest first. Views seen in the current or
    /// previous walk are exempt: the previous walk is the tree the client most likely holds ids
    /// from, and the current one is being built right now. Each evicted id drops its stable key too,
    /// so a native address is only released back to the runtime once no key still names it.
    /// </summary>
    private void Evict()
    {
        var protectedFrom = _walk - 1;

        var stale = _entriesById
            .Where(pair => pair.Value.LastSeenWalk < protectedFrom)
            .OrderBy(pair => pair.Value.LastSeenWalk)
            .Take(_entriesById.Count - MaxTrackedElements)
            .Select(pair => pair.Key)
            .ToList();

        if (stale.Count == 0) return;

        foreach (var id in stale)
            _entriesById.Remove(id);

        var orphanedKeys = _idsByStableKey
            .Where(pair => !_entriesById.ContainsKey(pair.Value))
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in orphanedKeys)
            _idsByStableKey.Remove(key);
    }

    private static string? GetStableKey(object view)
    {
#if IOS || MACCATALYST || MACOS
        if (view is INativeObject native && native.Handle != IntPtr.Zero)
            return $"objc:{((IntPtr)native.Handle).ToInt64():x}";
#endif
        return null;
    }
}
