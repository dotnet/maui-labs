using System;
using System.Collections.Generic;
using Comet.Reactive;
using Microsoft.Maui;

namespace Comet
{
	public class Picker : View, IPicker
	{
		public Picker() { }

		public Picker(int selectedIndex, params string[] items)
		{
			SelectedIndex = selectedIndex;
			if (items is not null)
				Items = new List<string>(items);
		}

		private PropertySubscription<IList<string>> _items;
		public PropertySubscription<IList<string>> Items
		{
			get => _items;
			set => this.SetPropertySubscription(ref _items, value);
		}

		private PropertySubscription<int> _selectedIndex;
		public PropertySubscription<int> SelectedIndex
		{
			get => _selectedIndex;
			set => this.SetPropertySubscription(ref _selectedIndex, value);
		}

		private PropertySubscription<string> _title;
		public new PropertySubscription<string> Title
		{
			get => _title;
			set => this.SetPropertySubscription(ref _title, value);
		}

		// IPicker implementation
		IList<string> IPicker.Items => Items?.CurrentValue ?? new List<string>();
		int IPicker.SelectedIndex { get => SelectedIndex?.CurrentValue ?? -1; set { if (SelectedIndex is not null) SelectedIndex.Set(value); } }
		string IPicker.Title => Title?.CurrentValue;
		Color IPicker.TitleColor => this.GetEnvironment<Color>(nameof(IPicker.TitleColor));

		// ITextStyle implementation
		Color ITextStyle.TextColor => this.GetEnvironment<Color>(nameof(ITextStyle.TextColor));
		Font ITextStyle.Font => this.GetEnvironment<Font>(nameof(ITextStyle.Font));
		double ITextStyle.CharacterSpacing => this.GetEnvironment<double>(nameof(ITextStyle.CharacterSpacing));

		// ITextAlignment implementation
		TextAlignment ITextAlignment.HorizontalTextAlignment => this.GetEnvironment<TextAlignment>(nameof(ITextAlignment.HorizontalTextAlignment));
		TextAlignment ITextAlignment.VerticalTextAlignment => this.GetEnvironment<TextAlignment>(nameof(ITextAlignment.VerticalTextAlignment));

		// IItemDelegate<string> implementation
		int IItemDelegate<string>.GetCount() => Items?.CurrentValue?.Count ?? 0;
		string IItemDelegate<string>.GetItem(int index)
		{
			var items = Items?.CurrentValue;
			if (items is null || index < 0 || index >= items.Count)
				return null;
			return items[index];
		}
	}
}
