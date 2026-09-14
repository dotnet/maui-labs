#nullable enable
using System.Collections.Generic;
using Comet.Reactive;
using Microsoft.Maui.Graphics;

namespace Comet
{
	/// <summary>
	/// A Material 3 bottom navigation bar: drives the REAL Compose <c>NavigationBar</c> +
	/// <c>NavigationBarItem</c> widgets (never a styled HStack), selection driven by a reactive
	/// <see cref="SelectedIndex"/> signal. Tapping an item calls <see cref="SelectItem"/>, which
	/// writes the signal and invokes the item's <see cref="NavigationItem.OnSelect"/> — so app
	/// chrome that reads the signal (screen swap, adaptive nav suite) re-renders reactively.
	/// </summary>
	public partial class NavigationBar : View, IContainerView
	{
		public NavigationBar(Signal<int> selectedIndex, IReadOnlyList<NavigationItem> items,
			Color? containerColor = null)
		{
			SelectedIndex = selectedIndex;
			Items = items;
			ContainerColor = containerColor;
			foreach (var item in items)
				item.Parent = this;
		}

		public Signal<int> SelectedIndex { get; }
		public IReadOnlyList<NavigationItem> Items { get; }
		public Color? ContainerColor { get; }

		/// <summary>Selection entry point shared by the platform item widgets and tests:
		/// writes <see cref="SelectedIndex"/> (reactive consumers re-render) and invokes the
		/// item's <see cref="NavigationItem.OnSelect"/>.</summary>
		public void SelectItem(int index)
		{
			SelectedIndex.Value = index;
			if (index >= 0 && index < Items.Count)
				Items[index].OnSelect?.Invoke();
		}

		public IReadOnlyList<View> GetChildren()
		{
			var children = new List<View>(Items.Count);
			foreach (var item in Items)
				children.Add(item);
			return children;
		}
	}
}
