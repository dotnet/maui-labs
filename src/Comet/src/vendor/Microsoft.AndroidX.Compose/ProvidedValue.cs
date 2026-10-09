namespace AndroidX.Compose;

/// <summary>
/// A <see cref="CompositionLocal{T}"/> paired with a specific value,
/// produced by <see cref="CompositionLocal{T}.Provides(T)"/>. Hand
/// these to <see cref="CompositionLocalProvider"/> via collection-init
/// syntax to install the value for the duration of the provider's
/// child composition.
/// </summary>
public sealed class ProvidedValue
{
    internal AndroidX.Compose.Runtime.ProvidedValue Peer { get; }

    internal ProvidedValue(AndroidX.Compose.Runtime.ProvidedValue peer) =>
        Peer = peer;
}
