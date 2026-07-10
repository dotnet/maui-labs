#nullable enable
#if IOS
using System;
using System.Collections.Generic;
using Comet.Backend;
using Comet.Reactive;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.SwiftUI
{
	/// <summary>
	/// Shared base for the iOS twins of the adaptive primitives: an own-content node that
	/// hosts ONE Comet view composed from the control's current state and swaps the whole
	/// subtree when that state changes — the <see cref="SwiftUINavigationNode"/> ShowTop
	/// idiom generalized. Per the iOS gate bar this is structure/values/interaction parity
	/// (SwiftUI-native look), not cross-OS pixel identity: chrome is composed from existing
	/// Comet views instead of binding new native widgets.
	/// </summary>
	abstract class SwiftUIHostedCompositionNode : ICometBackendNode, IBackendManagesOwnContent, ISwiftUINativeNode
	{
		protected readonly BackendContext Context;
		readonly CometNode _native = CometSwiftUIHost.MakeNode("navigation");
		View? _shown;
		Size _frameDp;
		protected ICometEventSink? Sink;

		public CometNode Native => _native;

		protected SwiftUIHostedCompositionNode(BackendContext context)
		{
			Context = context;
			// Reflow after every flush so nested content that changes measured size reflows
			// (mirrors the Compose own-content nodes); unhooked in Dispose.
			ReactiveScheduler.AfterFlush += Relayout;
		}

		/// <summary>Compose the full subtree for the CURRENT control state.</summary>
		protected abstract View BuildContent();

		/// <summary>Re-point the node at a re-built owner view (hot reload / ancestor
		/// refresh). Virtual so the interface member dispatches into subclasses — a plain
		/// method on a subclass never gets called (the ICometBackendNode default no-op wins).</summary>
		public virtual void OnOwnerViewChanged(View newView, bool isHotReload) { }

		/// <summary>Nodes materialized for the CURRENT hosted subtree; disposed on the next
		/// swap so stale generations release their static hooks (AfterFlush, signals, metrics).</summary>
		List<ICometBackendNode>? _generation;

		/// <summary>Rebuild + swap the hosted subtree (call from state patches). Flushes are
		/// held for the duration: materializing the new subtree writes the environment, and an
		/// inline flush's AfterFlush layout pass would re-arrange this node mid-build and
		/// re-enter (double-built generations / the Reply detail-swap overflow class).</summary>
		protected void Refresh()
		{
			using var hold = ReactiveScheduler.HoldFlushes();
			if (_shown is { } prev)
			{
				Comet.DevTools.CometDevRegistry.UnregisterSubtree(prev, includeRoot: true);
				_shown = null;
			}
			DisposeGeneration();
			CometSwiftUIHost.ClearChildren(_native);
			var view = BuildContent();
			var generation = new List<ICometBackendNode>();
			ISwiftUINativeNode node;
			using (CometBackendBridge.CollectNodes(generation))
				node = (ISwiftUINativeNode)CometBackendBridge.Materialize(view, Context);
			_generation = generation;
			CometSwiftUIHost.InsertChild(_native, 0, node.Native);
			_shown = view;
			Relayout();
		}

		void DisposeGeneration()
		{
			if (_generation is not { } nodes)
				return;
			_generation = null;
			foreach (var n in nodes)
				n.Dispose();
		}

		protected void Relayout()
		{
			if (_shown is not { } v)
				return;
			var size = _frameDp.Width > 0 ? _frameDp : ScreenDp();
			CometBackendLayoutEngine.Layout(v, size);
		}

		protected static Size ScreenDp()
		{
			var b = UIKit.UIScreen.MainScreen.Bounds;
			return new Size(b.Width, b.Height);
		}

		public virtual void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			// Honour the control's .Background(): the suite's safe-area strips paint with it
			// (without this the strips showed the bare window — white above the content).
			if (id == PropertyIds.BackgroundColor && value.AsColor is { } c)
				CometSwiftUIHost.SetColor(_native, "background",
					((uint)(c.Alpha * 255) << 24) | ((uint)(c.Red * 255) << 16) |
					((uint)(c.Green * 255) << 8) | (uint)(c.Blue * 255));
		}
		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }

		public virtual Size Measure(double widthConstraint, double heightConstraint)
		{
			var screen = ScreenDp();
			double w = double.IsFinite(widthConstraint) && widthConstraint > 0 ? widthConstraint : screen.Width;
			double h = double.IsFinite(heightConstraint) && heightConstraint > 0 ? heightConstraint : screen.Height;
			return new Size(w, h);
		}

		public void Arrange(Rect frame)
		{
			CometSwiftUIHost.SetFrame(_native, frame.X, frame.Y, frame.Width, frame.Height);
			_frameDp = new Size(frame.Width, frame.Height);
			if (_shown is null)
				Refresh();   // controls that push no properties still need an initial build
			else
				Relayout();
		}

		public void SetEventSink(ICometEventSink? sink) => Sink = sink;

		public virtual void Dispose()
		{
			ReactiveScheduler.AfterFlush -= Relayout;
			// Drop this generation's views from the dev registry — a disposed host's hosted
			// subtree would otherwise linger as ghost elements the agent can still query.
			if (_shown is { } shown)
			{
				Comet.DevTools.CometDevRegistry.UnregisterSubtree(shown, includeRoot: true);
				_shown = null;
			}
			DisposeGeneration();   // cascades: a disposed host releases its current subtree too
		}
	}

	/// <summary>iOS ContentSwitcher: swap the active route subtree on index patches.</summary>
	sealed class SwiftUIContentSwitcherNode : SwiftUIHostedCompositionNode
	{
		ContentSwitcher _switcher;
		int _index;

		public SwiftUIContentSwitcherNode(ContentSwitcher switcher, BackendContext context)
			: base(context) => _switcher = switcher;

		protected override View BuildContent()
		{
			var views = _switcher.Views;
			return _index >= 0 && _index < views.Count ? views[_index] : new VStack();
		}

		bool _applied;

		public override void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id != PropertyIds.ContentSwitcher_Index)
			{
				base.ApplyProperty(id, in value);
				return;
			}
			if (_applied && value.AsInt == _index)
				return;   // re-pushed unchanged (a re-materialize) — keep the built subtree
			_applied = true;
			_index = value.AsInt;
			Refresh();
		}
	}

	/// <summary>iOS ListDetail: two panes ≥840dp (side-by-side), else IsDetailOpen swaps
	/// list ↔ detail (the detail supplies its own back affordance — no system BackHandler
	/// on iOS).</summary>
	sealed class SwiftUIListDetailNode : SwiftUIHostedCompositionNode
	{
		ListDetail _listDetail;
		bool _open;

		public SwiftUIListDetailNode(ListDetail listDetail, BackendContext context)
			: base(context) => _listDetail = listDetail;

		protected override View BuildContent()
		{
			bool twoPane = ListDetail.TwoPaneFor(
				_listDetail.GetWindowMetrics().SizeDp.Peek() is { Width: > 0 } s ? s.Width : ScreenDp().Width);
			if (twoPane)
				// Split per the control's list fraction (flexGrow ratios) — keeps the iOS
				// twin on the SAME split as ComposeListDetailNode (Reply 0.5, JetNews ~1/3).
				return new HStack(spacing: (float)ListDetail.GapDp)
				{
					_listDetail.List.FlexGrow((float)_listDetail.ListFraction).FlexBasis(0),
					_listDetail.Detail.FlexGrow((float)(1 - _listDetail.ListFraction)).FlexBasis(0),
				};
			return _open ? _listDetail.Detail : _listDetail.List;
		}

		bool _applied;

		public override void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id != PropertyIds.ListDetail_IsDetailOpen)
			{
				base.ApplyProperty(id, in value);
				return;
			}
			if (_applied && value.AsBool == _open)
				return;
			_applied = true;
			_open = value.AsBool;
			Refresh();
		}
	}

	/// <summary>iOS NavigationSuite: compact bottom bar (iPhone) / rail (wide) composed from
	/// Comet views, safe-area aware; selection routes through <see cref="NavigationSuite.SelectItem"/>.
	/// The modal drawer and permanent-drawer variants are Android-first (the gold opens the
	/// modal only from the rail; the drawer variant needs ≥1200dp).</summary>
	sealed class SwiftUINavigationSuiteNode : SwiftUIHostedCompositionNode
	{
		const double BarHeight = 64;
		NavigationSuite _suite;
		int _selected;

		System.ComponentModel.PropertyChangedEventHandler? _metricsHandler;
		Backend.CometWindowMetrics? _hookedMetrics;
		Microsoft.Maui.Thickness _builtSafe;
		Size _builtSize;

		public SwiftUINavigationSuiteNode(NavigationSuite suite, BackendContext context)
			: base(context)
		{
			_suite = suite;
			// The first BuildContent can run before the root's layout pass publishes the
			// window size/safe area — refresh when they land (equality-gated on what the
			// last build actually used).
			_hookedMetrics = suite.GetWindowMetrics();
			_metricsHandler = (_, __) => ThreadHelper.RunOnMainThread(() =>
			{
				var size = _hookedMetrics!.SizeDp.Peek();
				var safe = _hookedMetrics.SafeAreaDp.Peek();
				if (size != _builtSize || safe != _builtSafe)
					Refresh();
			});
			_hookedMetrics.SizeDp.PropertyChanged += _metricsHandler;
			_hookedMetrics.SafeAreaDp.PropertyChanged += _metricsHandler;
		}

		public override void Dispose()
		{
			base.Dispose();
			if (_hookedMetrics is not null && _metricsHandler is not null)
			{
				_hookedMetrics.SizeDp.PropertyChanged -= _metricsHandler;
				_hookedMetrics.SafeAreaDp.PropertyChanged -= _metricsHandler;
			}
		}

		protected override View BuildContent()
		{
			var metrics = _suite.GetWindowMetrics();
			var size = metrics.SizeDp.Peek() is { Width: > 0 } s ? s : ScreenDp();
			var safe = metrics.SafeAreaDp.Peek();
			_builtSize = metrics.SizeDp.Peek();
			_builtSafe = safe;
			// The first build can run before the window exists (KeyWindow null → no inset
			// dispatch yet); fall back to typical iPhone insets — the metrics hook refreshes
			// with the real values the moment they publish.
			if (safe.Top == 0 && safe.Bottom == 0)
				safe = new Microsoft.Maui.Thickness(0, 59, 0, 34);
			var variant = _suite.VariantForWindow(size.Width, size.Height);
			// Publish the active variant (equality-gated) so content can adapt —
			// mirrors ComposeNavigationSuiteNode.UpdateFromMetrics.
			if (_suite.VariantSignal is { } vs && vs.Peek() != (int)variant)
				vs.Value = (int)variant;

			View chromed;
			if (variant == NavigationSuiteVariant.BottomBar)
				chromed = new VStack(spacing: 0f)
				{
					new HStack().Frame(height: (float)safe.Top).FlexShrink(0),
					_suite.Content.FlexGrow(1).FlexBasis(0),
					BottomBar((float)safe.Bottom).FlexShrink(0),
				};
			else if (variant == NavigationSuiteVariant.None)
				// Chromeless (JetNews compact): content fills; navigation via the modal drawer.
				chromed = new VStack(spacing: 0f)
				{
					new HStack().Frame(height: (float)safe.Top).FlexShrink(0),
					_suite.Content.FlexGrow(1).FlexBasis(0),
				};
			else
				// Rail (and, until a dedicated variant lands, the ≥1200dp case too): left rail
				// with the header + items, content beside it.
				chromed = new HStack(spacing: 0f)
				{
					Rail((float)safe.Top).Frame(width: 80).FlexShrink(0),
					new VStack(spacing: 0f)
					{
						new HStack().Frame(height: (float)safe.Top).FlexShrink(0),
						_suite.Content.FlexGrow(1).FlexBasis(0),
					}.FlexGrow(1).FlexBasis(0),
				};

			// Modal drawer wrap — chromeless variant only: the item views compose into the
			// sheet here, and a view materializes into exactly ONE node per generation, so
			// the sheet and a bar/rail can't share them. (The bar/rail variants never show
			// a drawer in the golds this twin serves.)
			if (variant == NavigationSuiteVariant.None && _suite.DrawerOpen is { } open)
				chromed = new Drawer(open, DrawerSheet((float)safe.Top), chromed);
			return chromed;
		}

		// Hand-composed to the M3 NavigationDrawerItem metrics (56dp pill, r28, icon 24 +
		// 12 gap, labelLarge) — the Android side renders the real widget.
		View DrawerSheet(float safeTop)
		{
			var col = new VStack(spacing: 0f) { new HStack().Frame(height: safeTop).FlexShrink(0) };
			if (_suite.DrawerHeaderView is { } header)
				col.Add(header.FlexShrink(0));
			for (int i = 0; i < _suite.Items.Count; i++)
			{
				int index = i;
				var item = _suite.Items[i];
				var row = new HStack(spacing: 0f)
				{
					item.IconView.Margin(new Microsoft.Maui.Thickness(16, 0, 12, 0))
						.VerticalLayoutAlignment(Microsoft.Maui.Primitives.LayoutAlignment.Center)
						.FlexShrink(0),
				};
				if (item.LabelView is { } label)
					row.Add(label.VerticalLayoutAlignment(Microsoft.Maui.Primitives.LayoutAlignment.Center));
				col.Add(row
					.Frame(height: 56)
					.Background(index == _selected ? PillColor : null)
					.CornerRadius(28)
					.Margin(new Microsoft.Maui.Thickness(12, index == 0 ? 0 : 4, 12, 0))
					.HorizontalLayoutAlignment(Microsoft.Maui.Primitives.LayoutAlignment.Fill)
					.OnTap(_ =>
					{
						_suite.SelectItem(index);
						if (_suite.DrawerOpen is { } o)
							o.Value = false;   // gold closes on selection
					}));
			}
			return col
				.VerticalLayoutAlignment(Microsoft.Maui.Primitives.LayoutAlignment.Fill)
				.Background(_suite.ContainerColor ?? Colors.White);
		}

		// M3 chrome tokens from the control (surfaceContainer / secondaryContainer in the
		// gold); neutral-overlay fallbacks when the sample didn't pass them.
		Color BarColor => _suite.ContainerColor ?? Color.FromArgb("#14000000");
		Color PillColor => _suite.IndicatorColor ?? Color.FromArgb("#33000000");

		View BottomBar(float safeBottom)
		{
			var row = new HStack(spacing: 0f);
			for (int i = 0; i < _suite.Items.Count; i++)
			{
				int index = i;
				var item = _suite.Items[i];
				var cell = new VStack(spacing: 0f)
				{
					new HStack().FlexGrow(1),
					new HStack(spacing: 0f)
					{
						new HStack().FlexGrow(1),
						// Center on the pill row's cross axis (default flex-start pinned
						// the icon to the pill's top edge).
						item.IconView.VerticalLayoutAlignment(Microsoft.Maui.Primitives.LayoutAlignment.Center),
						new HStack().FlexGrow(1),
					}
					.Frame(width: 64, height: 32)
					.Background(index == _selected ? PillColor : null)
					.CornerRadius(16)
					// Center the pill in its quarter cell (a fixed-width child of a column
					// otherwise sits at flex-start — the bar read flush-left on iOS).
					.HorizontalLayoutAlignment(Microsoft.Maui.Primitives.LayoutAlignment.Center),
					new HStack().FlexGrow(1),
				}
				.FlexGrow(1).FlexBasis(0)
				.OnTap(_ => _suite.SelectItem(index));
				row.Add(cell);
			}
			return new VStack(spacing: 0f)
			{
				row.Frame(height: (float)BarHeight),
				new HStack().Frame(height: safeBottom),
			}.Background(BarColor);
		}

		View Rail(float safeTop)
		{
			var col = new VStack(spacing: 12f) { new HStack().Frame(height: safeTop) };
			if (_suite.RailHeaderView is { } header)
				col.Add(header);
			for (int i = 0; i < _suite.Items.Count; i++)
			{
				int index = i;
				var pill = new HStack(spacing: 0f)
				{
					new HStack().FlexGrow(1),
					_suite.Items[i].IconView.VerticalLayoutAlignment(Microsoft.Maui.Primitives.LayoutAlignment.Center),
					new HStack().FlexGrow(1),
				}
				.Frame(width: 56, height: 32)
				.Background(index == _selected ? PillColor : null)
				.CornerRadius(16)
				.HorizontalLayoutAlignment(Microsoft.Maui.Primitives.LayoutAlignment.Center);
				// M3 alwaysShowLabel=false: label under the icon only while selected (JetNews).
				if (_suite.RailShowsSelectedLabel && index == _selected && _suite.Items[i].LabelView is { } label)
					col.Add(new VStack(spacing: 4f)
					{
						pill,
						label.HorizontalLayoutAlignment(Microsoft.Maui.Primitives.LayoutAlignment.Center),
					}.OnTap(_ => _suite.SelectItem(index)));
				else
					col.Add(pill.OnTap(_ => _suite.SelectItem(index)));
			}
			return col.Background(BarColor);
		}

		bool _applied;

		public override void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id != PropertyIds.Nav_SelectedIndex)
			{
				base.ApplyProperty(id, in value);
				return;
			}
			if (_applied && value.AsInt == _selected)
				return;
			_applied = true;
			_selected = value.AsInt;
			Refresh();
		}
	}

	/// <summary>iOS SearchBar: collapsed pill (placeholder + slots); tapping swaps to an
	/// expanded pane — a real TextField bound to the Query signal above the results content;
	/// its close affordance collapses. Values parity with the Android popup.</summary>
	sealed class SwiftUISearchBarNode : SwiftUIHostedCompositionNode
	{
		Comet.SearchBar _bar;
		System.ComponentModel.PropertyChangedEventHandler? _expandedHandler;

		public SwiftUISearchBarNode(Comet.SearchBar bar, BackendContext context)
			: base(context)
		{
			_bar = bar;
			// Expansion state lives on the CONTROL (survives node re-materialization from
			// ancestor refreshes); the node just re-renders when it changes.
			_expandedHandler = (_, __) => ThreadHelper.RunOnMainThread(Refresh);
			_bar.Expanded.PropertyChanged += _expandedHandler;
		}

		public override void Dispose()
		{
			base.Dispose();
			if (_expandedHandler is not null)
				_bar.Expanded.PropertyChanged -= _expandedHandler;
		}

		// In-flow footprint: the collapsed pill (56dp) or the expanded pane — NOT the base's
		// fill size (a fill-measured bar starves its flex siblings of height).
		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			double w = double.IsFinite(widthConstraint) && widthConstraint > 0 ? widthConstraint : ScreenDp().Width;
			return new Size(w, _bar.Expanded.Peek() ? 480 : 56);
		}

		protected override View BuildContent()
		{
			// The M3 SearchBar container token (Android's widget themes itself); fall back to
			// the old neutral overlay when the sample didn't pass one.
			var container = _bar.ContainerColor ?? Color.FromArgb("#14000000");

			if (!_bar.Expanded.Peek())
			{
				// Children CENTER on the row's cross axis (Yoga's default is flex-start —
				// the icon/placeholder/avatar sat pinned to the pill's top edge; the M3
				// input field centers its slots).
				var center = Microsoft.Maui.Primitives.LayoutAlignment.Center;
				var pill = new HStack(spacing: 12f) { };
				if (_bar.LeadingView is { } leading)
					pill.Add(leading.FlexShrink(0).VerticalLayoutAlignment(center));
				pill.Add(_bar.PlaceholderView.FlexGrow(1).FlexBasis(0).VerticalLayoutAlignment(center));
				if (_bar.TrailingView is { } trailing)
					pill.Add(trailing.FlexShrink(0).VerticalLayoutAlignment(center));
				return pill
					.Padding(new Microsoft.Maui.Thickness(16, 0))
					.Frame(height: 56)
					.Background(container)
					.CornerRadius(28)
					.OnTap(_ => _bar.Expanded.Value = true);
			}

			var centerV = Microsoft.Maui.Primitives.LayoutAlignment.Center;
			var field = SignalExtensions.TextField(_bar.Query, placeholder: "Search");
			return new VStack(spacing: 8f)
			{
				new HStack(spacing: 8f)
				{
					field.FlexGrow(1).FlexBasis(0).VerticalLayoutAlignment(centerV),
					new Text("Close").FontSize(14)
						.OnTap(_ => { _bar.Query.Value = ""; _bar.Expanded.Value = false; })
						.FlexShrink(0).VerticalLayoutAlignment(centerV),
				}.Frame(height: 56),
				_bar.ContentView.FlexGrow(1).FlexBasis(0),
			}
			.Background(container)
			.CornerRadius(16);
		}
	}
}
#endif
