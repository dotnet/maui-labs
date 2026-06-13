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

		public SwiftUIBackendRoot(IServiceProvider services)
			=> _context = new BackendContext(services);

		/// <summary>When true, the C# Yoga engine computes layout and the SwiftUI nodes are
		/// positioned absolutely from the computed frames. Default false (native layout).</summary>
		public bool UseYogaLayout { get; set; }

		public UIViewController CreateController(View view)
		{
			var root = (ISwiftUINativeNode)CometBackendBridge.Materialize(view, _context);
			var controller = CometSwiftUIHost.HostController(root.Native);

			if (UseYogaLayout)
			{
				var rootView = view.HasContent ? view.GetView() : view;
				var bounds = UIScreen.MainScreen.Bounds;
				CometBackendLayoutEngine.Layout(rootView,
					new Microsoft.Maui.Graphics.Size(bounds.Width, bounds.Height));
			}

			return controller;
		}
	}
}
#endif
