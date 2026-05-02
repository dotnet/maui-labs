using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Comet;

namespace Comet.Reactive;

public sealed class Signal<T> : IReactiveSource, INotifyPropertyRead, IDisposable
{
	static readonly PropertyChangedEventArgs ValueChangedArgs = new("Value");

	volatile StrongBox<T> _box;
	readonly EqualityComparer<T> _comparer;
	readonly SubscriberList _subscribers = new();
	readonly object _writeLock = new();
	bool _disposed;
	uint _version;

	public event PropertyChangedEventHandler? PropertyRead;
	public event PropertyChangedEventHandler? PropertyChanged;

	public Signal(T initialValue, EqualityComparer<T>? comparer = null)
	{
		_box = new StrongBox<T>(initialValue);
		_comparer = comparer ?? EqualityComparer<T>.Default;
	}

	public string? DebugName { get; init; }

	public T Value
	{
		get
		{
			if (ReactiveScope.Current is not null)
				ReactiveScope.Current.TrackRead(this);
			else
				PropertyRead?.Invoke(this, ValueChangedArgs);
			return _box.Value;
		}
		set
		{
			lock (_writeLock)
			{
				if (_comparer.Equals(_box.Value, value))
					return;

				_box = new StrongBox<T>(value);
				unchecked { _version++; }
			}

			// All notifications fire OUTSIDE the lock to prevent deadlocks
			// when subscriber callbacks write to other Signals (two-way binding).
			_subscribers.NotifyAll(this);
			PropertyChanged?.Invoke(this, ValueChangedArgs);

			ReactiveDiagnostics.NotifySignalChanged(this, DebugName);
			ReactiveScheduler.EnsureFlushScheduled();
		}
	}

	public T Peek() => _box.Value;

	public uint Version => _version;

	public void Subscribe(IReactiveSubscriber subscriber)
	{
		if (_disposed)
			return;

		_subscribers.Add(subscriber);
	}

	public void Unsubscribe(IReactiveSubscriber subscriber)
	{
		_subscribers.Remove(subscriber);
	}

	public void Dispose()
	{
		_disposed = true;
		_subscribers.Clear();
	}

	public static implicit operator Signal<T>(T value) => new Signal<T>(value);

	public override string ToString()
	{
		var value = Peek();
		return value is null ? string.Empty : value.ToString();
	}
}
