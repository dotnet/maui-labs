using Android.Views;

namespace AndroidX.Compose;

/// <summary>
/// Wrapper around Compose's <c>androidx.compose.ui.draganddrop.DragAndDropEvent</c>
/// — the payload passed to <see cref="DragAndDropTarget.OnDrop"/> and to the
/// <c>shouldStartDragAndDrop</c> predicate on
/// <see cref="Modifier.DragAndDropTarget(Func{DragAndDropEvent, bool}, DragAndDropTarget)"/>.
///
/// <see cref="AndroidDragEvent"/> exposes the underlying <see cref="DragEvent"/>
/// so callers can read <c>ClipData</c>, <c>ClipDescription</c>, and the drop
/// position via the regular Android API. <see cref="MimeTypes"/> is a quick
/// accessor for the MIME types advertised by the source app — useful for the
/// gating predicate (e.g. <c>event.MimeTypes.Any(m =&gt; m.StartsWith("image/"))</c>).
/// </summary>
public sealed class DragAndDropEvent
{
    internal AndroidX.Compose.UI.Draganddrop.DragAndDropEvent Jvm { get; }

    internal DragAndDropEvent(AndroidX.Compose.UI.Draganddrop.DragAndDropEvent jvm)
    {
        ArgumentNullException.ThrowIfNull(jvm);
        Jvm = jvm;
    }

    /// <summary>
    /// The Android <see cref="DragEvent"/> backing this Compose drag event.
    /// Use it to read <see cref="DragEvent.ClipData"/>,
    /// <see cref="DragEvent.ClipDescription"/>, the action code, and the
    /// drop position the same way you would in a classic
    /// <c>View.IOnDragListener</c>.
    /// </summary>
    public DragEvent AndroidDragEvent =>
        AndroidX.Compose.UI.Draganddrop.DragAndDrop_androidKt.ToAndroidDragEvent(Jvm);

    /// <summary>
    /// MIME types advertised by the drag source via its
    /// <see cref="Android.Content.ClipDescription"/>. Returns an empty list
    /// when the source carries no MIME metadata.
    /// </summary>
    public IReadOnlyList<string> MimeTypes
    {
        get
        {
            var desc = AndroidDragEvent.ClipDescription;
            if (desc is null)
                return [];
            int count = desc.MimeTypeCount;
            if (count <= 0)
                return [];
            var arr = new string[count];
            for (int i = 0; i < count; i++)
                arr[i] = desc.GetMimeType(i) ?? "";
            return arr;
        }
    }
}
