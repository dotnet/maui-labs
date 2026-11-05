#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;

namespace Comet.Platform.Compose
{
	/// <summary>Renders a Comet <see cref="Comet.TabView"/> as the REAL Material 3
	/// <c>NavigationBar</c> + <c>NavigationBarItem</c> widgets, with a content area
	/// that shows the selected tab's content. Owns its content
	/// (<see cref="IBackendManagesOwnContent"/>): the tab content is laid out by the
	/// shared Yoga engine to the window minus the bar height.</summary>
	sealed class ComposeTabViewNode : ComposeNode, IBackendManagesOwnContent
	{
		const float BarHeightDp = 80f;

		TabView _tabView;
		readonly BackendContext _context;
		readonly MutableState<int> _selected = new(0);
		readonly MutableState<int> _contentVersion = new(0);
		readonly MutableState<int> _geometry = new(0);
		ComposeNode?[] _contentNodes = System.Array.Empty<ComposeNode?>();
		ComposeNode?[] _iconNodes = System.Array.Empty<ComposeNode?>();
		ComposeNode?[] _labelNodes = System.Array.Empty<ComposeNode?>();
		OwnedContentGeneration? _generation;
		bool _built;
		Size _windowDp;
		Microsoft.Maui.Thickness _safeDp;
		bool _metricsHooked;
		System.ComponentModel.PropertyChangedEventHandler? _metricsHandler;
		Backend.CometWindowMetrics? _hookedMetrics;

		public ComposeTabViewNode(TabView tabView, BackendContext context)
		{
			_tabView = tabView;
			_context = context;
			Comet.Reactive.ReactiveScheduler.AfterFlush += ReflowContent;
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Nav_SelectedIndex)
			{
				_selected.Value = value.AsInt;
				LayoutContent();
			}
		}

		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not TabView tv)
				return;
			_tabView = tv;
			// Always rebuild: a theme/root rebuild replaces the TabView instance with new
			// tab content Views. The old materialized nodes reference stale content from the
			// previous instance. Hot reload is one trigger, but any owner replacement must
			// rematerialize. Selection index is preserved via the signal (ApplyAllSetProperties
			// re-pushes it after the owner swap).
			DisposeGeneration();
			UnhookMetrics();
			_built = false;
			_contentNodes = System.Array.Empty<ComposeNode?>();
			_iconNodes = System.Array.Empty<ComposeNode?>();
			_labelNodes = System.Array.Empty<ComposeNode?>();
			_contentVersion.Value++;
		}

		void EnsureContent()
		{
			if (_built)
				return;
			using var hold = Comet.Reactive.ReactiveScheduler.HoldFlushes();
			_built = true;
			var tabs = _tabView.Tabs;
			_contentNodes = new ComposeNode?[tabs.Count];
			_iconNodes = new ComposeNode?[tabs.Count];
			_labelNodes = new ComposeNode?[tabs.Count];
			var generation = new OwnedContentGeneration(_tabView, _context);
			try
			{
				for (int i = 0; i < tabs.Count; i++)
				{
					int index = i;
					var tab = tabs[i];
					if (tab.Content is { } content)
						_contentNodes[i] = (ComposeNode)generation.Materialize(content);

					var iconSymbol = tab.Icon is { Length: > 0 } name ? name : "star";
					var icon = new Icon(iconSymbol).OnTap(_ => _tabView.SelectItem(index));
					_iconNodes[i] = (ComposeNode)generation.Materialize(icon);

					if (tab.Title is { Length: > 0 })
					{
						var label = new Text(tab.Title).OnTap(_ => _tabView.SelectItem(index));
						_labelNodes[i] = (ComposeNode)generation.Materialize(label);
					}
				}
				_generation = generation;
			}
			catch
			{
				generation.Dispose();
				_built = false;
				throw;
			}
			HookMetrics();
			UpdateFromMetrics();
		}

		void DisposeGeneration()
		{
			if (_generation is not { } generation)
				return;
			_generation = null;
			generation.Dispose();
		}

		void HookMetrics()
		{
			if (_metricsHooked)
				return;
			_metricsHooked = true;
			_hookedMetrics = _tabView.GetWindowMetrics();
			_metricsHandler = (_, __) => Comet.ThreadHelper.RunOnMainThread(UpdateFromMetrics);
			_hookedMetrics.SizeDp.PropertyChanged += _metricsHandler;
			_hookedMetrics.SafeAreaDp.PropertyChanged += _metricsHandler;
		}

		void UnhookMetrics()
		{
			if (_hookedMetrics is not null && _metricsHandler is not null)
			{
				_hookedMetrics.SizeDp.PropertyChanged -= _metricsHandler;
				_hookedMetrics.SafeAreaDp.PropertyChanged -= _metricsHandler;
			}
			_metricsHooked = false;
			_metricsHandler = null;
			_hookedMetrics = null;
		}

		void UpdateFromMetrics()
		{
			var metrics = _hookedMetrics ?? _tabView.GetWindowMetrics();
			var size = metrics.SizeDp.Peek();
			if (size.Width <= 0 || size.Height <= 0)
				size = ScreenSizeDp();
			var safe = metrics.SafeAreaDp.Peek();
			if (size == _windowDp && safe == _safeDp)
				return;
			_windowDp = size;
			_safeDp = safe;
			_geometry.Value++;
			LayoutContent();
			Comet.Reactive.ReactiveScheduler.EnsureFlushScheduled();
		}

		Size ContentSizeDp()
		{
			double w = _windowDp.Width > 0 ? _windowDp.Width : ScreenSizeDp().Width;
			double h = _windowDp.Height > 0 ? _windowDp.Height : ScreenSizeDp().Height;
			return new Size(w, h - _safeDp.Top - BarHeightDp);
		}

		void LayoutContent()
		{
			int sel = _selected.Value;
			var tabs = _tabView.Tabs;
			if (sel >= 0 && sel < tabs.Count && tabs[sel].Content is { } content)
				CometBackendLayoutEngine.Layout(content, ContentSizeDp());
		}

		void ReflowContent()
		{
			if (_built)
			{
				UpdateFromMetrics();
				LayoutContent();
			}
		}

		public override void Dispose()
		{
			Comet.Reactive.ReactiveScheduler.AfterFlush -= ReflowContent;
			DisposeGeneration();
			UnhookMetrics();
			base.Dispose();
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
			_ = _geometry.Value;   // subscribe: safe area / window change recomposes
			int selected = _selected.Value;
			var tabs = _tabView.Tabs;
			float w = _windowDp.Width > 0 ? (float)_windowDp.Width : (float)ScreenSizeDp().Width;
			float h = _windowDp.Height > 0 ? (float)_windowDp.Height : (float)ScreenSizeDp().Height;
			float safeTop = (float)_safeDp.Top;

			var box = new Box();
			((ComposableNode)box).Modifier = BuildNodeModifier() ?? Modifier.Companion.FillMaxSize();

			// Content area: offset below the status bar/cutout strip.
			if (selected >= 0 && selected < _contentNodes.Length && _contentNodes[selected] is { } contentNode)
			{
				var contentHost = new Box();
				((ComposableNode)contentHost).Modifier = Modifier.Companion
					.AbsoluteOffset(new Dp(0), new Dp(safeTop));
				contentHost.Add(contentNode);
				box.Add(contentHost);
			}

			// Bottom navigation bar: REAL M3 NavigationBar + NavigationBarItem.
			var bar = new AndroidX.Compose.NavigationBar();
			for (int i = 0; i < tabs.Count; i++)
			{
				int index = i;
				var tab = tabs[i];
				var item = new AndroidX.Compose.NavigationBarItem(
					selected: index == selected,
					onClick: () => _tabView.SelectItem(index));

				item.Icon = _iconNodes[index]!;

				if (_labelNodes[index] is { } label)
					item.Label = label;

				bar.Add(item);
			}
			((ComposableNode)bar).Modifier = Modifier.Companion
				.AbsoluteOffset(new Dp(0), new Dp(h - BarHeightDp))
				.Width(new Dp(w));
			box.Add(bar);

			box.Render(composer);
		}
	}
}
#endif
