#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Comet.Reactive;

namespace Comet.Backend;

internal sealed class NativeListRowCache(BackendContext context, CometNodeFactory? factory = null) : IDisposable
{
	readonly Dictionary<int, (NativeListRow Row, OwnedContentGeneration Generation)> _rows = new();

	public bool TryGet(int index, out NativeListRow row)
	{
		if (_rows.TryGetValue(index, out var entry))
		{
			row = entry.Row;
			return true;
		}
		row = null!;
		return false;
	}

	public NativeListRow GetOrCreate(int index, View owner, Func<View> template)
	{
		if (TryGet(index, out var existing))
			return existing;

		// Publish the complete row before flushing modifier writes and parent layout.
		using var hold = ReactiveScheduler.HoldFlushes();
		var generation = factory is null
			? new OwnedContentGeneration(owner, context)
			: new OwnedContentGeneration(owner, factory, context);
		try
		{
			var row = NativeListRow.Materialize(template(), generation);
			_rows.Add(index, (row, generation));
			return row;
		}
		catch
		{
			generation.Dispose();
			throw;
		}
	}

	public void InvalidateFrom(int index)
	{
		foreach (var key in _rows.Keys.Where(key => key >= index).ToArray())
		{
			var entry = _rows[key];
			_rows.Remove(key);
			entry.Generation.Dispose();
			entry.Row.Dispose();
		}
	}

	public void Dispose() => InvalidateFrom(0);
}
