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

		/// <summary>Rebuild + swap the hosted subtree (call from state patches).</summary>
		protected void Refresh()
		{
			if (_shown is { } prev)
			{
				Comet.DevTools.CometDevRegistry.UnregisterSubtree(prev, includeRoot: true);
				_shown = null;
			}
			CometSwiftUIHost.ClearChildren(_native);
			var view = BuildContent();
			var node = (ISwiftUINativeNode)CometBackendBridge.Materialize(view, Context);
			CometSwiftUIHost.InsertChild(_native, 0, node.Native);
			_shown = view;
			Relayout();
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

		public virtual void ApplyProperty(PropertyId id, in PropertyValue value) { }
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

		public virtual void Dispose() => ReactiveScheduler.AfterFlush -= Relayout;
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
				return;
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
				return new HStack(spacing: (float)ListDetail.GapDp)
				{
					_listDetail.List.FlexGrow(1).FlexBasis(0),
					_listDetail.Detail.FlexGrow(1).FlexBasis(0),
				};
			return _open ? _listDetail.Detail : _listDetail.List;
		}

		bool _applied;

		public override void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id != PropertyIds.ListDetail_IsDetailOpen)
				return;
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
			var variant = NavigationSuite.VariantFor(size.Width, size.Height);

			if (variant == NavigationSuiteVariant.BottomBar)
				return new VStack(spacing: 0f)
				{
					new HStack().Frame(height: (float)safe.Top).FlexShrink(0),
					_suite.Content.FlexGrow(1).FlexBasis(0),
					BottomBar((float)safe.Bottom).FlexShrink(0),
				};

			// Rail (and, until a dedicated variant lands, the ≥1200dp case too): left rail
			// with the header + items, content beside it.
			return new HStack(spacing: 0f)
			{
				Rail((float)safe.Top).Frame(width: 80).FlexShrink(0),
				new VStack(spacing: 0f)
				{
					new HStack().Frame(height: (float)safe.Top).FlexShrink(0),
					_suite.Content.FlexGrow(1).FlexBasis(0),
				}.FlexGrow(1).FlexBasis(0),
			};
		}

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
						item.IconView,
						new HStack().FlexGrow(1),
					}
					.Frame(width: 64, height: 32)
					.Background(index == _selected ? Color.FromArgb("#33000000") : null)
					.CornerRadius(16),
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
			}.Background(Color.FromArgb("#14000000"));
		}

		View Rail(float safeTop)
		{
			var col = new VStack(spacing: 12f) { new HStack().Frame(height: safeTop) };
			if (_suite.RailHeaderView is { } header)
				col.Add(header);
			for (int i = 0; i < _suite.Items.Count; i++)
			{
				int index = i;
				col.Add(new HStack(spacing: 0f)
				{
					new HStack().FlexGrow(1),
					_suite.Items[i].IconView,
					new HStack().FlexGrow(1),
				}
				.Frame(width: 56, height: 32)
				.Background(index == _selected ? Color.FromArgb("#33000000") : null)
				.CornerRadius(16)
				.HorizontalLayoutAlignment(Microsoft.Maui.Primitives.LayoutAlignment.Center)
				.OnTap(_ => _suite.SelectItem(index)));
			}
			return col.Background(Color.FromArgb("#14000000"));
		}

		bool _applied;

		public override void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id != PropertyIds.Nav_SelectedIndex)
				return;
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
		bool _expanded;

		public SwiftUISearchBarNode(Comet.SearchBar bar, BackendContext context)
			: base(context) => _bar = bar;

		// In-flow footprint: the collapsed pill (56dp) or the expanded pane — NOT the base's
		// fill size (a fill-measured bar starves its flex siblings of height).
		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			double w = double.IsFinite(widthConstraint) && widthConstraint > 0 ? widthConstraint : ScreenDp().Width;
			return new Size(w, _expanded ? 480 : 56);
		}

		protected override View BuildContent()
		{
			if (!_expanded)
			{
				var pill = new HStack(spacing: 12f) { };
				if (_bar.LeadingView is { } leading)
					pill.Add(leading.FlexShrink(0));
				pill.Add(_bar.PlaceholderView.FlexGrow(1).FlexBasis(0));
				if (_bar.TrailingView is { } trailing)
					pill.Add(trailing.FlexShrink(0));
				return pill
					.Padding(new Microsoft.Maui.Thickness(16, 0))
					.Frame(height: 56)
					.Background(Color.FromArgb("#14000000"))
					.CornerRadius(28)
					.OnTap(_ => { _expanded = true; Refresh(); });
			}

			var field = SignalExtensions.TextField(_bar.Query, placeholder: "Search");
			return new VStack(spacing: 8f)
			{
				new HStack(spacing: 8f)
				{
					field.FlexGrow(1).FlexBasis(0),
					new Text("Close").FontSize(14)
						.OnTap(_ => { _expanded = false; _bar.Query.Value = ""; Refresh(); })
						.FlexShrink(0),
				}.Frame(height: 56),
				_bar.ContentView.FlexGrow(1).FlexBasis(0),
			}
			.Background(Color.FromArgb("#0A000000"))
			.CornerRadius(16);
		}
	}
}
#endif
