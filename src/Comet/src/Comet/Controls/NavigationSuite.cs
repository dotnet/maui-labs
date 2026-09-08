#nullable enable
using System.Collections.Generic;
using Comet.Reactive;

namespace Comet
{
	/// <summary>Which nav chrome a <see cref="NavigationSuite"/> shows for the current window.</summary>
	public enum NavigationSuiteVariant
	{
		BottomBar,
		Rail,
		PermanentDrawer,
		/// <summary>No persistent chrome — content fills the window; navigation happens
		/// through the modal drawer alone (JetNews' drawer-first compact chrome).</summary>
		None,
	}

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
			View content, View? railHeader = null, View? drawerHeader = null,
			Signal<bool>? drawerOpen = null,
			Microsoft.Maui.Graphics.Color? containerColor = null,
			Microsoft.Maui.Graphics.Color? indicatorColor = null,
			System.Func<double, double, NavigationSuiteVariant>? variantFor = null,
			bool railShowsSelectedLabel = false,
			Signal<int>? variantSignal = null)
		{
			SelectedIndex = selectedIndex;
			Items = items;
			Content = content;
			RailHeaderView = railHeader;
			DrawerHeaderView = drawerHeader;
			DrawerOpen = drawerOpen;
			ContainerColor = containerColor;
			IndicatorColor = indicatorColor;
			VariantPolicy = variantFor;
			RailShowsSelectedLabel = railShowsSelectedLabel;
			VariantSignal = variantSignal;
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

		/// <summary>When set, the whole suite wraps in a modal navigation drawer (the gold
		/// wraps the app — ReplyNavigationComponents.kt:122-137): the sheet shows
		/// <see cref="DrawerHeaderView"/> + labeled items. Open it from chrome (the rail's
		/// menu item); scrim tap / back / swipe dismissal writes the signal back to false.</summary>
		public Signal<bool>? DrawerOpen { get; }

		/// <summary>M3 chrome container color (bar/rail background — surfaceContainer in the
		/// gold). Android's real widgets theme themselves from the Compose scheme; the iOS
		/// twin composes its own chrome and needs the tokens explicitly.</summary>
		public Microsoft.Maui.Graphics.Color? ContainerColor { get; }

		/// <summary>Selected-item indicator (pill) color — secondaryContainer in the gold.</summary>
		public Microsoft.Maui.Graphics.Color? IndicatorColor { get; }

		/// <summary>App-specific breakpoint policy; null = the Reply defaults
		/// (<see cref="VariantFor"/>). JetNews: rail ≥ 840dp, otherwise <see cref="NavigationSuiteVariant.None"/>.</summary>
		public System.Func<double, double, NavigationSuiteVariant>? VariantPolicy { get; }

		/// <summary>Rail items show their label under the icon when selected
		/// (M3 <c>alwaysShowLabel=false</c> — the JetNews rail). Off = icon-only (Reply).</summary>
		public bool RailShowsSelectedLabel { get; }

		/// <summary>Optional out-signal the backend writes with the ACTIVE variant
		/// ((int)<see cref="NavigationSuiteVariant"/>) so content can adapt (JetNews swaps
		/// its home list chrome: app bar on compact, search field on expanded).</summary>
		public Signal<int>? VariantSignal { get; }

		/// <summary>The active-variant resolution every backend uses: the app policy if
		/// supplied, else the Reply defaults.</summary>
		public NavigationSuiteVariant VariantForWindow(double widthDp, double heightDp) =>
			VariantPolicy?.Invoke(widthDp, heightDp) ?? VariantFor(widthDp, heightDp);

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
