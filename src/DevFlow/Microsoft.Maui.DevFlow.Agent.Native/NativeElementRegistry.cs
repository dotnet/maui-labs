using System.Runtime.CompilerServices;
#if IOS || MACCATALYST || MACOS
using ObjCRuntime;
#endif

namespace Microsoft.Maui.DevFlow.Agent.Native;

/// <summary>
/// Assigns stable DevFlow element ids to live native views and resolves them back.
/// </summary>
/// <remarks>
/// Ids must survive between requests — a client fetches the tree, then taps by id in a later
/// request. Ids are therefore keyed off the view instance itself rather than its position in the
/// tree, so a re-walk that finds the same view hands back the same id. Views are held weakly so a
/// dismissed screen does not keep its whole hierarchy alive.
/// </remarks>
internal sealed class NativeElementRegistry
{
    private readonly ConditionalWeakTable<object, string> _idsByView = new();
    private readonly Dictionary<string, string> _idsByStableKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WeakReference<object>> _viewsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _parents = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _next;

    /// <summary>
    /// Drops entries whose views have been collected. Called at the start of every tree walk so the
    /// bookkeeping does not grow without bound across navigations.
    /// </summary>
    public void BeginWalk()
    {
        lock (_gate)
        {
            if (_viewsById.Count < 512 && _idsByStableKey.Count < 512) return;

            var dead = _viewsById
                .Where(pair => !pair.Value.TryGetTarget(out _))
                .Select(pair => pair.Key)
                .ToList();

            foreach (var id in dead)
            {
                _viewsById.Remove(id);
                _parents.Remove(id);
            }

            PruneStableKeys();
        }
    }

    /// <summary>
    /// Returns the id for <paramref name="view"/>, allocating one on first sight.
    /// </summary>
    /// <remarks>
    /// A native handle can be reused for an unrelated view once its previous owner is gone, so a
    /// stable-key hit whose bound view is dead (or no longer sits at that handle) yields a fresh id
    /// rather than silently handing the recycled handle its predecessor's id. A collected managed
    /// peer is indistinguishable from a recycled handle here, so an id whose peer was collected is
    /// retired rather than revived: callers see a clean miss instead of a possible wrong element.
    /// See https://github.com/dotnet/maui-labs/issues/413 for making both cases distinguishable.
    /// </remarks>
    public string Register(object view, string? parentId)
    {
        lock (_gate)
        {
            var stableKey = GetStableKey(view);
            string id;

            if (stableKey != null)
            {
                if (!_idsByStableKey.TryGetValue(stableKey, out id!) || !IsBoundTo(id, stableKey))
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

            _viewsById[id] = new WeakReference<object>(view);
            _parents[id] = parentId;
            return id;
        }
    }

    /// <summary>Resolves an id back to a live view, or <c>null</c> when it is gone.</summary>
    public object? Resolve(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        lock (_gate)
        {
            if (!_viewsById.TryGetValue(id, out var reference)) return null;
            if (reference.TryGetTarget(out var view)) return view;

            _viewsById.Remove(id);
            _parents.Remove(id);
            return null;
        }
    }

    /// <summary>Returns the parent id recorded for <paramref name="id"/> during the last walk.</summary>
    public string? ParentOf(string id)
    {
        lock (_gate)
        {
            return _parents.TryGetValue(id, out var parent) ? parent : null;
        }
    }

    /// <summary>
    /// True while <paramref name="id"/> still resolves to a live view sitting at the same native
    /// handle that produced <paramref name="stableKey"/>. Guards against a recycled handle whose
    /// stale key would otherwise rebind an unrelated view onto a dead element's id.
    /// </summary>
    private bool IsBoundTo(string id, string stableKey)
        => _viewsById.TryGetValue(id, out var reference)
            && reference.TryGetTarget(out var view)
            && GetStableKey(view) == stableKey;

    /// <summary>
    /// Drops stable-key mappings whose id no longer backs a live view. Their handles may be reused
    /// for unrelated views, so the mappings must not outlive the element they were minted for.
    /// </summary>
    private void PruneStableKeys()
    {
        var orphaned = _idsByStableKey
            .Where(pair => !_viewsById.TryGetValue(pair.Value, out var reference) || !reference.TryGetTarget(out _))
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in orphaned)
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
