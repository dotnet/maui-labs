// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.AI;

/// <summary>
/// Subscribes to <see cref="IAgentSession.StateSnapshotReceived"/> and
/// <see cref="IAgentSession.StateDeltaReceived"/> events, deserializes incoming data,
/// applies JSON patches, and raises <see cref="ValueChanged"/> with the latest state.
/// </summary>
/// <typeparam name="T">The state type (must be JSON-serializable).</typeparam>
public sealed class StateChannel<T> : IDisposable where T : class
{
    private readonly IAgentSession _session;
    private JsonNode? _currentDocument;
    private T? _currentValue;

    /// <summary>The latest deserialized state value.</summary>
    public T? Value => _currentValue;

    /// <summary>Fires whenever the state changes (snapshot or delta).</summary>
    public event Action<T?>? ValueChanged;

    /// <summary>Optional JSON serializer options for deserialization.</summary>
    public JsonSerializerOptions? SerializerOptions { get; set; }

    public StateChannel(IAgentSession session)
    {
        _session = session;
        _session.StateSnapshotReceived += OnSnapshot;
        _session.StateDeltaReceived += OnDelta;
    }

    private void OnSnapshot(ReadOnlyMemory<byte> data)
    {
        try
        {
            _currentDocument = JsonNode.Parse(data.Span);
            _currentValue = JsonSerializer.Deserialize<T>(data.Span, SerializerOptions);
            MainThread.BeginInvokeOnMainThread(() => ValueChanged?.Invoke(_currentValue));
        }
        catch
        {
            // Swallow deserialization errors — value stays at previous state
        }
    }

    private void OnDelta(ReadOnlyMemory<byte> data)
    {
        try
        {
            _currentDocument = JsonPatch.Apply(_currentDocument, data.Span);
            if (_currentDocument is not null)
            {
                _currentValue = _currentDocument.Deserialize<T>(SerializerOptions);
            }
            MainThread.BeginInvokeOnMainThread(() => ValueChanged?.Invoke(_currentValue));
        }
        catch
        {
            // Swallow patch/deserialization errors
        }
    }

    /// <summary>Manually sets the state value (e.g., for initial state).</summary>
    public void SetValue(T? value)
    {
        _currentValue = value;
        if (value is not null)
        {
            _currentDocument = JsonSerializer.SerializeToNode(value, SerializerOptions);
        }
    }

    public void Dispose()
    {
        _session.StateSnapshotReceived -= OnSnapshot;
        _session.StateDeltaReceived -= OnDelta;
    }
}
