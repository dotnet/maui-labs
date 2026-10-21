using System.Text.Json;

namespace Microsoft.Maui.DevFlow.WebView;

internal sealed class NativeCdpResponseChannel : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<long, TaskCompletionSource<string>> _pending = new();
    private TaskCompletionSource? _handshake;
    private string? _handshakeNonce;
    private bool _disposed;

    internal string BindingName { get; } = "__devflow_cdp_" + Guid.NewGuid().ToString("N");

    internal (string Script, Task Received) BeginHandshake()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _handshake?.TrySetCanceled();
            _handshakeNonce = Guid.NewGuid().ToString("N");
            _handshake = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var payload = JsonSerializer.Serialize(new { channel = BindingName, kind = "handshake", nonce = _handshakeNonce });
            var script = $"(() => {{ const binding = window[{JsonSerializer.Serialize(BindingName)}]; " +
                "if (typeof binding !== 'function') return 'missing'; " +
                $"binding({JsonSerializer.Serialize(payload)}); return 'sent'; }})()";
            return (script, _handshake.Task);
        }
    }

    internal void EndHandshake(Task received)
    {
        lock (_gate)
        {
            if (_handshake?.Task != received) return;
            _handshake.TrySetCanceled();
            _handshake = null;
            _handshakeNonce = null;
        }
    }

    internal Task<string> Register(long wireId)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add(wireId, completion);
            return completion.Task;
        }
    }

    internal void Remove(long wireId)
    {
        lock (_gate)
            if (_pending.Remove(wireId, out var completion))
                completion.TrySetCanceled();
    }

    internal bool TryReceive(string payload)
    {
        // Binding messages are untrusted page input, not an additional command transport.
        try
        {
            using var message = JsonDocument.Parse(payload);
            var root = message.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("channel", out var channel) ||
                channel.ValueKind != JsonValueKind.String || channel.GetString() != BindingName)
                return false;
            if (root.TryGetProperty("kind", out var kind))
            {
                if (kind.ValueKind != JsonValueKind.String || kind.GetString() != "handshake" ||
                    !root.TryGetProperty("nonce", out var nonce) || nonce.ValueKind != JsonValueKind.String)
                    return false;
                lock (_gate)
                    return !_disposed && _handshake is not null &&
                        nonce.GetString() == _handshakeNonce && _handshake.TrySetResult();
            }
            if (!root.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.String)
                return false;
            var json = response.GetString()!;
            using var reply = JsonDocument.Parse(json);
            if (reply.RootElement.ValueKind != JsonValueKind.Object ||
                !reply.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number ||
                !id.TryGetInt64(out var wireId))
                return false;
            lock (_gate)
                return !_disposed && _pending.TryGetValue(wireId, out var completion) && completion.TrySetResult(json);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _handshake?.TrySetCanceled();
            _handshake = null;
            _handshakeNonce = null;
            foreach (var completion in _pending.Values)
                completion.TrySetCanceled();
            _pending.Clear();
        }
    }
}
