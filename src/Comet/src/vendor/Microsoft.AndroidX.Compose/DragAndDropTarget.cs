using Android.Runtime;
using AndroidX.Compose.UI.Draganddrop;

namespace AndroidX.Compose;

/// <summary>
/// <c>androidx.compose.ui.draganddrop.DragAndDropTarget</c> adapter — the
/// receiver of drag-and-drop callbacks installed by
/// <see cref="Modifier.DragAndDropTarget(Func{DragAndDropEvent, bool}, DragAndDropTarget)"/>.
/// Mirrors Kotlin's <c>DragAndDropTarget</c> interface as a delegate-bag:
/// <see cref="OnDrop"/> processes the dropped payload (return <c>true</c>
/// to consume); the other six callbacks (<see cref="OnStarted"/>,
/// <see cref="OnEntered"/>, <see cref="OnMoved"/>, <see cref="OnChanged"/>,
/// <see cref="OnExited"/>, <see cref="OnEnded"/>) are typically used for
/// hover-state visuals — leave them <c>null</c> to inherit Kotlin's
/// default no-op behaviour.
///
/// The Java peer is created once per managed instance, so
/// <see cref="ComposeExtensions.Remember{T}(Func{T}, int, string)"/> the target
/// to keep the modifier-element identity stable across recompositions —
/// otherwise Compose rebuilds the underlying <c>DragAndDropTargetElement</c>
/// every frame and any internal hover/started/ended bookkeeping resets.
/// Delegates may be reassigned at any time; the new delegate is observed
/// on the next event.
/// </summary>
[Register("net/compose/DragAndDropTarget")]
public sealed class DragAndDropTarget : Java.Lang.Object, IDragAndDropTarget
{
    /// <summary>
    /// Delegate invoked when a drag is dropped onto the modified composable.
    /// Return <c>true</c> to mark the drop as consumed (matches Kotlin
    /// <c>onDrop</c>'s contract). When <c>null</c> the target reports the
    /// drop as unhandled and the system propagates it to the next eligible
    /// listener.
    /// </summary>
    public Func<DragAndDropEvent, bool>? OnDrop { get; set; }

    /// <summary>
    /// Invoked once when a drag-and-drop session that this target opted
    /// into via <c>shouldStartDragAndDrop</c> begins. A common use is to
    /// flip a "drag in progress" state-holder so siblings can reveal
    /// affordances. Mirrors Kotlin <c>onStarted</c>.
    /// </summary>
    public Action<DragAndDropEvent>? OnStarted { get; set; }

    /// <summary>
    /// Invoked when the dragged item enters this target's bounds — the
    /// canonical place to highlight the drop zone. Mirrors Kotlin
    /// <c>onEntered</c>.
    /// </summary>
    public Action<DragAndDropEvent>? OnEntered { get; set; }

    /// <summary>
    /// Invoked while the dragged item moves over this target. Mirrors
    /// Kotlin <c>onMoved</c>.
    /// </summary>
    public Action<DragAndDropEvent>? OnMoved { get; set; }

    /// <summary>
    /// Invoked when the source app modifies the drag's <c>ClipData</c> or
    /// <c>ClipDescription</c> mid-session (e.g. switches MIME types).
    /// Mirrors Kotlin <c>onChanged</c>.
    /// </summary>
    public Action<DragAndDropEvent>? OnChanged { get; set; }

    /// <summary>
    /// Invoked when the dragged item leaves this target's bounds — the
    /// counterpart to <see cref="OnEntered"/>; clear any hover highlight
    /// here. Mirrors Kotlin <c>onExited</c>.
    /// </summary>
    public Action<DragAndDropEvent>? OnExited { get; set; }

    /// <summary>
    /// Invoked once when the drag-and-drop session ends — fired regardless
    /// of whether the drop happened on this target. Use it to reset any
    /// session-scoped state set in <see cref="OnStarted"/>. Mirrors Kotlin
    /// <c>onEnded</c>.
    /// </summary>
    public Action<DragAndDropEvent>? OnEnded { get; set; }

    /// <summary>Construct an unwired target. Assign at least <see cref="OnDrop"/> before installing it on a modifier chain.</summary>
    public DragAndDropTarget() { }

    /// <summary>Convenience ctor that wires <paramref name="onDrop"/> in one expression.</summary>
    public DragAndDropTarget(Func<DragAndDropEvent, bool> onDrop)
    {
        OnDrop = onDrop;
    }

    bool IDragAndDropTarget.OnDrop(AndroidX.Compose.UI.Draganddrop.DragAndDropEvent p0)
    {
        var cb = OnDrop;
        if (cb is null)
            return false;
        return cb(new DragAndDropEvent(p0));
    }

    void IDragAndDropTarget.OnStarted(AndroidX.Compose.UI.Draganddrop.DragAndDropEvent p0) =>
        OnStarted?.Invoke(new DragAndDropEvent(p0));

    void IDragAndDropTarget.OnEntered(AndroidX.Compose.UI.Draganddrop.DragAndDropEvent p0) =>
        OnEntered?.Invoke(new DragAndDropEvent(p0));

    void IDragAndDropTarget.OnMoved(AndroidX.Compose.UI.Draganddrop.DragAndDropEvent p0) =>
        OnMoved?.Invoke(new DragAndDropEvent(p0));

    void IDragAndDropTarget.OnChanged(AndroidX.Compose.UI.Draganddrop.DragAndDropEvent p0) =>
        OnChanged?.Invoke(new DragAndDropEvent(p0));

    void IDragAndDropTarget.OnExited(AndroidX.Compose.UI.Draganddrop.DragAndDropEvent p0) =>
        OnExited?.Invoke(new DragAndDropEvent(p0));

    void IDragAndDropTarget.OnEnded(AndroidX.Compose.UI.Draganddrop.DragAndDropEvent p0) =>
        OnEnded?.Invoke(new DragAndDropEvent(p0));
}
