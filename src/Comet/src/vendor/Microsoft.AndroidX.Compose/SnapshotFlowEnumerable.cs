
namespace AndroidX.Compose;

/// <summary>
/// Backing implementation for
/// <see cref="ComposeExtensions.SnapshotFlow{T}(Func{T})"/>. Each call to
/// <see cref="GetAsyncEnumerator(CancellationToken)"/> spins up a
/// fresh Kotlin <c>snapshotFlow</c> collection so multiple consumers
/// of the same producer don't share state.
/// </summary>
internal sealed class SnapshotFlowEnumerable<T> : IAsyncEnumerable<T>
{
    readonly Func<T> _producer;

    public SnapshotFlowEnumerable(Func<T> producer)
    {
        _producer = producer;
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new SnapshotFlowEnumerator<T>(_producer, cancellationToken);
}
