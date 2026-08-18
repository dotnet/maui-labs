// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>Holds application state extracted from streamed agent responses.</summary>
/// <typeparam name="T">The application state type.</typeparam>
/// <remarks>
/// This type is single-thread-affine and is not thread-safe. Mutating properties inside
/// <see cref="Value"/> does not raise <see cref="OnChanged"/>; assign a new value to notify observers.
/// </remarks>
public sealed class AgentState<T> where T : class, new()
{
    private readonly T _initialValue;
    private T _value;
    private T? _valueBeforePrediction;
    private readonly List<Action> _callbacks = new();

    internal AgentState(T? initialValue = null)
    {
        _initialValue = initialValue ?? new T();
        _value = _initialValue;
    }

    /// <summary>Gets or replaces the current state value.</summary>
    public T Value
    {
        get => _value;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _value = value;
            _valueBeforePrediction = null;
            NotifyChanged();
        }
    }

    /// <summary>Gets whether the current value is provisional and can still be rejected.</summary>
    public bool HasPendingPredictiveState => _valueBeforePrediction is not null;

    /// <summary>Accepts the current predictive value as the committed state.</summary>
    public void AcceptPredictiveState()
    {
        if (_valueBeforePrediction is null)
            return;

        _valueBeforePrediction = null;
        NotifyChanged();
    }

    /// <summary>Restores the state that was current before the pending prediction.</summary>
    public void RejectPredictiveState()
    {
        if (_valueBeforePrediction is not { } previous)
            return;

        _value = previous;
        _valueBeforePrediction = null;
        NotifyChanged();
    }

    internal void SetPredictiveValue(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _valueBeforePrediction ??= _value;
        _value = value;
        NotifyChanged();
    }

    internal StateCheckpoint CaptureCheckpoint() =>
        new(_value, _valueBeforePrediction);

    internal void RestoreCheckpoint(StateCheckpoint checkpoint)
    {
        var changed = !ReferenceEquals(_value, checkpoint.Value)
            || !ReferenceEquals(
                _valueBeforePrediction,
                checkpoint.ValueBeforePrediction);
        _value = checkpoint.Value;
        _valueBeforePrediction = checkpoint.ValueBeforePrediction;
        if (changed)
            NotifyChanged();
    }

    internal void ResetToInitialValue()
    {
        var changed = !ReferenceEquals(_value, _initialValue)
            || _valueBeforePrediction is not null;
        _value = _initialValue;
        _valueBeforePrediction = null;
        if (changed)
            NotifyChanged();
    }

    /// <summary>Registers a callback invoked whenever <see cref="Value"/> is replaced.</summary>
    public IDisposable OnChanged(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callbacks.Add(callback);
        return new CallbackRegistration(_callbacks, callback);
    }

    private void NotifyChanged()
    {
        var snapshot = _callbacks.ToArray();
        foreach (var callback in snapshot)
            callback();
    }

    internal readonly record struct StateCheckpoint(
        T Value,
        T? ValueBeforePrediction);

    private sealed class CallbackRegistration : IDisposable
    {
        private List<Action>? _list;
        private Action? _callback;

        internal CallbackRegistration(List<Action> list, Action callback)
        {
            _list = list;
            _callback = callback;
        }

        public void Dispose()
        {
            if (_list is null || _callback is null)
                return;

            _list.Remove(_callback);
            _list = null;
            _callback = null;
        }
    }
}
