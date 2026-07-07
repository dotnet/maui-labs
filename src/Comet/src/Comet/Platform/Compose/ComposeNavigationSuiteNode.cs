#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.Compose
{
	/// <summary>Renders a Comet <see cref="Comet.NavigationSuite"/>: hosts the app content and
	/// swaps the REAL M3 chrome widget (bottom <c>NavigationBar</c> / <c>NavigationRail</c> /
	/// <c>PermanentDrawerSheet</c> + <c>NavigationDrawerItem</c>s) as the window crosses the
	/// gold breakpoints. The swap happens INSIDE this node via <see cref="MutableState{T}"/>
	/// (the Drawer/SelectorPanel idiom) because a body-level container-type swap does not
	/// reach the retained node tree. Item icon/label slot nodes are materialized ONCE and
	/// shared across variants — only one variant composes at a time. Owns its content
	/// (<see cref="IBackendManagesOwnContent"/>): the content subtree is laid out with the
	/// shared Yoga engine to the window minus the active chrome, re-flowed on every reactive
	/// flush and on every window-metrics change.</summary>
	sealed class ComposeNavigationSuiteNode : ComposeNode, IBackendManagesOwnContent
	{
		// M3 container sizes (dp): bar height, rail width; drawer sheet = the gold's max
		// (PermanentNavigationDrawerContent sizeIn(minWidth 200, maxWidth 300)).
		const float BarHeightDp = 80f, RailWidthDp = 80f, DrawerWidthDp = 300f;

		NavigationSuite _suite;
		readonly BackendContext _context;
		readonly MutableState<int> _selected = new(0);
		readonly MutableState<int> _variant = new((int)NavigationSuiteVariant.BottomBar);
		readonly MutableState<int> _geometry = new(0);   // bumped when the window size changes
		readonly MutableState<int> _contentVersion = new(0);
		(ComposeNode icon, ComposeNode? label)[] _items =
			System.Array.Empty<(ComposeNode, ComposeNode?)>();
		ComposeNode? _contentNode, _railHeaderNode, _drawerHeaderNode;
		Size _windowDp;
		// Plain mirror of _variant for reads OUTSIDE composition (Measure/Layout — the
		// SelectorPanel _selectorValue idiom); the MutableState drives recomposition.
		NavigationSuiteVariant _variantValue = NavigationSuiteVariant.BottomBar;
		bool _built, _metricsHooked;

		public ComposeNavigationSuiteNode(NavigationSuite suite, BackendContext context)
		{
			_suite = suite;
			_context = context;
			// Reflow the hosted content after every reactive flush (IME, rotation, content
			// growth) — this own-content node is a leaf to the engine, so the top-level
			// RunLayout can't reach inside it. Mirrors ComposeDrawerNode.
			Comet.Reactive.ReactiveScheduler.AfterFlush += ReflowContent;
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Nav_SelectedIndex)
				_selected.Value = value.AsInt;
		}

		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not NavigationSuite suite)
				return;
			_suite = suite;
			if (!isHotReload)
				return;
			_built = false;
			_items = System.Array.Empty<(ComposeNode, ComposeNode?)>();
			_contentNode = _railHeaderNode = _drawerHeaderNode = null;
			_contentVersion.Value++;
		}

		void EnsureContent()
		{
			if (_built)
				return;
			_built = true;

			var items = _suite.Items;
			_items = new (ComposeNode, ComposeNode?)[items.Count];
			for (int i = 0; i < items.Count; i++)
				_items[i] = (
					(ComposeNode)CometBackendBridge.Materialize(items[i].IconView, _context),
					items[i].LabelView is { } label
						? (ComposeNode)CometBackendBridge.Materialize(label, _context)
						: null);
			if (_suite.RailHeaderView is { } rail)
				_railHeaderNode = (ComposeNode)CometBackendBridge.Materialize(rail, _context);
			if (_suite.DrawerHeaderView is { } drawer)
				_drawerHeaderNode = (ComposeNode)CometBackendBridge.Materialize(drawer, _context);
			_contentNode = (ComposeNode)CometBackendBridge.Materialize(_suite.Content, _context);

			HookMetrics();
			UpdateFromMetrics();
		}

		/// <summary>Follow the per-root reactive window contract: a size change recomputes the
		/// variant (recomposes the chrome swap) and re-lays the content to the new bounds.</summary>
		void HookMetrics()
		{
			if (_metricsHooked)
				return;
			_metricsHooked = true;
			_suite.GetWindowMetrics().SizeDp.PropertyChanged += (_, __) =>
				Comet.ThreadHelper.RunOnMainThread(UpdateFromMetrics);
		}

		void UpdateFromMetrics()
		{
			var size = _suite.GetWindowMetrics().SizeDp.Peek();
			if (size.Width <= 0 || size.Height <= 0)
				size = ScreenSizeDp();
			if (size == _windowDp)
				return;
			_windowDp = size;
			_variantValue = NavigationSuite.VariantFor(size.Width, size.Height);
			_variant.Value = (int)_variantValue;
			_geometry.Value++;
			LayoutContent();
			Comet.Reactive.ReactiveScheduler.EnsureFlushScheduled();
		}

		Size ContentSizeDp() => _variantValue switch
		{
			NavigationSuiteVariant.Rail => new Size(_windowDp.Width - RailWidthDp, _windowDp.Height),
			NavigationSuiteVariant.PermanentDrawer => new Size(_windowDp.Width - DrawerWidthDp, _windowDp.Height),
			_ => new Size(_windowDp.Width, _windowDp.Height - BarHeightDp),
		};

		void LayoutContent()
		{
			if (_contentNode is null || _windowDp.Width <= 0)
				return;
			CometBackendLayoutEngine.Layout(_suite.Content, ContentSizeDp());
			// Headers lay out to their INTRINSIC height (they're column entries inside the
			// chrome widget — a window-height frame would push the destination items off-screen).
			if (_suite.RailHeaderView is { } rail)
				CometBackendLayoutEngine.Layout(rail,
					new Size(RailWidthDp, CometBackendLayoutEngine.Measure(rail).Height));
			if (_suite.DrawerHeaderView is { } drawer)
				CometBackendLayoutEngine.Layout(drawer,
					new Size(DrawerWidthDp, CometBackendLayoutEngine.Measure(drawer).Height));
		}

		void ReflowContent()
		{
			if (_contentNode is not null)
				LayoutContent();
		}

		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			double w = double.IsFinite(widthConstraint) && widthConstraint > 0 ? widthConstraint : ScreenSizeDp().Width;
			double h = double.IsFinite(heightConstraint) && heightConstraint > 0 ? heightConstraint : ScreenSizeDp().Height;
			return new Size(w, h);
		}

		public override void Render(IComposer composer)
		{
			_ = _contentVersion.Value;
			EnsureContent();
			_ = _geometry.Value;   // subscribe: a resize recomposes the offsets below
			int selected = _selected.Value;
			var variant = (NavigationSuiteVariant)_variant.Value;
			float w = (float)_windowDp.Width, h = (float)_windowDp.Height;

			var box = new Box();
			((ComposableNode)box).Modifier = BuildNodeModifier() ?? Modifier.Companion.FillMaxSize();

			// Content first (chrome paints over it at the shared edge).
			var contentHost = new Box();
			((ComposableNode)contentHost).Modifier = variant switch
			{
				NavigationSuiteVariant.Rail =>
					Modifier.Companion.AbsoluteOffset(new Dp(RailWidthDp), new Dp(0)),
				NavigationSuiteVariant.PermanentDrawer =>
					Modifier.Companion.AbsoluteOffset(new Dp(DrawerWidthDp), new Dp(0)),
				_ => Modifier.Companion,
			};
			contentHost.Add(_contentNode!);
			box.Add(contentHost);

			switch (variant)
			{
				case NavigationSuiteVariant.BottomBar:
				{
					var bar = new AndroidX.Compose.NavigationBar();
					for (int i = 0; i < _items.Length; i++)
					{
						int index = i;
						// Gold bar items are icon-only (ReplyNavigationComponents.kt:240-249).
						bar.Add(new AndroidX.Compose.NavigationBarItem(
							selected: index == selected,
							onClick: () => _suite.SelectItem(index))
						{ Icon = _items[i].icon });
					}
					((ComposableNode)bar).Modifier = Modifier.Companion
						.AbsoluteOffset(new Dp(0), new Dp(h - BarHeightDp))
						.Width(new Dp(w));
					box.Add(bar);
					break;
				}
				case NavigationSuiteVariant.Rail:
				{
					var rail = new AndroidX.Compose.NavigationRail();
					if (_railHeaderNode is not null)
						rail.Add(_railHeaderNode);
					for (int i = 0; i < _items.Length; i++)
					{
						int index = i;
						rail.Add(new AndroidX.Compose.NavigationRailItem(
							selected: index == selected,
							onClick: () => _suite.SelectItem(index))
						{ Icon = _items[i].icon });
					}
					((ComposableNode)rail).Modifier = Modifier.Companion.Height(new Dp(h));
					box.Add(rail);
					break;
				}
				case NavigationSuiteVariant.PermanentDrawer:
				{
					var column = new Column();
					if (_drawerHeaderNode is not null)
						column.Add(_drawerHeaderNode);
					for (int i = 0; i < _items.Length; i++)
					{
						int index = i;
						var item = new AndroidX.Compose.NavigationDrawerItem(
							selected: index == selected,
							onClick: () => _suite.SelectItem(index))
						{ Icon = _items[i].icon };
						// The drawer variant shows labels (gold :309-331); icon-only items
						// still need the required Label slot — reuse the icon as a stand-in.
						item.Label = _items[i].label ?? _items[i].icon;
						column.Add(item);
					}
					var sheet = new PermanentDrawerSheet();
					sheet.Add(column);
					((ComposableNode)sheet).Modifier = Modifier.Companion
						.Width(new Dp(DrawerWidthDp)).Height(new Dp(h));
					box.Add(sheet);
					break;
				}
			}

			box.Render(composer);
		}
	}
}
#endif
