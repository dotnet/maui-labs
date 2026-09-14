#nullable enable
#if IOS
using System;
using Comet.Backend;
using Comet.SwiftUI.Interop;
using UIKit;

namespace Comet.Platform.SwiftUI
{
	/// <summary>
	/// Hosts a Comet view tree as a SwiftUI composition: materializes the view into the
	/// retained <see cref="SwiftUINode"/> tree and returns the hosting
	/// <see cref="UIViewController"/> (a <c>UIHostingController</c>) to set as the window root.
	/// </summary>
	public sealed class SwiftUIBackendRoot
	{
		readonly BackendContext _context;
		View? _layoutRoot;

		public SwiftUIBackendRoot(IServiceProvider services)
			=> _context = new BackendContext(services);

		/// <summary>When true, the C# Yoga engine computes layout and the SwiftUI nodes are
		/// positioned absolutely from the computed frames. Default false (native layout).</summary>
		public bool UseYogaLayout { get; set; }

		public UIViewController CreateController(View view)
		{
			// Let the dev agent's /ui/screenshot endpoint snapshot the rendered SwiftUI window.
			Comet.DevTools.CometDevRegistry.ScreenshotProvider = () => CometSwiftUIHost.ScreenshotPng()?.ToArray();

			// Drive Comet's animation engine (view.Animate/FadeTo/…) from CADisplayLink —
			// the iOS twin of the Compose backend's ChoreographerTicker.
			Backend.CometAnimationDriver.Initialize(new DisplayLinkTicker());

			var root = (ISwiftUINativeNode)CometBackendBridge.Materialize(view, _context);
			var controller = CometSwiftUIHost.HostController(root.Native);

			if (UseYogaLayout)
			{
				_logicalRoot = view;
				RunLayout();
				// Reflow after each reactive flush: content whose size changed (e.g. a bound
				// label) is re-measured and the tree re-arranged.
				Comet.Reactive.ReactiveScheduler.AfterFlush += RunLayout;
			}

			return controller;
		}

		/// <summary>The logical root: the layout target is re-resolved per pass because a
		/// (hot) reload rebuilds the view tree (read-only BuiltView — building views on a
		/// background flush thread is not safe). The iOS twin of the ComposeBackendRoot fix.</summary>
		View? _logicalRoot;

		void RunLayout()
		{
			_layoutRoot = _logicalRoot?.BuiltView ?? _logicalRoot;
			if (_layoutRoot is null)
				return;
			var bounds = UIScreen.MainScreen.Bounds;
			var size = new Microsoft.Maui.Graphics.Size(bounds.Width, bounds.Height);
			// Per-root reactive window contract (adaptive size-class UI). Screen bounds are
			// only re-read per layout pass — real resize observation (viewWillTransition /
			// scene geometry) lands with the first adaptive iOS sample.
			CometWindowMetrics.Shared.Update(size);
			// Safe area from the key window (notch / home indicator), same per-pass cadence.
			if (UIApplication.SharedApplication.KeyWindow is { } window)
			{
				var sa = window.SafeAreaInsets;
				CometWindowMetrics.Shared.UpdateSafeArea(new Microsoft.Maui.Thickness(
					sa.Left, sa.Top, sa.Right, sa.Bottom));
			}
			CometBackendLayoutEngine.Layout(_layoutRoot, size);
		}
	}
}
#endif
