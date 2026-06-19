#nullable enable
#if IOS
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.SwiftUI
{
	/// <summary>iOS has no Material FAB, so the native idiom for a floating action button is an
	/// icon + label row with a capsule background and a tap, composed from native SwiftUI primitives
	/// (the "fab" shim kind). Owns its content; positions + sizes itself from the Yoga frame
	/// (<see cref="Measure"/> reports the content's intrinsic size so the parent can corner-pin it,
	/// <see cref="Arrange"/> pushes the frame down). Content colour is applied to the icon/label
	/// directly since SwiftUI has no LocalContentColor inheritance from the capsule.
	/// <see cref="Comet.Fab.ExtendedSignal"/> drives <c>fabExtended</c> on the native node so the
	/// shim can animate label show/hide.</summary>
	sealed class SwiftUIFabNode : ICometBackendNode, IBackendManagesOwnContent, ISwiftUINativeNode
	{
		// FAB content insets (matches the "fab" shim render: .padding(.horizontal, 16) + HStack spacing 8).
		const double PadH = 16, Gap = 8;

		readonly Fab _fab;
		readonly BackendContext _context;
		readonly CometNode _native;
		ICometEventSink? _sink;

		public CometNode Native => _native;

		public SwiftUIFabNode(Fab fab, BackendContext context)
		{
			_fab = fab;
			_context = context;
			_native = CometSwiftUIHost.MakeNode("fab");
			CometSwiftUIHost.SetTapHandler(_native, () => _sink?.OnEvent(EventIds.Clicked));
			BuildContent();

			// Subscribe to the reactive extended signal so the shim can animate the label.
			if (fab.ExtendedSignal is { } sig)
			{
				CometSwiftUIHost.SetBool(_native, "fabextended", sig.Peek());
				sig.PropertyChanged += (_, __) =>
					CometSwiftUIHost.SetBool(_native, "fabextended", sig.Peek());
			}
			else
			{
				// No reactive signal → use the static extended value.
				CometSwiftUIHost.SetBool(_native, "fabextended", fab.Extended);
			}
		}

		void BuildContent()
		{
			var icon = (ISwiftUINativeNode)CometBackendBridge.Materialize(_fab.IconView, _context, _fab);
			var label = (ISwiftUINativeNode)CometBackendBridge.Materialize(_fab.LabelView, _context, _fab);
			CometSwiftUIHost.InsertChild(_native, 0, icon.Native);
			CometSwiftUIHost.InsertChild(_native, 1, label.Native);

			// The slots materialize INHERITING the FAB's env opacity — and the JumpToBottom FAB starts at
			// Opacity(0) (hidden until scrolled away), so without this the icon/label stay at opacity 0 even
			// after the FAB reactively fades in: the capsule shows but the label is invisible. Reset them to
			// fully opaque — the FAB container's own opacity controls visibility. The iOS twin of the
			// ComposeFabNode slot-opacity-inheritance fix. (Now reachable because iOS honours Opacity.)
			CometSwiftUIHost.SetDouble(icon.Native, "opacity", 1.0);
			CometSwiftUIHost.SetDouble(label.Native, "opacity", 1.0);

			if (_fab.ContainerColor is { } c)
				CometSwiftUIHost.SetColor(_native, "background", ToArgb(c));

			// The icon/label inherit the FAB content colour (no SwiftUI LocalContentColor): tint both.
			if (_fab.ContentColor is { } fc)
			{
				CometSwiftUIHost.SetColor(icon.Native, "textcolor", ToArgb(fc));
				CometSwiftUIHost.SetColor(label.Native, "textcolor", ToArgb(fc));
			}

			// Capsule: corner radius = half the height.
			float r = (float)(_fab.Height / 2);
			CometSwiftUIHost.SetDouble(_native, "corner.tl", r);
			CometSwiftUIHost.SetDouble(_native, "corner.tr", r);
			CometSwiftUIHost.SetDouble(_native, "corner.br", r);
			CometSwiftUIHost.SetDouble(_native, "corner.bl", r);
			CometSwiftUIHost.SetDouble(_native, "padding", PadH);
		}

		static uint ToArgb(Color c) =>
			((uint)(c.Alpha * 255) << 24) | ((uint)(c.Red * 255) << 16) |
			((uint)(c.Green * 255) << 8) | (uint)(c.Blue * 255);

		public void ApplyProperty(PropertyId id, in PropertyValue value) { }
		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }

		// Intrinsic size for the parent's Yoga layout: the real content (icon/label measured via
		// SwiftUI sizeThatFits) + the FAB's content insets. Height is the gold's pinned value.
		// Always measures the full extended width so the parent's Yoga frame can corner-pin the
		// FAB; the FAB visually contracts on iOS by hiding the label.
		public Size Measure(double widthConstraint, double heightConstraint)
		{
			var icon = CometBackendLayoutEngine.Measure(_fab.IconView);
			var label = CometBackendLayoutEngine.Measure(_fab.LabelView);
			// Round the content width up: the shim renders the label single-line (.fixedSize), so any
			// sub-pixel shortfall here would clip the last glyph against the capsule.
			var width = System.Math.Ceiling(PadH * 2 + icon.Width + Gap + label.Width);
			return new Size(width, _fab.Height);
		}

		public void Arrange(Rect frame) =>
			CometSwiftUIHost.SetFrame(_native, frame.X, frame.Y, frame.Width, frame.Height);

		public void SetEventSink(ICometEventSink? sink) => _sink = sink;
		public void Dispose() { }
	}
}
#endif
