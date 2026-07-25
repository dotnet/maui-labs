using System.Runtime.CompilerServices;

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
    private readonly Dictionary<string, WeakReference<object>> _viewsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _parents = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _next;

    /// <summary>
    /// Drops entries whose views have been collected. Called at the start of every tree walk so the
    /// dictionary does not grow without bound across navigations.
    /// </summary>
    public void BeginWalk()
    {
        lock (_gate)
        {
            if (_viewsById.Count < 512) return;

            var dead = _viewsById
                .Where(pair => !pair.Value.TryGetTarget(out _))
                .Select(pair => pair.Key)
                .ToList();

            foreach (var id in dead)
            {
                _viewsById.Remove(id);
                _parents.Remove(id);
            }
        }
    }

    /// <summary>
    /// Returns the id for <paramref name="view"/>, allocating one on first sight.
    /// </summary>
    public string Register(object view, string? parentId)
    {
        lock (_gate)
        {
            if (!_idsByView.TryGetValue(view, out var id))
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
}
