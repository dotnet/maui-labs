using System.Text.Json;

namespace Microsoft.Maui.DevFlow.WebView;

internal sealed class NativeCdpEventSubscription(NativeCdpResponseChannel channel) : IDisposable
{
    private readonly object _gate = new();
    private bool _disposed;
    private int _matchingBindings;
    private int _acceptedPayloads;

    internal int MatchingBindings => Volatile.Read(ref _matchingBindings);
    internal int AcceptedPayloads => Volatile.Read(ref _acceptedPayloads);

    internal bool TryReceive(string eventJson)
    {
        using var message = JsonDocument.Parse(eventJson);
        var root = message.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
            name.GetString() != channel.BindingName)
            return false;
        lock (_gate)
        {
            if (_disposed) return false;
            Interlocked.Increment(ref _matchingBindings);
            if (!root.TryGetProperty("payload", out var data) || data.ValueKind != JsonValueKind.String ||
                data.GetString() is not { } payload || !channel.TryReceive(payload))
                return false;
            Interlocked.Increment(ref _acceptedPayloads);
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
    }
}
