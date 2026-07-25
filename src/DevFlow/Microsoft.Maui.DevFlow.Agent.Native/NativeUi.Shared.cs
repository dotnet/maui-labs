namespace Microsoft.Maui.DevFlow.Agent.Native;

/// <summary>
/// Framework-neutral helpers shared by every <c>NativeUi</c> backend. Anything expressible in terms
/// of <see cref="GetRoots"/> and <see cref="GetChildren"/> belongs here so the platform partials only
/// carry genuinely platform-specific code.
/// </summary>
internal static partial class NativeUi
{
    /// <summary>
    /// Walks up from <paramref name="viewObject"/> looking for the first ancestor (or the view itself)
    /// matching <paramref name="predicate"/>, then — when nothing was supplied at all — searches down
    /// from the roots instead.
    ///
    /// This mirrors the MAUI agent, whose scroll handler resolves its target with
    /// <c>FindAncestor&lt;ItemsView&gt;</c> and falls back to the page when no element was named. Without
    /// it, scrolling a list by naming one of its children (or naming nothing) fails on native even
    /// though the identical request succeeds against MAUI.
    /// </summary>
    public static object? FindSelfOrAncestor(object? viewObject, Func<object, bool> predicate, Func<object, object?> parentOf)
    {
        for (var current = viewObject; current != null; current = parentOf(current))
        {
            if (predicate(current))
                return current;
        }

        return viewObject == null ? FindFirstDescendant(predicate) : null;
    }

    /// <summary>Breadth-first search across every root for the first view matching <paramref name="predicate"/>.</summary>
    public static object? FindFirstDescendant(Func<object, bool> predicate)
    {
        var queue = new Queue<object>(GetRoots());

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (predicate(current))
                return current;

            foreach (var child in GetChildren(current))
                queue.Enqueue(child);
        }

        return null;
    }
}
