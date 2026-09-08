#nullable enable
#if ANDROID
using System.Collections.Generic;
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.Compose
{
	/// <summary>Shared core for the Material 3 nav chrome nodes: owns the per-item icon/label
	/// slot nodes (<see cref="IBackendManagesOwnContent"/> — the real item widgets lay out their
	/// own slots), tracks the reactive selected index, and routes an item tap through the Comet
	/// control's <c>SelectItem</c> (signal write + OnSelect) so selection state has one source
	/// of truth.</summary>
	abstract class ComposeNavChromeNode : ComposeNode, IBackendManagesOwnContent
	{
		protected readonly BackendContext Context;
		protected readonly MutableState<int> Selected = new(0);
		protected readonly MutableState<int> ContentVersion = new(0);
		protected (ComposeNode icon, ComposeNode? label)[] ItemNodes =
			System.Array.Empty<(ComposeNode, ComposeNode?)>();
		bool _built;

		protected ComposeNavChromeNode(BackendContext context) => Context = context;

		protected abstract IReadOnlyList<NavigationItem> Items { get; }

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Nav_SelectedIndex)
				Selected.Value = value.AsInt;
		}

		/// <summary>Only a hot reload re-materializes the slot content (the code changed);
		/// an ordinary re-render keeps the materialized nodes — the view diff patches them.</summary>
		protected void OnChromeViewChanged(bool isHotReload)
		{
			if (!isHotReload)
				return;
			_built = false;
			ItemNodes = System.Array.Empty<(ComposeNode, ComposeNode?)>();
			ContentVersion.Value++;
		}

		protected void EnsureContent()
		{
			if (_built)
				return;
			_built = true;
			var items = Items;
			ItemNodes = new (ComposeNode, ComposeNode?)[items.Count];
			for (int i = 0; i < items.Count; i++)
				ItemNodes[i] = (
					(ComposeNode)CometBackendBridge.Materialize(items[i].IconView, Context),
					items[i].LabelView is { } label
						? (ComposeNode)CometBackendBridge.Materialize(label, Context)
						: null);
		}
	}

	/// <summary>Renders a Comet <see cref="Comet.NavigationBar"/> as the REAL Material 3
	/// <c>NavigationBar</c> + <c>NavigationBarItem</c> widgets.</summary>
	sealed class ComposeNavigationBarNode : ComposeNavChromeNode
	{
		// Material 3 NavigationBar container height (dp), before window insets — bottom
		// system-inset handling is the safe-area automation follow-up.
		const float BarHeightDp = 80f;

		Comet.NavigationBar _bar;

		public ComposeNavigationBarNode(Comet.NavigationBar bar, BackendContext context)
			: base(context) => _bar = bar;

		protected override IReadOnlyList<NavigationItem> Items => _bar.Items;

		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not Comet.NavigationBar bar)
				return;
			_bar = bar;
			OnChromeViewChanged(isHotReload);
		}

		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			double width = double.IsFinite(widthConstraint) && widthConstraint > 0
				? widthConstraint : ScreenSizeDp().Width;
			return new Size(width, BarHeightDp);
		}

		public override void Render(IComposer composer)
		{
			_ = ContentVersion.Value;
			EnsureContent();
			int selected = Selected.Value;

			// TODO(fidelity): surface the bridge's containerColor param (facade Phase-6) so
			// _bar.ContainerColor reaches the widget; until then the M3 default renders.
			var bar = new AndroidX.Compose.NavigationBar();
			for (int i = 0; i < ItemNodes.Length; i++)
			{
				int index = i;
				var item = new AndroidX.Compose.NavigationBarItem(
					selected: index == selected,
					onClick: () => _bar.SelectItem(index))
				{
					Icon = ItemNodes[i].icon,
				};
				if (ItemNodes[i].label is { } label)
					item.Label = label;
				bar.Add(item);
			}
			((ComposableNode)bar).Modifier = BuildNodeModifier();
			bar.Render(composer);
		}
	}

	/// <summary>Renders a Comet <see cref="Comet.NavigationRail"/> as the REAL Material 3
	/// <c>NavigationRail</c> + <c>NavigationRailItem</c> widgets, with the optional Comet
	/// header view (menu + FAB in the gold Reply) rendered first in the rail's content column.</summary>
	sealed class ComposeNavigationRailNode : ComposeNavChromeNode
	{
		// Material 3 NavigationRail container width (dp).
		const float RailWidthDp = 80f;

		Comet.NavigationRail _rail;
		ComposeNode? _headerNode;

		public ComposeNavigationRailNode(Comet.NavigationRail rail, BackendContext context)
			: base(context) => _rail = rail;

		protected override IReadOnlyList<NavigationItem> Items => _rail.Items;

		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not Comet.NavigationRail rail)
				return;
			_rail = rail;
			if (isHotReload)
				_headerNode = null;
			OnChromeViewChanged(isHotReload);
		}

		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			double height = double.IsFinite(heightConstraint) && heightConstraint > 0
				? heightConstraint : ScreenSizeDp().Height;
			return new Size(RailWidthDp, height);
		}

		public override void Render(IComposer composer)
		{
			_ = ContentVersion.Value;
			EnsureContent();
			if (_headerNode is null && _rail.HeaderView is { } header)
				_headerNode = (ComposeNode)CometBackendBridge.Materialize(header, Context);
			int selected = Selected.Value;

			// TODO(fidelity): surface containerColor (gold rail uses inverseOnSurface) — same
			// facade Phase-6 follow-up as the bar.
			var rail = new AndroidX.Compose.NavigationRail();
			if (_headerNode is not null)
				rail.Add(_headerNode);
			for (int i = 0; i < ItemNodes.Length; i++)
			{
				int index = i;
				var item = new AndroidX.Compose.NavigationRailItem(
					selected: index == selected,
					onClick: () => _rail.SelectItem(index))
				{
					Icon = ItemNodes[i].icon,
				};
				if (ItemNodes[i].label is { } label)
					item.Label = label;
				rail.Add(item);
			}
			((ComposableNode)rail).Modifier = BuildNodeModifier();
			rail.Render(composer);
		}
	}
}
#endif
