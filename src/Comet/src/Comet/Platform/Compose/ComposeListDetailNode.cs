#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.Compose
{
	/// <summary>Renders a Comet <see cref="Comet.ListDetail"/>: two panes 50/50 with a 16dp gap
	/// at expanded widths (≥ 840dp, the gold TwoPane), a single pane below that where the
	/// IsDetailOpen state swaps list ↔ detail with a <see cref="BackHandler"/> closing the
	/// compact detail (raised as <see cref="EventIds.DetailClosed"/>; the control writes the
	/// signal back). Same own-content shape as <see cref="ComposeNavigationSuiteNode"/>:
	/// MutableState drives the swap, both panes materialize once, panes are Yoga-laid to their
	/// slot and re-flowed on window-metrics changes and after every reactive flush.</summary>
	sealed class ComposeListDetailNode : ComposeNode, IBackendManagesOwnContent
	{
		ListDetail _listDetail;
		readonly BackendContext _context;
		readonly MutableState<bool> _detailOpen = new(false);
		readonly MutableState<bool> _twoPane = new(false);
		readonly MutableState<int> _geometry = new(0);
		readonly MutableState<int> _contentVersion = new(0);
		ComposeNode? _listNode, _detailNode;
		Size _windowDp;
		bool _twoPaneValue, _detailOpenValue;   // plain mirrors for reads outside composition
		bool _built, _metricsHooked;

		public ComposeListDetailNode(ListDetail listDetail, BackendContext context)
		{
			_listDetail = listDetail;
			_context = context;
			Comet.Reactive.ReactiveScheduler.AfterFlush += ReflowContent;
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.ListDetail_IsDetailOpen)
			{
				_detailOpenValue = value.AsBool;
				_detailOpen.Value = value.AsBool;
			}
		}

		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not ListDetail listDetail)
				return;
			_listDetail = listDetail;
			if (!isHotReload)
				return;
			_built = false;
			_listNode = _detailNode = null;
			_contentVersion.Value++;
		}

		void EnsureContent()
		{
			if (_built)
				return;
			_built = true;
			_listNode = (ComposeNode)CometBackendBridge.Materialize(_listDetail.List, _context);
			_detailNode = (ComposeNode)CometBackendBridge.Materialize(_listDetail.Detail, _context);
			HookMetrics();
			UpdateFromMetrics();
		}

		System.ComponentModel.PropertyChangedEventHandler? _metricsHandler;
		Backend.CometWindowMetrics? _hookedMetrics;

		void HookMetrics()
		{
			if (_metricsHooked)
				return;
			_metricsHooked = true;
			_hookedMetrics = _listDetail.GetWindowMetrics();
			_metricsHandler = (_, __) => Comet.ThreadHelper.RunOnMainThread(UpdateFromMetrics);
			_hookedMetrics.SizeDp.PropertyChanged += _metricsHandler;
		}

		public override void Dispose()
		{
			Comet.Reactive.ReactiveScheduler.AfterFlush -= ReflowContent;
			if (_hookedMetrics is not null && _metricsHandler is not null)
				_hookedMetrics.SizeDp.PropertyChanged -= _metricsHandler;
		}

		/// <summary>This node usually fills a NavigationSuite content slot, so its bounds are
		/// the window minus the active chrome — the Yoga-arranged frame, not the raw window.
		/// Metrics changes still drive the update (the suite re-lays this node first), and the
		/// frame is the fallback the first time through.</summary>
		Size BoundsDp()
		{
			if (FrameWidth > 0 && FrameHeight > 0)
				return new Size(FrameWidth, FrameHeight);
			var size = _listDetail.GetWindowMetrics().SizeDp.Peek();
			if (size.Width <= 0 || size.Height <= 0)
				size = ScreenSizeDp();
			return size;
		}

		void UpdateFromMetrics()
		{
			var size = BoundsDp();
			bool twoPane = ListDetail.TwoPaneFor(size.Width);
			if (size == _windowDp && twoPane == _twoPaneValue)
				return;
			_windowDp = size;
			_twoPaneValue = twoPane;
			_twoPane.Value = twoPane;
			_geometry.Value++;
			LayoutContent();
			Comet.Reactive.ReactiveScheduler.EnsureFlushScheduled();
		}

		void LayoutContent()
		{
			if (_listNode is null || _windowDp.Width <= 0)
				return;
			if (_twoPaneValue)
			{
				var paneW = (_windowDp.Width - ListDetail.GapDp) / 2;
				CometBackendLayoutEngine.Layout(_listDetail.List, new Size(paneW, _windowDp.Height));
				CometBackendLayoutEngine.Layout(_listDetail.Detail, new Size(paneW, _windowDp.Height));
			}
			else
			{
				CometBackendLayoutEngine.Layout(_listDetail.List, _windowDp);
				CometBackendLayoutEngine.Layout(_listDetail.Detail, _windowDp);
			}
		}

		void ReflowContent()
		{
			if (_listNode is not null)
			{
				// Re-check bounds too: the suite re-arranges this node's frame on chrome swaps.
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
			_ = _geometry.Value;
			bool twoPane = _twoPane.Value;
			bool detailOpen = _detailOpen.Value;
			float w = (float)_windowDp.Width;

			var box = new Box();
			((ComposableNode)box).Modifier = BuildNodeModifier() ?? Modifier.Companion.FillMaxSize();

			if (twoPane)
			{
				var detailHost = new Box();
				((ComposableNode)detailHost).Modifier = Modifier.Companion
					.AbsoluteOffset(new Dp((w + (float)ListDetail.GapDp) / 2), new Dp(0));
				detailHost.Add(_detailNode!);
				box.Add(_listNode!);
				box.Add(detailHost);
			}
			else if (detailOpen)
			{
				box.Add(_detailNode!);
				// Gold ReplySinglePaneContent: BackHandler { closeDetailScreen() } while the
				// full-screen detail shows.
				box.Add(new BackHandler(() => Sink?.OnEvent(EventIds.DetailClosed), enabled: true));
			}
			else
			{
				box.Add(_listNode!);
			}

			box.Render(composer);
		}
	}
}
#endif
