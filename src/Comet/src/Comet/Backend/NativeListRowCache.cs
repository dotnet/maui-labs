#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Comet.Reactive;

namespace Comet.Backend;

internal sealed class NativeListRowCache(BackendContext context, CometNodeFactory? factory = null) : IDisposable
{
	readonly Dictionary<int, Entry> _rows = new();
	long _access;

	internal int Capacity { get; set; }
	internal Action<int, View>? Evicted { get; set; }
	internal int Count => _rows.Count;

	public bool TryGet(int index, out NativeListRow row)
	{
		if (_rows.TryGetValue(index, out var entry))
		{
			entry.LastAccess = ++_access;
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
			var content = template();
			var row = NativeListRow.Materialize(content, generation);
			_rows.Add(index, new Entry(row, generation, content, ++_access));
			return row;
		}
		catch
		{
			generation.Dispose();
			throw;
		}
	}

	/// <summary>Reserve a row for a native composition, including uncommitted prefetch.</summary>
	internal IDisposable Retain(int index, NativeListRow row)
	{
		if (!_rows.TryGetValue(index, out var entry) || !ReferenceEquals(row, entry.Row))
			throw new InvalidOperationException("Cannot retain a row outside its current cache generation.");
		entry.Retainers++;
		entry.LastAccess = ++_access;
		return new Lease(() => ThreadHelper.RunOnMainThread(() =>
		{
			entry.Retainers--;
			Trim();
		}));
	}

	void Trim()
	{
		if (Capacity <= 0)
			return;
		using var hold = ReactiveScheduler.HoldFlushes();
		while (_rows.Count > Capacity)
		{
			int oldestIndex = -1;
			Entry? oldest = null;
			foreach (var pair in _rows)
			{
				if (pair.Value.Retainers == 0 &&
					(oldest is null || pair.Value.LastAccess < oldest.LastAccess))
				{
					oldestIndex = pair.Key;
					oldest = pair.Value;
				}
			}
			// Native-retained rows are never evicted to satisfy a budget.
			if (oldest is null)
				return;
			_rows.Remove(oldestIndex);
			Release(oldest);
			Evicted?.Invoke(oldestIndex, oldest.Content);
		}
	}

	static void Release(Entry entry)
	{
		entry.Generation.Dispose();
		entry.Row.Dispose();
	}

	public void InvalidateFrom(int index)
	{
		foreach (var key in _rows.Keys.Where(key => key >= index).ToArray())
		{
			var entry = _rows[key];
			_rows.Remove(key);
			Release(entry);
		}
	}

	public void Dispose() => InvalidateFrom(0);

	sealed class Entry(NativeListRow row, OwnedContentGeneration generation, View content, long lastAccess)
	{
		public NativeListRow Row { get; } = row;
		public OwnedContentGeneration Generation { get; } = generation;
		public View Content { get; } = content;
		public long LastAccess { get; set; } = lastAccess;
		public int Retainers { get; set; }
	}

	sealed class Lease(Action release) : IDisposable
	{
		Action? _release = release;
		public void Dispose() => System.Threading.Interlocked.Exchange(ref _release, null)?.Invoke();
	}
}
