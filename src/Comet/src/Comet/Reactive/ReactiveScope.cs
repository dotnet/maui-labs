using System;
using System.Collections.Generic;

namespace Comet.Reactive;

/// <summary>
/// Tracking context for automatic dependency discovery.
/// Uses [ThreadStatic] for the current scope; background-thread reads are untracked by design.
/// </summary>
public sealed class ReactiveScope : IDisposable
{
	[ThreadStatic]
	static ReactiveScope? _current;

	public static ReactiveScope? Current => _current;

	readonly ReactiveScope? _previous;
	readonly HashSet<IReactiveSource> _reads;

	ReactiveScope(ReactiveScope? previous)
	{
		_previous = previous;
		_reads = new HashSet<IReactiveSource>();
	}

	public static ReactiveScope BeginTracking()
	{
		var scope = new ReactiveScope(_current);
		_current = scope;
		return scope;
	}

	public void TrackRead(IReactiveSource source)
	{
		_reads.Add(source);
	}

	public HashSet<IReactiveSource> EndTracking()
	{
		return _reads;
	}

	public void Dispose()
	{
		if (_current == this)
			_current = _previous;
	}
}
