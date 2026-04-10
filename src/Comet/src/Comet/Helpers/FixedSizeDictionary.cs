using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace System.Collections.Generic
{
	public class FixedSizeDictionary<T, T1> : IDictionary<T, T1>
	{
		public FixedSizeDictionary(int count)
		{
			queue = new FixedSizedQueue<T>(count)
			{
				OnDequeue = (key) => {
					if (dictionary.TryGetValue(key, out var value))
					{
						dictionary.Remove(key);
						OnDequeue?.Invoke(new KeyValuePair<T, T1>(key, value));
					}
				}
			};
		}

		public Action<KeyValuePair<T, T1>> OnDequeue { get; set; }

		readonly object _lock = new object();
		Dictionary<T, T1> dictionary = new Dictionary<T, T1>();
		FixedSizedQueue<T> queue;
		public T1 this[T key]
		{
			get
			{
				lock (_lock)
				{
					if (key is null)
						return default(T1);
					if (dictionary.TryGetValue(key, out var value))
					{
						queue.Enqueue(key);
						return value;
					}
					return default(T1);
				}
			}
			set
			{
				lock (_lock)
				{
					queue.Enqueue(key);
					dictionary[key] = value;
				}
			}
		}

		public ICollection<T> Keys => dictionary.Keys;

		public ICollection<T1> Values => dictionary.Values;

		public int Count => dictionary.Count;

		public bool IsReadOnly => false;

		public void Add(T key, T1 value)
		{
			this[key] = value;
		}

		public void Add(KeyValuePair<T, T1> item)
		{
			this[item.Key] = item.Value;
		}

		public void Clear()
		{
			List<KeyValuePair<T, T1>> items;
			lock (_lock)
			{
				queue.Clear();
				items = dictionary.ToList();
				dictionary.Clear();
			}
			foreach (var item in items)
				OnDequeue?.Invoke(item);
		}

		public bool Contains(KeyValuePair<T, T1> item) { lock (_lock) return dictionary.Contains(item); }

		public bool ContainsKey(T key) { lock (_lock) return dictionary.ContainsKey(key); }

		public void CopyTo(KeyValuePair<T, T1>[] array, int arrayIndex)
		{
			for (int i = 0; i < dictionary.Count; i++)
			{
				array[arrayIndex + i] = dictionary.ElementAt(i);
			}
		}

		public IEnumerator<KeyValuePair<T, T1>> GetEnumerator() => dictionary.GetEnumerator();

		public bool Remove(T key) { lock (_lock) return queue.Remove(key); }

		public bool Remove(KeyValuePair<T, T1> item) { lock (_lock) return queue.Remove(item.Key); }

		public bool TryGetValue(T key, out T1 value) { lock (_lock) return dictionary.TryGetValue(key, out value); }

		IEnumerator IEnumerable.GetEnumerator() => dictionary.GetEnumerator();
	}
}
