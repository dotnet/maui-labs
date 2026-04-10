using System;
using System.Collections.Generic;

namespace Comet.Reactive;

public sealed class Computed<T> : IReactiveSource, IReactiveSubscriber, IDisposable
{
	readonly Func<T> _compute;
	readonly EqualityComparer<T> _comparer;
	T _cachedValue = default!;
	bool _dirty = true;
	uint _version;
	HashSet<IReactiveSource>? _dependencies;
	Dictionary<IReactiveSource, uint>? _depVersions;
	readonly SubscriberList _subscribers = new();
	bool _disposed;

	public Computed(Func<T> compute, EqualityComparer<T>? comparer = null)
	{
		_compute = compute ?? throw new ArgumentNullException(nameof(compute));
		_comparer = comparer ?? EqualityComparer<T>.Default;
	}

	public T Value
	{
		get
		{
			ReactiveScope.Current?.TrackRead(this);

			if (_dirty)
				Evaluate();

			return _cachedValue;
		}
	}

	public T Peek()
	{
		if (_dirty)
			Evaluate();

		return _cachedValue;
	}

	public uint Version => _version;

	void Evaluate()
	{
		if (_disposed)
			return;

		var oldDeps = _dependencies;
		_dirty = false;

		using var scope = ReactiveScope.BeginTracking();
		T newValue;
		HashSet<IReactiveSource> newDeps;
		try
		{
			newValue = _compute();
		}
		catch
		{
			scope.EndTracking();
			_dirty = true;
			return;
		}

		newDeps = scope.EndTracking();

		if (oldDeps is not null)
		{
			foreach (var dep in oldDeps)
			{
				if (!newDeps.Contains(dep))
					dep.Unsubscribe(this);
			}
		}

		foreach (var dep in newDeps)
		{
			if (oldDeps is null || !oldDeps.Contains(dep))
				dep.Subscribe(this);
		}

		_dependencies = newDeps;
		_depVersions = new Dictionary<IReactiveSource, uint>(newDeps.Count);
		foreach (var dep in newDeps)
			_depVersions[dep] = dep.Version;

		if (_dirty)
			return;

		if (!_comparer.Equals(_cachedValue, newValue))
		{
			_cachedValue = newValue;
			unchecked { _version++; }
			_subscribers.NotifyAll(this);
			ReactiveScheduler.EnsureFlushScheduled();
		}
	}

	public void OnDependencyChanged(IReactiveSource source)
	{
		if (_dirty || _disposed)
			return;

		_dirty = true;
		_subscribers.NotifyAll(this);
		ReactiveScheduler.EnsureFlushScheduled();
	}

	public void Subscribe(IReactiveSubscriber subscriber) => _subscribers.Add(subscriber);

	public void Unsubscribe(IReactiveSubscriber subscriber) => _subscribers.Remove(subscriber);

	public void Dispose()
	{
		_disposed = true;
		if (_dependencies is not null)
		{
			foreach (var dep in _dependencies)
				dep.Unsubscribe(this);
			_dependencies = null;
		}
		_subscribers.Clear();
		_depVersions = null;
	}
}
