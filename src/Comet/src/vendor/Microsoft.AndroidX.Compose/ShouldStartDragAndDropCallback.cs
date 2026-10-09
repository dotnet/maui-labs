using Android.Runtime;
using Kotlin.Jvm.Functions;

namespace AndroidX.Compose;

// JCW adapter for the `shouldStartDragAndDrop: (DragAndDropEvent) -> Boolean`
// parameter on `Modifier.dragAndDropTarget(...)`. Wraps a managed
// `Func<DragAndDropEvent, bool>` and returns a boxed Java.Lang.Boolean so
// Kotlin's Function1 contract is honoured.
[Register("net/compose/ShouldStartDragAndDropCallback")]
internal sealed class ShouldStartDragAndDropCallback : Java.Lang.Object, IFunction1
{
    readonly Func<DragAndDropEvent, bool> _callback;

    public ShouldStartDragAndDropCallback(Func<DragAndDropEvent, bool> callback)
    {
        _callback = callback;
    }

    public Java.Lang.Object? Invoke(Java.Lang.Object? p0)
    {
        if (p0 is null)
            return Java.Lang.Boolean.False;
        var jvm = Android.Runtime.Extensions.JavaCast<AndroidX.Compose.UI.Draganddrop.DragAndDropEvent>(p0);
        return _callback(new DragAndDropEvent(jvm)) ? Java.Lang.Boolean.True : Java.Lang.Boolean.False;
    }
}
