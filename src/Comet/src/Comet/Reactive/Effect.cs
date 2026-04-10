using System;
using System.Collections.Generic;

namespace Comet.Reactive;

public sealed class Effect : IReactiveSubscriber, IDisposable
{
	readonly Action _execute;
	HashSet<IReactiveSource>? _dependencies;
	bool _dirty;
	bool _disposed;

	public Effect(Action execute, bool runImmediately = true)
	{
		_execute = execute ?? throw new ArgumentNullException(nameof(execute));
		if (runImmediately)
			Run();
	}

	public void Run()
	{
		if (_disposed)
			return;

		var oldDeps = _dependencies;
		_dirty = false;

		using var scope = ReactiveScope.BeginTracking();
		HashSet<IReactiveSource> newDeps;
		try
		{
			_execute();
		}
		catch
		{
			scope.EndTracking();
			_dirty = false;
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
	}

	public void OnDependencyChanged(IReactiveSource source)
	{
		if (_dirty || _disposed)
			return;

		_dirty = true;
		ReactiveScheduler.ScheduleEffect(this);
	}

	internal void Flush()
	{
		if (!_dirty || _disposed)
			return;

		Run();
	}

	public void Dispose()
	{
		_disposed = true;
		if (_dependencies is not null)
		{
			foreach (var dep in _dependencies)
				dep.Unsubscribe(this);
			_dependencies = null;
		}
	}
}
