#nullable enable
using System.Collections.Generic;
using Comet.Reactive;
using Microsoft.Maui.Graphics;

namespace Comet
{
	/// <summary>
	/// A Material 3 navigation rail: drives the REAL Compose <c>NavigationRail</c> +
	/// <c>NavigationRailItem</c> widgets. The optional <see cref="HeaderView"/> renders above the
	/// destinations inside the rail's content column (the gold Reply pattern: a menu item + FAB).
	/// Selection contract is identical to <see cref="NavigationBar"/> — one
	/// <see cref="SelectedIndex"/> signal, <see cref="SelectItem"/> from item taps.
	/// </summary>
	public partial class NavigationRail : View, IContainerView
	{
		public NavigationRail(Signal<int> selectedIndex, IReadOnlyList<NavigationItem> items,
			View? header = null, Color? containerColor = null)
		{
			SelectedIndex = selectedIndex;
			Items = items;
			HeaderView = header;
			ContainerColor = containerColor;
			foreach (var item in items)
				item.Parent = this;
			if (header is not null)
				header.Parent = this;
		}

		public Signal<int> SelectedIndex { get; }
		public IReadOnlyList<NavigationItem> Items { get; }
		public View? HeaderView { get; }
		public Color? ContainerColor { get; }

		/// <summary>See <see cref="NavigationBar.SelectItem"/> — same contract.</summary>
		public void SelectItem(int index)
		{
			SelectedIndex.Value = index;
			if (index >= 0 && index < Items.Count)
				Items[index].OnSelect?.Invoke();
		}

		public IReadOnlyList<View> GetChildren()
		{
			var children = new List<View>();
			if (HeaderView is not null)
				children.Add(HeaderView);
			children.AddRange(Items);
			return children;
		}
	}
}
