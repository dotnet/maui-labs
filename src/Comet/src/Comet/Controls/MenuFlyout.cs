using System.Collections;
using System.Collections.Generic;
using Microsoft.Maui;

namespace Comet
{
	/// <summary>
	/// Context menu that can be attached to any view via the .ContextMenu() extension.
	/// Maps to MAUI's MenuFlyout.
	/// </summary>
	public class MenuFlyout : View, IMenuFlyout
	{
		private readonly List<IMenuElement> _items = new();

		public MenuFlyout(params MenuFlyoutItem[] items)
		{
			foreach (var item in items)
				_items.Add(item);
		}

		#region IList<IMenuElement>
		public IMenuElement this[int index]
		{
			get => _items[index];
			set => _items[index] = value;
		}

		public int Count => _items.Count;
		public bool IsReadOnly => false;

		public void Add(IMenuElement item) => _items.Add(item);
		public void Clear() => _items.Clear();
		public bool Contains(IMenuElement item) => _items.Contains(item);
		public void CopyTo(IMenuElement[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
		public IEnumerator<IMenuElement> GetEnumerator() => _items.GetEnumerator();
		public int IndexOf(IMenuElement item) => _items.IndexOf(item);
		public void Insert(int index, IMenuElement item) => _items.Insert(index, item);
		public bool Remove(IMenuElement item) => _items.Remove(item);
		public void RemoveAt(int index) => _items.RemoveAt(index);
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
		#endregion
	}
}
