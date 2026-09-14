namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// Tracks the DevFlow native-element registrations made for a single NSAlert presentation
/// (the dialog surface, its buttons, and any prompt input) and guarantees every tracked
/// registration is unregistered exactly once, regardless of how the presentation ends -
/// native action, programmatic close, an exception while presenting, or a fallback result.
/// Contains no AppKit or MAUI dependencies so it can be exercised by plain unit tests.
/// </summary>
internal sealed class DialogNativeRegistrationScope : IDisposable
{
    readonly List<object> _tracked = new();
    readonly Action<object> _unregister;
    bool _disposed;

    public DialogNativeRegistrationScope(Action<object> unregister)
    {
        ArgumentNullException.ThrowIfNull(unregister);
        _unregister = unregister;
    }

    /// <summary>Number of native elements currently tracked by this scope. Exposed for testing.</summary>
    public int TrackedCount => _tracked.Count;

    /// <summary>
    /// Records a native element that has already been registered so it is unregistered when
    /// this scope is disposed. Call only after the corresponding registration succeeds.
    /// </summary>
    public void Track(object nativeElement)
    {
        ArgumentNullException.ThrowIfNull(nativeElement);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _tracked.Add(nativeElement);
    }

    /// <summary>
    /// Unregisters every tracked native element exactly once, most-recently-registered first.
    /// Safe to call multiple times (idempotent) and safe to call after a partial or failed
    /// registration sequence.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        for (var index = _tracked.Count - 1; index >= 0; index--)
            _unregister(_tracked[index]);

        _tracked.Clear();
    }
}
