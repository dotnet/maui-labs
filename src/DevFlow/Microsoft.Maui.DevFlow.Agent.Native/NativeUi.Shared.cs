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

    /// <summary>
    /// Stamps the framework-neutral property names every backend must expose, on top of whatever
    /// native names it has already published.
    ///
    /// The invariant is that anything <c>TrySetProperty</c> accepts has to be readable back. Getters and
    /// setters drifted apart independently on two backends before this existed: AppKit accepted
    /// <c>Text</c>/<c>IsVisible</c> while publishing only <c>StringValue</c>/<c>Hidden</c>, and Android
    /// accepted <c>IsVisible</c>/<c>IsEnabled</c>/<c>Opacity</c> while publishing only
    /// <c>Visibility</c>/<c>Enabled</c>/<c>Alpha</c>. Both shipped as write-only properties. Defining the
    /// set in one place is what stops the next backend repeating it.
    ///
    /// Null arguments are skipped, so a backend passes only the concepts its control actually has.
    /// Existing keys win: a backend that has already published a more accurate value keeps it.
    /// </summary>
    public static void AddCanonicalAliases(
        IDictionary<string, string?> properties,
        bool? isVisible = null,
        bool? isEnabled = null,
        double? opacity = null,
        string? text = null,
        string? value = null,
        bool? isChecked = null,
        string? accessibilityIdentifier = null,
        string? placeholder = null)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        Add("IsVisible", isVisible?.ToString());
        Add("IsEnabled", isEnabled?.ToString());
        Add("Opacity", opacity?.ToString(culture));
        Add("Alpha", opacity?.ToString(culture));
        Add("Text", text);
        Add("Value", value ?? text);
        Add("IsChecked", isChecked?.ToString());
        Add("On", isChecked?.ToString());
        Add("AccessibilityIdentifier", accessibilityIdentifier);
        Add("Placeholder", placeholder);

        void Add(string name, string? resolved)
        {
            if (resolved != null && !properties.ContainsKey(name))
                properties[name] = resolved;
        }
    }
}
