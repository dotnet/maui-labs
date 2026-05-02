using System;
using System.Collections;
using System.Collections.Generic;

namespace Comet.Reactive;

public sealed class SignalList<T> : IReactiveSource, IReadOnlyList<T>, IDisposable
{
	readonly List<T> _items;
	uint _version;
	readonly SubscriberList _subscribers = new();
	readonly Queue<ListChange<T>> _pendingChanges = new();

	public SignalList()
	{
		_items = new List<T>();
	}

	public SignalList(IEnumerable<T> items)
	{
		_items = new List<T>(items);
	}

	public IReadOnlyList<ListChange<T>> ConsumePendingChanges()
	{
		if (_pendingChanges.Count == 0)
			return Array.Empty<ListChange<T>>();

		var result = _pendingChanges.ToArray();
		_pendingChanges.Clear();
		return result;
	}

	public int Count
	{
		get
		{
			ReactiveScope.Current?.TrackRead(this);
			return _items.Count;
		}
	}

	public T this[int index]
	{
		get
		{
			ReactiveScope.Current?.TrackRead(this);
			return _items[index];
		}
		set
		{
			var oldItem = _items[index];
			_items[index] = value;
			Notify(ListChange<T>.Replace(index, oldItem, value));
		}
	}

	public void Add(T item)
	{
		_items.Add(item);
		Notify(ListChange<T>.Insert(_items.Count - 1, item));
	}

	public void Insert(int index, T item)
	{
		_items.Insert(index, item);
		Notify(ListChange<T>.Insert(index, item));
	}

	public bool Remove(T item)
	{
		var index = _items.IndexOf(item);
		if (index < 0)
			return false;

		_items.RemoveAt(index);
		Notify(ListChange<T>.Remove(index, item));
		return true;
	}

	public void RemoveAt(int index)
	{
		var item = _items[index];
		_items.RemoveAt(index);
		Notify(ListChange<T>.Remove(index, item));
	}

	public void Clear()
	{
		_items.Clear();
		Notify(ListChange<T>.Reset());
	}

	public void Batch(Action<List<T>> mutator)
	{
		mutator(_items);
		Notify(ListChange<T>.Reset());
	}

	void Notify(ListChange<T> change)
	{
		const int MaxPendingChanges = 100;
		if (_pendingChanges.Count >= MaxPendingChanges)
		{
			_pendingChanges.Clear();
			_pendingChanges.Enqueue(ListChange<T>.Reset());
		}
		else
		{
			_pendingChanges.Enqueue(change);
		}

		unchecked { _version++; }
		_subscribers.NotifyAll(this);
		ReactiveScheduler.EnsureFlushScheduled();
	}

	public uint Version => _version;

	public void Subscribe(IReactiveSubscriber subscriber) => _subscribers.Add(subscriber);

	public void Unsubscribe(IReactiveSubscriber subscriber) => _subscribers.Remove(subscriber);

	public IEnumerator<T> GetEnumerator()
	{
		ReactiveScope.Current?.TrackRead(this);
		return _items.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public void Dispose() => _subscribers.Clear();
}

public readonly record struct ListChange<T>
{
	public ListChangeKind Kind { get; init; }
	public int Index { get; init; }
	public T? Item { get; init; }
	public T? OldItem { get; init; }

	public static ListChange<T> Insert(int index, T item)
		=> new() { Kind = ListChangeKind.Insert, Index = index, Item = item };

	public static ListChange<T> Remove(int index, T item)
		=> new() { Kind = ListChangeKind.Remove, Index = index, OldItem = item };

	public static ListChange<T> Replace(int index, T oldItem, T newItem)
		=> new() { Kind = ListChangeKind.Replace, Index = index, Item = newItem, OldItem = oldItem };

	public static ListChange<T> Reset()
		=> new() { Kind = ListChangeKind.Reset };
}

public enum ListChangeKind
{
	Insert,
	Remove,
	Replace,
	Reset
}
