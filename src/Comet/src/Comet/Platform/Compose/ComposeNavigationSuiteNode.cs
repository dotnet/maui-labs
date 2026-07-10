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
		Microsoft.Maui.Thickness _safeDp;
		// Plain mirror of _variant for reads OUTSIDE composition (Measure/Layout — the
		// SelectorPanel _selectorValue idiom); the MutableState drives recomposition.
		NavigationSuiteVariant _variantValue = NavigationSuiteVariant.BottomBar;
		bool _built, _metricsHooked;
		// Modal drawer (gold wraps the whole app): Comet signal ↔ animated drawer state,
		// the ComposeDrawerNode sync idiom.
		readonly MutableState<bool> _drawerOpen = new(false);
		readonly DrawerStateHolder _drawerHolder = new(AndroidX.Compose.Material3.DrawerValue.Closed);

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
			else if (id == PropertyIds.Drawer_IsOpen)
				_drawerOpen.Value = value.AsBool;
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
		System.ComponentModel.PropertyChangedEventHandler? _metricsHandler;
		Backend.CometWindowMetrics? _hookedMetrics;

		void HookMetrics()
		{
			if (_metricsHooked)
				return;
			_metricsHooked = true;
			_hookedMetrics = _suite.GetWindowMetrics();
			_metricsHandler = (_, __) => Comet.ThreadHelper.RunOnMainThread(UpdateFromMetrics);
			_hookedMetrics.SizeDp.PropertyChanged += _metricsHandler;
			_hookedMetrics.SafeAreaDp.PropertyChanged += _metricsHandler;
		}

		public override void Dispose()
		{
			Comet.Reactive.ReactiveScheduler.AfterFlush -= ReflowContent;
			if (_hookedMetrics is not null && _metricsHandler is not null)
			{
				_hookedMetrics.SizeDp.PropertyChanged -= _metricsHandler;
				_hookedMetrics.SafeAreaDp.PropertyChanged -= _metricsHandler;
			}
		}

		void UpdateFromMetrics()
		{
			var metrics = _suite.GetWindowMetrics();
			var size = metrics.SizeDp.Peek();
			if (size.Width <= 0 || size.Height <= 0)
				size = ScreenSizeDp();
			var safe = metrics.SafeAreaDp.Peek();
			if (size == _windowDp && safe == _safeDp)
				return;
			_windowDp = size;
			_safeDp = safe;
			_variantValue = _suite.VariantForWindow(size.Width, size.Height);
			if (_suite.VariantSignal is { } vs)
				vs.Value = (int)_variantValue;
			_variant.Value = (int)_variantValue;
			_geometry.Value++;
			LayoutContent();
			Comet.Reactive.ReactiveScheduler.EnsureFlushScheduled();
		}

		// The content slot clears the safe area automatically: the status-bar/cutout strip at
		// the top always, plus the bottom system-bar strip on variants where the content runs
		// to the bottom edge (rail/drawer — the M3 bottom NavigationBar already covers it).
		// The chrome widgets themselves keep the M3 default internal inset handling.
		Size ContentSizeDp() => _variantValue switch
		{
			NavigationSuiteVariant.Rail => new Size(
				_windowDp.Width - RailWidthDp,
				_windowDp.Height - _safeDp.Top - _safeDp.Bottom),
			NavigationSuiteVariant.PermanentDrawer => new Size(
				_windowDp.Width - DrawerWidthDp,
				_windowDp.Height - _safeDp.Top - _safeDp.Bottom),
			NavigationSuiteVariant.None => new Size(
				_windowDp.Width,
				_windowDp.Height - _safeDp.Top - _safeDp.Bottom),
			_ => new Size(
				_windowDp.Width,
				_windowDp.Height - _safeDp.Top - BarHeightDp),
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
			{
				// Re-check bounds too (the ComposeListDetailNode idiom): a resize that
				// recreates the activity can land before the metrics hook, so per-flush
				// recheck is what keeps the variant honest.
				UpdateFromMetrics();
				LayoutContent();
			}
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

			// Content first (chrome paints over it at the shared edge), shifted below the
			// status-bar/cutout strip — the layout math in ContentSizeDp shrank it to match.
			float safeTop = (float)_safeDp.Top;
			var contentHost = new Box();
			((ComposableNode)contentHost).Modifier = variant switch
			{
				NavigationSuiteVariant.Rail =>
					Modifier.Companion.AbsoluteOffset(new Dp(RailWidthDp), new Dp(safeTop)),
				NavigationSuiteVariant.PermanentDrawer =>
					Modifier.Companion.AbsoluteOffset(new Dp(DrawerWidthDp), new Dp(safeTop)),
				_ => Modifier.Companion.AbsoluteOffset(new Dp(0), new Dp(safeTop)),
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
					var railItem = new AndroidX.Compose.NavigationRailItem(
							selected: index == selected,
							onClick: () => _suite.SelectItem(index))
						{ Icon = _items[i].icon };
						// JetNews rail: label under the icon only while selected (gold
						// alwaysShowLabel=false). Reply stays icon-only.
						if (_suite.RailShowsSelectedLabel && _items[i].label is { } railLabel)
						{
							railItem.Label = railLabel;
							railItem.AlwaysShowLabel = false;
						}
						rail.Add(railItem);
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

			// Modal drawer wrap (gold ReplyNavigationComponents.kt:122-137): only when the
			// app supplied a DrawerOpen signal. The sheet reuses the drawer header + labeled
			// item nodes (rendering the same slot nodes in two composition sites is safe —
			// they're render-emitters over shared MutableState).
			if (_suite.DrawerOpen is null)
			{
				box.Render(composer);
				return;
			}

			bool open = _drawerOpen.Value;
			composer.LaunchedEffect(open, async ct =>
			{
				if (open && _drawerHolder.IsClosed)
					await _drawerHolder.OpenAsync();
				else if (!open && _drawerHolder.IsOpen)
					await _drawerHolder.CloseAsync();
			});
			// Reading CurrentValue subscribes this scope: a gesture/scrim dismissal
			// recomposes here and reports back so the Comet signal clears.
			var current = _drawerHolder.CurrentValue;
			composer.LaunchedEffect(current, ct =>
			{
				if (_drawerHolder.IsClosed && _drawerOpen.Value)
					Sink?.OnEvent(EventIds.DrawerClosed);
				// Symmetric gesture-open report — see ComposeDrawerNode (signal desync
				// otherwise swallows the next programmatic close).
				else if (_drawerHolder.IsOpen && !_drawerOpen.Value)
					Sink?.OnEvent(EventIds.DrawerOpened);
				return System.Threading.Tasks.Task.CompletedTask;
			});

			var modalColumn = new Column();
			if (_drawerHeaderNode is not null)
				modalColumn.Add(_drawerHeaderNode);
			for (int i = 0; i < _items.Length; i++)
			{
				int index = i;
				var item = new AndroidX.Compose.NavigationDrawerItem(
					selected: index == selected,
					onClick: () =>
					{
						_suite.SelectItem(index);
						Sink?.OnEvent(EventIds.DrawerClosed);   // gold closes on selection
					})
				{ Icon = _items[i].icon };
				item.Label = _items[i].label ?? _items[i].icon;
				modalColumn.Add(item);
			}
			var modalSheet = new ModalDrawerSheet();
			modalSheet.Add(modalColumn);

			var drawer = new ModalNavigationDrawer(drawerState: _drawerHolder)
			{
				Drawer = modalSheet,
				Content = box,
			};
			((ComposableNode)drawer).Modifier = Modifier.Companion.FillMaxSize();
			drawer.Render(composer);
		}
	}
}
#endif
