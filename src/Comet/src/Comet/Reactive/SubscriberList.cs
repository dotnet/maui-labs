using System;
using System.Buffers;

namespace Comet.Reactive;

internal sealed class SubscriberList
{
	WeakReference<IReactiveSubscriber>[]? _items;
	int _count;
	readonly object _lock = new();

	public void Add(IReactiveSubscriber subscriber)
	{
		lock (_lock)
		{
			_items ??= new WeakReference<IReactiveSubscriber>[4];
			if (_count == _items.Length)
				Array.Resize(ref _items, _items.Length * 2);
			_items[_count++] = new WeakReference<IReactiveSubscriber>(subscriber);
		}
	}

	public void Remove(IReactiveSubscriber subscriber)
	{
		lock (_lock)
		{
			if (_items is null)
				return;

			for (var i = 0; i < _count; i++)
			{
				if (_items[i].TryGetTarget(out var target) && ReferenceEquals(target, subscriber))
				{
					_items[i] = _items[--_count];
					_items[_count] = null!;
					return;
				}
			}
		}
	}

	public void NotifyAll(IReactiveSource source)
	{
		IReactiveSubscriber[]? snapshot = null;
		var snapshotCount = 0;

		lock (_lock)
		{
			if (_items is null || _count == 0)
				return;

			snapshot = ArrayPool<IReactiveSubscriber>.Shared.Rent(_count);
			var writeIndex = 0;
			for (var i = 0; i < _count; i++)
			{
				if (_items[i].TryGetTarget(out var target))
				{
					snapshot[snapshotCount++] = target;
					_items[writeIndex++] = _items[i];
				}
			}
			_count = writeIndex;
		}

		try
		{
			for (var i = 0; i < snapshotCount; i++)
				snapshot[i].OnDependencyChanged(source);
		}
		finally
		{
			if (snapshot is not null)
				ArrayPool<IReactiveSubscriber>.Shared.Return(snapshot, clearArray: true);
		}
	}

	public void Clear()
	{
		lock (_lock)
		{
			_items = null;
			_count = 0;
		}
	}
}
