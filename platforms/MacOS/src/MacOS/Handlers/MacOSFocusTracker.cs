using AppKit;
using Foundation;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// Keeps <see cref="IView.IsFocused"/> in step with the window's first responder, which is what raises
/// MAUI's <c>Focused</c>/<c>Unfocused</c> events and the <c>Focused</c> visual state. AppKit has no
/// per-view focus events, but <c>NSWindow.firstResponder</c> is key-value observable, so one observer per
/// window covers every control - including text fields, whose first responder is the window's shared
/// field editor rather than the field itself.
/// </summary>
internal sealed class MacOSFocusTracker : NSObject
{
    static readonly NSString FirstResponderKey = new("firstResponder");

    readonly WeakReference<NSWindow> _window;
    WeakReference<IView>? _focused;
    bool _observing;

    public MacOSFocusTracker(NSWindow window)
    {
        _window = new WeakReference<NSWindow>(window);
        window.AddObserver(this, FirstResponderKey, NSKeyValueObservingOptions.New, IntPtr.Zero);
        _observing = true;
    }

    public override void ObserveValue(NSString keyPath, NSObject ofObject, NSDictionary change, IntPtr context)
    {
        if (keyPath == FirstResponderKey)
            Update();
        else
            base.ObserveValue(keyPath, ofObject, change, context);
    }

    void Update()
    {
        if (!_window.TryGetTarget(out var window))
            return;

        var next = Resolve(window.FirstResponder);
        IView? previous = null;
        _focused?.TryGetTarget(out previous);
        if (ReferenceEquals(previous, next))
            return;

        _focused = next is null ? null : new WeakReference<IView>(next);
        if (previous is not null)
            previous.IsFocused = false;
        if (next is not null)
            next.IsFocused = true;
    }

    /// <summary>
    /// The MAUI view that owns a responder: a text field's field editor maps to the field, and a native
    /// child (an Editor's NSTextView inside its scroll view) maps to the nearest registered handler.
    /// Containers - layouts, content views, pages, borders, scroll views - are not focus targets.
    /// </summary>
    internal static IView? Resolve(NSResponder? responder)
    {
        var view = responder as NSView;
        if (view is NSTextView { FieldEditor: true } fieldEditor && fieldEditor.Delegate is NSView owner)
            view = owner;

        for (var candidate = view; candidate is not null; candidate = candidate.Superview)
        {
            if (MacOSViewGeometry.TryGetHandler(candidate, out var handler) && handler.VirtualView is IView virtualView)
                return virtualView is ILayout or IContentView ? null : virtualView;
        }

        return null;
    }

    /// <summary>True when the window's first responder belongs to <paramref name="view"/>.</summary>
    internal static bool OwnsFirstResponder(NSWindow window, IView view) => ReferenceEquals(Resolve(window.FirstResponder), view);

    public void Detach()
    {
        if (_observing && _window.TryGetTarget(out var window))
            window.RemoveObserver(this, FirstResponderKey);
        _observing = false;

        if (_focused is not null && _focused.TryGetTarget(out var focused))
            focused.IsFocused = false;
        _focused = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Detach();
        base.Dispose(disposing);
    }
}
