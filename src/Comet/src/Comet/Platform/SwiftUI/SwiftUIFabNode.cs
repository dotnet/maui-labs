#nullable enable
#if IOS
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.SwiftUI
{
	/// <summary>iOS has no Material FAB, so the native idiom for a floating action button is an
	/// <c>HStack</c>(icon + label) with a capsule background and a tap — composed from native SwiftUI
	/// primitives (not a styled cross-platform pill). This owns its content (icon + label) and styles
	/// itself as the FAB. NOTE: pending on-device (simulator) verification — the Android side drives
	/// the real Material <c>FloatingActionButton</c>.</summary>
	sealed class SwiftUIFabNode : ICometBackendNode, IBackendManagesOwnContent, ISwiftUINativeNode
	{
		readonly Fab _fab;
		readonly BackendContext _context;
		readonly CometNode _native;
		ICometEventSink? _sink;

		public CometNode Native => _native;

		public SwiftUIFabNode(Fab fab, BackendContext context)
		{
			_fab = fab;
			_context = context;
			_native = CometSwiftUIHost.MakeNode("hstack");
			CometSwiftUIHost.SetTapHandler(_native, () => _sink?.OnEvent(EventIds.Clicked));
			BuildContent();
		}

		void BuildContent()
		{
			var icon = (ISwiftUINativeNode)CometBackendBridge.Materialize(_fab.IconView, _context, _fab);
			var label = (ISwiftUINativeNode)CometBackendBridge.Materialize(_fab.LabelView, _context, _fab);
			CometSwiftUIHost.InsertChild(_native, 0, icon.Native);
			CometSwiftUIHost.InsertChild(_native, 1, label.Native);

			if (_fab.ContainerColor is { } c)
				CometSwiftUIHost.SetColor(_native, "background", ToArgb(c));

			// Capsule: corner radius = half the height; standard FAB content padding.
			float r = (float)(_fab.Height / 2);
			CometSwiftUIHost.SetDouble(_native, "corner.tl", r);
			CometSwiftUIHost.SetDouble(_native, "corner.tr", r);
			CometSwiftUIHost.SetDouble(_native, "corner.br", r);
			CometSwiftUIHost.SetDouble(_native, "corner.bl", r);
			CometSwiftUIHost.SetDouble(_native, "padding", 16);
		}

		static uint ToArgb(Color c) =>
			((uint)(c.Alpha * 255) << 24) | ((uint)(c.Red * 255) << 16) |
			((uint)(c.Green * 255) << 8) | (uint)(c.Blue * 255);

		public void ApplyProperty(PropertyId id, in PropertyValue value) { }
		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }
		public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;
		public void Arrange(Rect frame) { }
		public void SetEventSink(ICometEventSink? sink) => _sink = sink;
		public void Dispose() { }
	}
}
#endif
