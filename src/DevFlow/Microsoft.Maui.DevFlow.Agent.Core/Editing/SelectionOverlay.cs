using Microsoft.Maui.Controls;

namespace Microsoft.Maui.DevFlow.Agent.Core.Editing;

/// <summary>
/// Draws a selection adorner inside the running app and implements "pick an element" mode through
/// each window's <see cref="IVisualDiagnosticsOverlay"/>, which already accounts for density,
/// scroll offsets and native chrome on every platform.
/// </summary>
internal sealed class SelectionOverlay
{
    readonly HashSet<IVisualDiagnosticsOverlay> _hooked = new(ReferenceEqualityComparer.Instance);
    WeakReference<IVisualTreeElement>? _highlighted;

    /// <summary>Raised with the picked element after a tap in pick mode. Pick mode is already off.</summary>
    public event Action<VisualElement>? Picked;

    public bool IsPickMode { get; private set; }

    /// <summary>Highlights <paramref name="element"/>, or clears every highlight when null.</summary>
    /// <returns>False when the element's window has no diagnostics overlay.</returns>
    public bool Highlight(Application app, IVisualTreeElement? element)
    {
        foreach (var overlay in Overlays(app))
        {
            overlay.RemoveAdorners();
            overlay.Invalidate();
        }

        _highlighted = null;
        if (element is null)
            return true;

        if (element is not VisualElement { Window.VisualDiagnosticsOverlay: { } target })
            return false;

        target.AddAdorner(element, false);
        target.Invalidate();
        _highlighted = new WeakReference<IVisualTreeElement>(element);
        return true;
    }

    /// <summary>
    /// Clears the highlight when its element has left the tree (removed, or replaced by a XAML
    /// reload), so the adorner is not left drawn where the element used to be.
    /// </summary>
    public void ClearIfDetached(Application app)
    {
        if (_highlighted is null)
            return;

        if (!_highlighted.TryGetTarget(out var element) || element is not VisualElement { Window: not null })
            Highlight(app, null);
    }

    /// <summary>Turns pick mode on or off in every window.</summary>
    /// <returns>False when no window has a diagnostics overlay, so taps cannot be intercepted.</returns>
    public bool SetPickMode(Application app, bool enabled)
    {
        IsPickMode = enabled;
        var installed = false;
        foreach (var overlay in Overlays(app))
        {
            if (_hooked.Add(overlay))
                overlay.Tapped += (_, e) => OnTapped(app, e.VisualTreeElements);

            overlay.DisableUITouchEventPassthrough = enabled;
            overlay.EnableDrawableTouchHandling = enabled;
            installed = true;
        }

        if (!installed)
            IsPickMode = false;
        return installed;
    }

    /// <summary>Handles a tap on the overlay. Internal so tests can drive it without a platform.</summary>
    internal VisualElement? OnTapped(Application app, IEnumerable<IVisualTreeElement> elements)
    {
        if (!IsPickMode)
            return null;

        // the smallest visible element under the pointer is the one the user meant; the deepest one
        // wins ties (and elements that have not been measured yet)
        var picked = elements
            .OfType<VisualElement>()
            .Where(v => v is not Page && v.IsVisible)
            .OrderBy(v => v.Width > 0 && v.Height > 0 ? v.Width * v.Height : double.MaxValue)
            .ThenByDescending(Depth)
            .FirstOrDefault();

        SetPickMode(app, false);
        if (picked is null)
            return null;

        Highlight(app, picked);
        Picked?.Invoke(picked);
        return picked;
    }

    static int Depth(Element element)
    {
        var depth = 0;
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
            depth++;
        return depth;
    }

    static IEnumerable<IVisualDiagnosticsOverlay> Overlays(Application app)
    {
        foreach (var window in app.Windows)
        {
            if (window.VisualDiagnosticsOverlay is not { } overlay)
                continue;

            if (!overlay.IsPlatformViewInitialized && window.Handler is not null)
                overlay.Initialize();

            yield return overlay;
        }
    }
}
