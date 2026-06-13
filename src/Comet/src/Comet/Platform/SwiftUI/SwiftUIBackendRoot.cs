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

		public UIViewController CreateController(View view)
		{
			var root = (SwiftUINode)CometBackendBridge.Materialize(view, _context);
			return CometSwiftUIHost.HostController(root.Native);
		}
	}
}
#endif
