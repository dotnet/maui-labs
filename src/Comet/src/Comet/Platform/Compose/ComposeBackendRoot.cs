#nullable enable
#if ANDROID
using System;
using Android.Content;
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using AndroidX.Compose.UI.Platform;
using Comet.Backend;

namespace Comet.Platform.Compose
{
	/// <summary>
	/// Hosts a Comet view tree as a single root Jetpack Compose composition. Builds the
	/// retained <see cref="ComposeNode"/> tree from the Comet view via
	/// <see cref="CometBackendBridge"/>, then drives it through one
	/// <see cref="ComposeView"/> set as the activity/root content.
	/// </summary>
	public sealed class ComposeBackendRoot
	{
		readonly BackendContext _context;
		ComposeNode? _root;
		View? _layoutRoot;
		Microsoft.Maui.Graphics.Size _availableDp;

		/// <summary>The logical root view: the layout target is re-resolved from it on every
		/// pass because a (hot) reload rebuilds the view tree — a captured built tree would
		/// go stale and the reloaded views would never be laid out.</summary>
		View? _logicalRoot;

		// IME handling: the view's laid-out size and the soft-keyboard inset are tracked
		// separately — under Android 15's forced edge-to-edge the window never resizes for
		// the IME (AdjustResize is a no-op), the keyboard just overlays, so the available
		// height is the view height minus the reported IME inset.
		Microsoft.Maui.Graphics.Size _viewDp;
		double _imeInsetDp;

		void RecomputeAvailable()
		{
			if (_viewDp.Width <= 0 || _viewDp.Height <= 0)
				return;
			var avail = new Microsoft.Maui.Graphics.Size(_viewDp.Width, System.Math.Max(0, _viewDp.Height - _imeInsetDp));
			if (avail == ComposeNode.AvailableSize)
				return;
			ComposeNode.AvailableSize = avail;
			_availableDp = avail;
			Comet.Reactive.ReactiveScheduler.EnsureFlushScheduled();
		}

		internal void SetImeInsetDp(double dp)
		{
			if (System.Math.Abs(dp - _imeInsetDp) < 0.5)
				return;
			_imeInsetDp = dp;
			RecomputeAvailable();
		}

		/// <summary>Observes the IME inset at the decor level via the AndroidX compat
		/// dispatch — the ComposeView consumes child-level insets, so a listener on the
		/// view itself never fires.</summary>
		sealed class ImeInsetListener : Java.Lang.Object, AndroidX.Core.View.IOnApplyWindowInsetsListener
		{
			readonly ComposeBackendRoot _owner;
			public ImeInsetListener(ComposeBackendRoot owner) => _owner = owner;

			public AndroidX.Core.View.WindowInsetsCompat OnApplyWindowInsets(
				global::Android.Views.View v, AndroidX.Core.View.WindowInsetsCompat insets)
			{
				var ime = insets.GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.Ime()).Bottom;
				_owner.SetImeInsetDp(ime / ComposeNode.Density);

				// Safe area (per-root reactive contract): system bars + display cutout — the
				// insets edge-to-edge content must clear. Equality-gated, safe per dispatch.
				var bars = insets.GetInsets(
					AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars()
					| AndroidX.Core.View.WindowInsetsCompat.Type.DisplayCutout());
				float d = ComposeNode.Density;
				Backend.CometWindowMetrics.Shared.UpdateSafeArea(new Microsoft.Maui.Thickness(
					bars.Left / d, bars.Top / d, bars.Right / d, bars.Bottom / d));

				return AndroidX.Core.View.ViewCompat.OnApplyWindowInsets(v, insets);
			}
		}

		public ComposeBackendRoot(IServiceProvider services)
			=> _context = new BackendContext(services);

		/// <summary>When true, the C# Yoga engine computes layout and the Compose nodes are
		/// positioned absolutely from the computed frames. Default false (native layout).</summary>
		public bool UseYogaLayout { get; set; }

		/// <summary>Optional hook to wrap the composed root — e.g. in a <c>MaterialTheme</c> carrying
		/// the app's color scheme — so real Material controls (Button, Icon, ripples) pick up the
		/// theme. The app supplies this since the theme/colors are app-specific, not backend policy.</summary>
		public Func<AndroidX.Compose.ComposableNode, AndroidX.Compose.ComposableNode>? WrapContent { get; set; }

		/// <summary>Materializes <paramref name="view"/> into a Compose tree and returns the
		/// hosting <see cref="ComposeView"/> to set as content.</summary>
		public ComposeView CreateView(Context context, View view)
		{
			var metrics = context.Resources!.DisplayMetrics!;
			ComposeNode.Density = metrics.Density;

			// Drive Comet's animation engine (view.Animate/FadeTo/…) from the vsync
			// Choreographer — without this, animations silently no-op on the node backend.
			Backend.CometAnimationDriver.Initialize(new ChoreographerTicker());

			// A NEW root (activity re-creation) obsoletes every prior registration; without
			// this the registry keeps ghost elements whose disposed views the agent can still
			// resolve (semantic taps/long-presses then silently no-op on dead views).
			Comet.DevTools.CometDevRegistry.Reset();
			_root = (ComposeNode)CometBackendBridge.Materialize(view, _context);

			if (UseYogaLayout)
			{
				_logicalRoot = view;
				_availableDp = new Microsoft.Maui.Graphics.Size(
					metrics.WidthPixels / metrics.Density,
					metrics.HeightPixels / metrics.Density);
				RunLayout();
				// Reflow after each reactive flush (content size changes re-measure + re-arrange).
				Comet.Reactive.ReactiveScheduler.AfterFlush += RunLayout;
			}

			var composeView = new ComposeView(context);
			composeView.SetContent(_ => WrapContent is null ? _root : WrapContent(_root));

			// Track the view's ACTUAL size (rotation, split-screen, or an OS that still
			// resizes for the keyboard). A change recomputes the available size and
			// schedules a flush so the backend root AND nested own-content hosts
			// (NavigationView) re-lay-out to it.
			composeView.LayoutChange += (_, e) =>
			{
				var dp = new Microsoft.Maui.Graphics.Size(
					(e.Right - e.Left) / ComposeNode.Density,
					(e.Bottom - e.Top) / ComposeNode.Density);
				if (dp.Width <= 0 || dp.Height <= 0 || dp == _viewDp)
					return;
				_viewDp = dp;
				// Window metrics track the RAW view size (per-root reactive contract for
				// adaptive size-class UI) — deliberately not the IME-shrunk available size:
				// a soft keyboard must not flip Medium→Compact chrome.
				Backend.CometWindowMetrics.Shared.Update(dp);
				RecomputeAvailable();
			};

			// IME inset via the decor: shrinks the available height so the composer/footer
			// lifts above the soft keyboard (P7). The window itself must do NOTHING for the
			// IME — without AdjustNothing the system falls back to adjustPan and pans the
			// whole window up (pushing the top bar off-screen) on top of our reflow.
			if (context is global::Android.App.Activity activity && activity.Window is { } window)
			{
				window.SetSoftInputMode(global::Android.Views.SoftInput.AdjustNothing);
				if (window.DecorView is { } decor)
					AndroidX.Core.View.ViewCompat.SetOnApplyWindowInsetsListener(decor, new ImeInsetListener(this));
			}

			return composeView;
		}

		void RunLayout()
		{
			// Re-resolve the layout target: a (hot) reload rebuilds the view tree, so a
			// captured built tree goes stale. Read-only (BuiltView, never GetView) — a
			// flush can run off the main thread and building views there is not safe.
			_layoutRoot = _logicalRoot?.BuiltView ?? _logicalRoot;
			if (_layoutRoot is not null)
				CometBackendLayoutEngine.Layout(_layoutRoot, _availableDp);
		}
	}
}
#endif
