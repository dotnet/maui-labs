// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;

namespace Microsoft.Maui.AI.Chat;

/// <summary>Holds application state extracted from streamed agent responses.</summary>
/// <typeparam name="T">The application state type.</typeparam>
/// <remarks>
/// <para>
/// The state raises <see cref="PropertyChanged"/> for <see cref="Value"/> and
/// <see cref="HasPendingPredictiveState"/> once they changed, and then its <see cref="OnChanged"/>
/// callbacks, so a view can bind to the state's value. Assigning <see cref="Value"/> raises its change
/// even for the current instance. Mutating properties inside <see cref="Value"/> raises nothing; assign the
/// value again, or a new value, to notify observers.
/// </para>
/// <para>
/// Events are raised synchronously on the thread that changes the state: the thread that drives the
/// <see cref="AgentContext"/> for the state its responses report, rolls back, restores or clears, and the
/// caller's thread for <see cref="Value"/>, <see cref="AcceptPredictiveState"/> and
/// <see cref="RejectPredictiveState"/>. A handler may call back into the state or the context.
/// </para>
/// <para>
/// This type is single-thread-affine and is not thread-safe.
/// </para>
/// </remarks>
public sealed class AgentState<T> : INotifyPropertyChanged where T : class, new()
{
    private static readonly PropertyChangedEventArgs s_valueChanged = new(nameof(Value));
    private static readonly PropertyChangedEventArgs s_hasPendingPredictiveStateChanged =
        new(nameof(HasPendingPredictiveState));

    private readonly T _initialValue;
    private T _value;
    private T? _valueBeforePrediction;
    private readonly List<Action> _callbacks = new();

    internal AgentState(T? initialValue = null)
    {
        _initialValue = initialValue ?? new T();
        _value = _initialValue;
    }

    /// <summary>Occurs after <see cref="Value"/> or <see cref="HasPendingPredictiveState"/> changed.</summary>
    /// <remarks>
    /// Raised before the <see cref="OnChanged"/> callbacks of the same change. A handler that runs earlier may
    /// already have accepted or rejected the prediction, so a later handler reads the current state.
    /// </remarks>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets or replaces the current state value.</summary>
    /// <remarks>
    /// Assigning a value commits it: a pending prediction is dropped. The assignment raises
    /// <see cref="PropertyChanged"/> for this property even when it is the current instance, so assigning
    /// the value again after changing it in place refreshes bindings to its properties. A binding that hands
    /// a view the value itself sees the same instance and keeps its first value.
    /// </remarks>
    public T Value
    {
        get => _value;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var previousValue = _value;
            var wasPending = HasPendingPredictiveState;
            _value = value;
            _valueBeforePrediction = null;
            ReportChange(previousValue, wasPending, valueAssigned: true);
        }
    }

    /// <summary>Gets whether the current value is provisional and can still be rejected.</summary>
    /// <remarks>
    /// <see cref="PropertyChanged"/> is raised for this property when a prediction arrives and when it is
    /// accepted, rejected, replaced by a committed value, or rolled back at the end of its turn.
    /// </remarks>
    public bool HasPendingPredictiveState => _valueBeforePrediction is not null;

    /// <summary>Accepts the current predictive value as the committed state.</summary>
    public void AcceptPredictiveState()
    {
        if (_valueBeforePrediction is null)
            return;

        var previousValue = _value;
        _valueBeforePrediction = null;
        ReportChange(previousValue, wasPending: true, valueAssigned: false);
    }

    /// <summary>Restores the state that was current before the pending prediction.</summary>
    public void RejectPredictiveState()
    {
        if (_valueBeforePrediction is not { } previous)
            return;

        var previousValue = _value;
        _value = previous;
        _valueBeforePrediction = null;
        ReportChange(previousValue, wasPending: true, valueAssigned: false);
    }

    internal void SetPredictiveValue(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var previousValue = _value;
        var wasPending = HasPendingPredictiveState;
        _valueBeforePrediction ??= _value;
        _value = value;
        ReportChange(previousValue, wasPending, valueAssigned: true);
    }

    internal StateCheckpoint CaptureCheckpoint() =>
        new(_value, _valueBeforePrediction);

    internal void RestoreCheckpoint(StateCheckpoint checkpoint)
    {
        var changed = !ReferenceEquals(_value, checkpoint.Value)
            || !ReferenceEquals(
                _valueBeforePrediction,
                checkpoint.ValueBeforePrediction);
        var previousValue = _value;
        var wasPending = HasPendingPredictiveState;
        _value = checkpoint.Value;
        _valueBeforePrediction = checkpoint.ValueBeforePrediction;
        if (changed)
            ReportChange(previousValue, wasPending, valueAssigned: false);
    }

    internal void ResetToInitialValue()
    {
        var changed = !ReferenceEquals(_value, _initialValue)
            || _valueBeforePrediction is not null;
        var previousValue = _value;
        var wasPending = HasPendingPredictiveState;
        _value = _initialValue;
        _valueBeforePrediction = null;
        if (changed)
            ReportChange(previousValue, wasPending, valueAssigned: false);
    }

    /// <summary>
    /// Registers a callback invoked whenever the state changes: <see cref="Value"/> is assigned, predicted,
    /// rolled back, restored or reset, or a prediction is accepted or rejected.
    /// </summary>
    /// <remarks>Runs after the <see cref="PropertyChanged"/> events of the change.</remarks>
    public IDisposable OnChanged(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callbacks.Add(callback);
        return new CallbackRegistration(_callbacks, callback);
    }

    /// <summary>
    /// Raises <see cref="PropertyChanged"/> for what changed since <paramref name="previousValue"/> and
    /// <paramref name="wasPending"/> were read, and then the <see cref="OnChanged"/> callbacks.
    /// </summary>
    /// <param name="previousValue">The value before the change.</param>
    /// <param name="wasPending">Whether a prediction was pending before the change.</param>
    /// <param name="valueAssigned">Whether the value was assigned, which reports it even for the same instance.</param>
    private void ReportChange(T previousValue, bool wasPending, bool valueAssigned)
    {
        if (valueAssigned || !ReferenceEquals(_value, previousValue))
            PropertyChanged?.Invoke(this, s_valueChanged);

        // Read again: a handler of the value may already have accepted or rejected the prediction.
        if (HasPendingPredictiveState != wasPending)
            PropertyChanged?.Invoke(this, s_hasPendingPredictiveStateChanged);

        NotifyChanged();
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
