using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Comet
{
	public class MenuBar : View, IMenuBar
	{
		private readonly List<IMenuBarItem> _items = new List<IMenuBarItem>();

		public void Add(MenuBarItem item) => _items.Add(item);

		public IReadOnlyList<IMenuBarItem> Items => _items;

		bool IMenuBar.IsEnabled => this.GetEnvironment<bool?>(nameof(IView.IsEnabled)) ?? true;

		#region IList<IMenuBarItem>
		IMenuBarItem IList<IMenuBarItem>.this[int index]
		{
			get => _items[index];
			set => _items[index] = value;
		}

		int ICollection<IMenuBarItem>.Count => _items.Count;
		bool ICollection<IMenuBarItem>.IsReadOnly => false;

		void ICollection<IMenuBarItem>.Add(IMenuBarItem item) => _items.Add(item);
		void ICollection<IMenuBarItem>.Clear() => _items.Clear();
		bool ICollection<IMenuBarItem>.Contains(IMenuBarItem item) => _items.Contains(item);
		void ICollection<IMenuBarItem>.CopyTo(IMenuBarItem[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
		IEnumerator<IMenuBarItem> IEnumerable<IMenuBarItem>.GetEnumerator() => _items.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
		int IList<IMenuBarItem>.IndexOf(IMenuBarItem item) => _items.IndexOf(item);
		void IList<IMenuBarItem>.Insert(int index, IMenuBarItem item) => _items.Insert(index, item);
		bool ICollection<IMenuBarItem>.Remove(IMenuBarItem item) => _items.Remove(item);
		void IList<IMenuBarItem>.RemoveAt(int index) => _items.RemoveAt(index);
		#endregion
	}

	public class MenuBarItem : View, IMenuBarItem
	{
		private readonly List<IMenuElement> _items = new List<IMenuElement>();

		public string Text { get; set; }

		public void Add(IMenuElement item) => _items.Add(item);

		public IReadOnlyList<IMenuElement> Items => _items;

		bool IMenuBarItem.IsEnabled => this.GetEnvironment<bool?>(nameof(IView.IsEnabled)) ?? true;

		#region IList<IMenuElement>
		IMenuElement IList<IMenuElement>.this[int index]
		{
			get => _items[index];
			set => _items[index] = value;
		}

		int ICollection<IMenuElement>.Count => _items.Count;
		bool ICollection<IMenuElement>.IsReadOnly => false;

		void ICollection<IMenuElement>.Add(IMenuElement item) => _items.Add(item);
		void ICollection<IMenuElement>.Clear() => _items.Clear();
		bool ICollection<IMenuElement>.Contains(IMenuElement item) => _items.Contains(item);
		void ICollection<IMenuElement>.CopyTo(IMenuElement[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
		IEnumerator<IMenuElement> IEnumerable<IMenuElement>.GetEnumerator() => _items.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
		int IList<IMenuElement>.IndexOf(IMenuElement item) => _items.IndexOf(item);
		void IList<IMenuElement>.Insert(int index, IMenuElement item) => _items.Insert(index, item);
		bool ICollection<IMenuElement>.Remove(IMenuElement item) => _items.Remove(item);
		void IList<IMenuElement>.RemoveAt(int index) => _items.RemoveAt(index);
		#endregion
	}

	public class MenuFlyoutItem : View, IMenuFlyoutItem
	{
		public string Text { get; set; }
		public Action ClickedAction { get; set; }
		public IReadOnlyList<IKeyboardAccelerator> KeyboardAccelerators { get; set; }

		#region IMenuElement
		string IText.Text => Text;
		bool IMenuElement.IsEnabled => this.GetEnvironment<bool?>(nameof(IView.IsEnabled)) ?? true;
		void IMenuElement.Clicked() => ClickedAction?.Invoke();
		#endregion

		#region IMenuFlyoutItem
		IReadOnlyList<IKeyboardAccelerator> IMenuFlyoutItem.KeyboardAccelerators => KeyboardAccelerators;
		#endregion

		#region IImageSourcePart
		IImageSource IImageSourcePart.Source => null;
		bool IImageSourcePart.IsAnimationPlaying => false;
		void IImageSourcePart.UpdateIsLoading(bool isLoading) { }
		#endregion

		#region ITextStyle
		Color ITextStyle.TextColor => null;
		Font ITextStyle.Font => Font.Default;
		double ITextStyle.CharacterSpacing => 0;
		#endregion
	}

	public class MenuFlyoutSubItem : View, IMenuFlyoutSubItem
	{
		private readonly List<IMenuElement> _items = new List<IMenuElement>();

		public string Text { get; set; }
		public Action ClickedAction { get; set; }
		public IReadOnlyList<IKeyboardAccelerator> KeyboardAccelerators { get; set; }

		public void Add(IMenuElement item) => _items.Add(item);

		public IReadOnlyList<IMenuElement> Items => _items;

		#region IMenuElement
		string IText.Text => Text;
		bool IMenuElement.IsEnabled => this.GetEnvironment<bool?>(nameof(IView.IsEnabled)) ?? true;
		void IMenuElement.Clicked() => ClickedAction?.Invoke();
		#endregion

		#region IMenuFlyoutItem
		IReadOnlyList<IKeyboardAccelerator> IMenuFlyoutItem.KeyboardAccelerators => KeyboardAccelerators;
		#endregion

		#region IImageSourcePart
		IImageSource IImageSourcePart.Source => null;
		bool IImageSourcePart.IsAnimationPlaying => false;
		void IImageSourcePart.UpdateIsLoading(bool isLoading) { }
		#endregion

		#region ITextStyle
		Color ITextStyle.TextColor => null;
		Font ITextStyle.Font => Font.Default;
		double ITextStyle.CharacterSpacing => 0;
		#endregion

		#region IList<IMenuElement>
		IMenuElement IList<IMenuElement>.this[int index]
		{
			get => _items[index];
			set => _items[index] = value;
		}

		int ICollection<IMenuElement>.Count => _items.Count;
		bool ICollection<IMenuElement>.IsReadOnly => false;

		void ICollection<IMenuElement>.Add(IMenuElement item) => _items.Add(item);
		void ICollection<IMenuElement>.Clear() => _items.Clear();
		bool ICollection<IMenuElement>.Contains(IMenuElement item) => _items.Contains(item);
		void ICollection<IMenuElement>.CopyTo(IMenuElement[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
		IEnumerator<IMenuElement> IEnumerable<IMenuElement>.GetEnumerator() => _items.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
		int IList<IMenuElement>.IndexOf(IMenuElement item) => _items.IndexOf(item);
		void IList<IMenuElement>.Insert(int index, IMenuElement item) => _items.Insert(index, item);
		bool ICollection<IMenuElement>.Remove(IMenuElement item) => _items.Remove(item);
		void IList<IMenuElement>.RemoveAt(int index) => _items.RemoveAt(index);
		#endregion
	}

	public class MenuFlyoutSeparator : View, IMenuFlyoutSeparator
	{
		#region IMenuElement
		string IText.Text => string.Empty;
		bool IMenuElement.IsEnabled => false;
		void IMenuElement.Clicked() { }
		#endregion

		#region IMenuFlyoutItem
		IReadOnlyList<IKeyboardAccelerator> IMenuFlyoutItem.KeyboardAccelerators => null;
		#endregion

		#region IImageSourcePart
		IImageSource IImageSourcePart.Source => null;
		bool IImageSourcePart.IsAnimationPlaying => false;
		void IImageSourcePart.UpdateIsLoading(bool isLoading) { }
		#endregion

		#region ITextStyle
		Color ITextStyle.TextColor => null;
		Font ITextStyle.Font => Font.Default;
		double ITextStyle.CharacterSpacing => 0;
		#endregion
	}
}
