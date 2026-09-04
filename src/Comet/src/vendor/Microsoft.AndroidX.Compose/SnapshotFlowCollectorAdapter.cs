using System.Threading.Channels;
using Android.Runtime;
using Kotlin.Coroutines;
using Xamarin.KotlinX.Coroutines.Flow;

namespace AndroidX.Compose;

/// <summary>
/// JCW implementing Kotlin's <c>kotlinx.coroutines.flow.FlowCollector</c>
/// for <see cref="ComposeExtensions.SnapshotFlow{T}(Func{T})"/>. Each
/// <c>emit(value, continuation)</c> call from Kotlin unboxes the
/// value, pushes it onto a single-slot bounded
/// <see cref="Channel{T}"/>, and returns
/// <c>Unit.INSTANCE</c> synchronously — i.e. <c>emit</c> never
/// suspends Kotlin's continuation.
/// </summary>
/// <remarks>
/// <para>
/// The channel is <see cref="BoundedChannelFullMode.DropOldest"/> with
/// capacity 1 so a slow C# consumer naturally conflates to the
/// latest value. This matches Compose's own behaviour: between
/// snapshot applies, only the most recent producer result matters.
/// It also makes <c>emit</c> non-blocking, which is critical when
/// the Kotlin coroutine ends up dispatched onto the same main
/// thread the C# <c>await foreach</c> resumes on (blocking
/// <c>emit</c> there would deadlock).
/// </para>
/// </remarks>
[Register("net/compose/SnapshotFlowCollectorAdapter")]
internal sealed class SnapshotFlowCollectorAdapter<T> : Java.Lang.Object, IFlowCollector
{
    readonly ChannelWriter<T> _writer;

    public SnapshotFlowCollectorAdapter(ChannelWriter<T> writer)
    {
        _writer = writer;
    }

    public Java.Lang.Object Emit(Java.Lang.Object? value, IContinuation p1)
    {
        try
        {
            var unboxed = MutableState<T>.FromJava(value);
            // BoundedChannelFullMode.DropOldest guarantees TryWrite
            // succeeds — the channel always has room because the
            // oldest entry is silently discarded when full.
            _writer.TryWrite(unboxed);
        }
        catch (Exception ex)
        {
            // A faulting Emit would otherwise propagate into the
            // Kotlin flow as a CancellationException-shaped exit,
            // but the C# consumer would never see the underlying
            // cause. Surface it through channel completion so the
            // awaiter's await foreach throws it directly.
            _writer.TryComplete(ex);
        }
        // Synchronous completion: returning a non-suspended value
        // tells Kotlin's flow machinery that emit finished without
        // needing to park the continuation. snapshotFlow then loops
        // back to its next snapshot-apply wait.
        return Kotlin.Unit.Instance!;
    }
}
