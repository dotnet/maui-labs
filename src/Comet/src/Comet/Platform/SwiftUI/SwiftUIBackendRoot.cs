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

			var root = (ISwiftUINativeNode)CometBackendBridge.Materialize(view, _context);
			var controller = CometSwiftUIHost.HostController(root.Native);

			if (UseYogaLayout)
			{
				_layoutRoot = view.HasContent ? view.GetView() : view;
				RunLayout();
				// Reflow after each reactive flush: content whose size changed (e.g. a bound
				// label) is re-measured and the tree re-arranged.
				Comet.Reactive.ReactiveScheduler.AfterFlush += RunLayout;
			}

			return controller;
		}

		void RunLayout()
		{
			if (_layoutRoot is null)
				return;
			var bounds = UIScreen.MainScreen.Bounds;
			CometBackendLayoutEngine.Layout(_layoutRoot,
				new Microsoft.Maui.Graphics.Size(bounds.Width, bounds.Height));
		}
	}
}
#endif
