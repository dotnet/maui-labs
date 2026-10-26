#nullable enable
#if IOS
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;
using UIKit;

namespace Comet.Platform.SwiftUI
{
	/// <summary>Renders a Comet <see cref="Comet.Drawer"/> as a SwiftUI sliding panel (the iOS
	/// counterpart of <see cref="ComposeDrawerNode"/>; SwiftUI has no built-in modal nav drawer, so
	/// the shim composes content + scrim + a left-edge panel). Owns its two children
	/// (<see cref="IBackendManagesOwnContent"/>) and lays each out with the shared Yoga engine —
	/// content at full screen, the panel at the sheet width.</summary>
	sealed class SwiftUIDrawerNode : ICometBackendNode, IBackendManagesOwnContent, ISwiftUINativeNode
	{
		readonly Drawer _drawer;
		readonly BackendContext _context;
		readonly CometNode _native;
		ICometEventSink? _sink;

		public CometNode Native => _native;

		public SwiftUIDrawerNode(Drawer drawer, BackendContext context)
		{
			_drawer = drawer;
			_context = context;
			_native = CometSwiftUIHost.MakeNode("drawer");
			CometSwiftUIHost.SetTapHandler(_native, OnDismiss); // scrim tap closes the drawer
			BuildContent();
		}

		void OnDismiss() => _sink?.OnEvent(EventIds.DrawerClosed);

		void BuildContent()
		{
			var content = (ISwiftUINativeNode)CometBackendBridge.Materialize(_drawer.Content, _context, _drawer);
			var side = (ISwiftUINativeNode)CometBackendBridge.Materialize(_drawer.Side, _context, _drawer);
			CometSwiftUIHost.InsertChild(_native, 0, content.Native);
			CometSwiftUIHost.InsertChild(_native, 1, side.Native);

			var b = UIScreen.MainScreen.Bounds;
			double w = b.Width, h = b.Height;
			double panel = System.Math.Min(320, w * 0.85);
			CometBackendLayoutEngine.Layout(_drawer.Content, new Size(w, h));
			CometBackendLayoutEngine.Layout(_drawer.Side, new Size(panel, h));
		}

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Drawer_IsOpen)
				CometSwiftUIHost.SetBool(_native, "draweropen", value.AsBool);
		}

		public void SetEventSink(ICometEventSink? sink) => _sink = sink;

		// Content is managed internally.
		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }
		public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;
		public void Arrange(Rect frame) { }
		public void Dispose() { }
	}
}
#endif
