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
    private T _value;
    private readonly List<Action> _callbacks = new();

    internal AgentState(T? initialValue = null)
    {
        _value = initialValue ?? new T();
    }

    /// <summary>Gets or replaces the current state value.</summary>
    public T Value
    {
        get => _value;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _value = value;
            NotifyChanged();
        }
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
