#nullable enable
#if ANDROID
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Android.Content;
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using AndroidX.Compose.UI.Platform;
using Comet.Backend;
using Comet.DevTools;

namespace Comet.Platform.Compose
{
	/// <summary>
	/// Hosts a Comet view tree as a single root Jetpack Compose composition. Builds the
	/// retained <see cref="ComposeNode"/> tree from the Comet view via
	/// <see cref="CometBackendBridge"/>, then drives it through one
	/// <see cref="ComposeView"/> set as the activity/root content.
	/// </summary>
	public sealed class ComposeBackendRoot : IDisposable
	{
		static ThemeConfigurationCallbacks? themeCallbacks;
		static readonly ConditionalWeakTable<global::Android.Views.View, ImeInsetListener> InsetOwners = new();
		readonly BackendContext _context;
		readonly List<ICometBackendNode> _nodes = new();
		readonly RootCallbackLifetime _callbacks = new();
		ComposeView? _composeView;
		global::Android.Views.View? _decor;
		ImeInsetListener? _insetListener;
		Func<CometDevRegistry.NativeWindowMetrics?>? _metricsProvider;
		bool _created;
		ComposeNode? _root;
		View? _layoutRoot;
		Microsoft.Maui.Graphics.Size _availableDp;
		Microsoft.Maui.Thickness? _nativeSafeAreaPixels;

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
				if (!InsetOwners.TryGetValue(v, out var current) || !ReferenceEquals(current, this))
					return insets;
				var result = insets;
				_owner._callbacks.Run(() =>
				{
					var ime = insets.GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.Ime()).Bottom;
					_owner.SetImeInsetDp(ime / ComposeNode.Density);

					// Safe area (per-root reactive contract): system bars + display cutout — the
					// insets edge-to-edge content must clear. Equality-gated, safe per dispatch.
					var bars = insets.GetInsets(
						AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars()
						| AndroidX.Core.View.WindowInsetsCompat.Type.DisplayCutout());
					_owner._nativeSafeAreaPixels = new Microsoft.Maui.Thickness(
						bars.Left, bars.Top, bars.Right, bars.Bottom);
					float d = ComposeNode.Density;
					Backend.CometWindowMetrics.Shared.UpdateSafeArea(new Microsoft.Maui.Thickness(
						bars.Left / d, bars.Top / d, bars.Right / d, bars.Bottom / d));
					result = AndroidX.Core.View.ViewCompat.OnApplyWindowInsets(v, insets);
				});
				return result;
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
			_callbacks.ThrowIfClosed();
			if (_created)
				throw new InvalidOperationException("A Compose root can own only one native composition.");
			_created = true;
			if (themeCallbacks is null)
			{
				themeCallbacks = new ThemeConfigurationCallbacks();
				global::Android.App.Application.Context.RegisterComponentCallbacks(themeCallbacks);
			}
			var metrics = context.Resources!.DisplayMetrics!;
			ComposeNode.Density = metrics.Density;

			// Drive Comet's animation engine (view.Animate/FadeTo/…) from the vsync
			// Choreographer — without this, animations silently no-op on the node backend.
			Backend.CometAnimationDriver.Initialize(new ChoreographerTicker());

			// A NEW root (activity re-creation) obsoletes every prior registration; without
			// this the registry keeps ghost elements whose disposed views the agent can still
			// resolve (semantic taps/long-presses then silently no-op on dead views).
			Comet.DevTools.CometDevRegistry.Reset();
			using (CometBackendBridge.CollectNodes(_nodes))
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

			var composeView = _composeView = new ComposeView(context);
			composeView.SetContent(_ => WrapContent is null ? _root : WrapContent(_root));

			// Track the view's ACTUAL size (rotation, split-screen, or an OS that still
			// resizes for the keyboard). A change recomputes the available size and
			// schedules a flush so the backend root AND nested own-content hosts
			// (NavigationView) re-lay-out to it.
			composeView.LayoutChange += OnLayoutChange;

			// IME inset via the decor: shrinks the available height so the composer/footer
			// lifts above the soft keyboard (P7). The window itself must do NOTHING for the
			// IME — without AdjustNothing the system falls back to adjustPan and pans the
			// whole window up (pushing the top bar off-screen) on top of our reflow.
			CometDevRegistry.NativeWindowMetricsProvider = null;
			if (context is global::Android.App.Activity activity && activity.Window is { } window)
			{
				window.SetSoftInputMode(global::Android.Views.SoftInput.AdjustNothing);
				if (window.DecorView is { } decor)
				{
					_decor = decor;
					_insetListener = new ImeInsetListener(this);
					InsetOwners.AddOrUpdate(decor, _insetListener);
					AndroidX.Core.View.ViewCompat.SetOnApplyWindowInsetsListener(decor, _insetListener);
					_metricsProvider = () =>
					{
						CometDevRegistry.NativeWindowMetrics? metrics = null;
						_callbacks.Run(() =>
						{
							if (decor.Width > 0 && decor.Height > 0)
								metrics = new CometDevRegistry.NativeWindowMetrics
								{
									Size = new Microsoft.Maui.Graphics.Size(decor.Width, decor.Height),
									SafeAreaInsets = _nativeSafeAreaPixels,
									Units = "physicalPixels",
									Scale = ComposeNode.Density,
									Source = "Window.DecorView bounds and WindowInsetsCompat(systemBars|displayCutout)",
								};
						});
						return metrics;
					};
					CometDevRegistry.NativeWindowMetricsProvider = _metricsProvider;
				}
			}

			return composeView;
		}

		public void Dispose()
		{
			_callbacks.Dispose();
			Comet.Reactive.ReactiveScheduler.AfterFlush -= RunLayout;
			if (_composeView is not null)
				_composeView.LayoutChange -= OnLayoutChange;
			if (_decor is not null && InsetOwners.TryGetValue(_decor, out var current)
				&& ReferenceEquals(current, _insetListener))
			{
				AndroidX.Core.View.ViewCompat.SetOnApplyWindowInsetsListener(_decor, null);
				InsetOwners.Remove(_decor);
			}
			if (ReferenceEquals(CometDevRegistry.NativeWindowMetricsProvider, _metricsProvider))
				CometDevRegistry.NativeWindowMetricsProvider = null;
			_metricsProvider = null;
			_decor = null;
			_insetListener?.Dispose();
			_insetListener = null;
			_logicalRoot = null;
			_layoutRoot = null;
			try
			{
				_composeView?.DisposeComposition();
			}
			finally
			{
				_composeView = null;
				CometBackendBridge.DisposeNodes(_nodes);
				_root = null;
				WrapContent = null;
			}
		}

		void OnLayoutChange(object? sender, global::Android.Views.View.LayoutChangeEventArgs e) =>
			_callbacks.Run(() =>
			{
				var dp = new Microsoft.Maui.Graphics.Size(
					(e.Right - e.Left) / ComposeNode.Density,
					(e.Bottom - e.Top) / ComposeNode.Density);
				if (dp.Width <= 0 || dp.Height <= 0 || dp == _viewDp)
					return;
				_viewDp = dp;
				// Classify the raw window, not its IME-shrunk content area.
				Backend.CometWindowMetrics.Shared.Update(dp);
				RecomputeAvailable();
			});

		sealed class ThemeConfigurationCallbacks : Java.Lang.Object, global::Android.Content.IComponentCallbacks
		{
			public void OnConfigurationChanged(global::Android.Content.Res.Configuration newConfig)
				=> Comet.Styles.ThemeManager.NotifySystemThemeChanged();

			public void OnLowMemory() { }
		}

		void RunLayout() => _callbacks.Run(() =>
		{
			// Re-resolve the layout target: a (hot) reload rebuilds the view tree, so a
			// captured built tree goes stale. Read-only (BuiltView, never GetView) — a
			// flush can run off the main thread and building views there is not safe.
			_layoutRoot = _logicalRoot?.BuiltView ?? _logicalRoot;
			if (_layoutRoot is not null)
				CometBackendLayoutEngine.Layout(_layoutRoot, _availableDp);
		});
	}
}
#endif
