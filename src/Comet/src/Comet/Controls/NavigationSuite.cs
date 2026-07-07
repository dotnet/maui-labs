#nullable enable
using System.Collections.Generic;
using Comet.Reactive;

namespace Comet
{
	/// <summary>Which nav chrome a <see cref="NavigationSuite"/> shows for the current window.</summary>
	public enum NavigationSuiteVariant { BottomBar, Rail, PermanentDrawer }

	/// <summary>
	/// The adaptive navigation chrome (docs/adaptive-primitives-design.md): ONE control that
	/// hosts the app content and switches between a bottom <c>NavigationBar</c>, a
	/// <c>NavigationRail</c> (with optional header — Reply's menu + FAB), and a
	/// <c>PermanentDrawerSheet</c> (with optional header — Reply's "REPLY" + Compose FAB) as
	/// the window crosses the gold breakpoints (<see cref="VariantFor"/>). The backend node
	/// owns the swap internally (MutableState, same idiom as Drawer/SelectorPanel) because a
	/// body-level container-type swap does not propagate to the retained node tree — see the
	/// skipped reproduction in BackendBodyMetricsTests.
	/// <para>Selection contract matches <see cref="NavigationBar"/>: one
	/// <see cref="SelectedIndex"/> signal, <see cref="SelectItem"/> from item taps; the content
	/// view swaps screens by reading the signal in bindings.</para>
	/// </summary>
	public partial class NavigationSuite : View, IContainerView
	{
		public NavigationSuite(Signal<int> selectedIndex, IReadOnlyList<NavigationItem> items,
			View content, View? railHeader = null, View? drawerHeader = null)
		{
			SelectedIndex = selectedIndex;
			Items = items;
			Content = content;
			RailHeaderView = railHeader;
			DrawerHeaderView = drawerHeader;
			foreach (var item in items)
				item.Parent = this;
			content.Parent = this;
			if (railHeader is not null)
				railHeader.Parent = this;
			if (drawerHeader is not null)
				drawerHeader.Parent = this;
		}

		public Signal<int> SelectedIndex { get; }
		public IReadOnlyList<NavigationItem> Items { get; }
		public View Content { get; }
		public View? RailHeaderView { get; }
		public View? DrawerHeaderView { get; }

		/// <summary>See <see cref="NavigationBar.SelectItem"/> — same contract.</summary>
		public void SelectItem(int index)
		{
			SelectedIndex.Value = index;
			if (index >= 0 && index < Items.Count)
				Items[index].OnSelect?.Invoke();
		}

		/// <summary>The gold (Reply) chrome breakpoints — ReplyNavigationComponents.kt:77-107:
		/// bottom bar when width &lt; 600dp OR height &lt; 480dp; permanent drawer when width
		/// ≥ 1200dp; rail between. Pure so hosts test it and every backend shares it.</summary>
		public static NavigationSuiteVariant VariantFor(double widthDp, double heightDp) =>
			widthDp < 600 || heightDp < 480 ? NavigationSuiteVariant.BottomBar
			: widthDp >= 1200 ? NavigationSuiteVariant.PermanentDrawer
			: NavigationSuiteVariant.Rail;

		public IReadOnlyList<View> GetChildren()
		{
			var children = new List<View> { Content };
			if (RailHeaderView is not null)
				children.Add(RailHeaderView);
			if (DrawerHeaderView is not null)
				children.Add(DrawerHeaderView);
			children.AddRange(Items);
			return children;
		}
	}
}
