#nullable enable
#if IOS
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.SwiftUI
{
	/// <summary>iOS no-op twin of <see cref="ComposeSelectorPanelNode"/>. The expandable input-selector
	/// panel is an Android-first (gold Jetchat) feature, so on iOS the panel never expands: the node
	/// hosts an empty native view, ignores the selector, and measures to zero — keeping the shared view
	/// tree valid without reserving any space.</summary>
	sealed class SwiftUISelectorPanelNode : ICometBackendNode, IBackendManagesOwnContent, ISwiftUINativeNode
	{
		readonly CometNode _native;

		public CometNode Native => _native;

		public SwiftUISelectorPanelNode(SelectorPanel panel, BackendContext context)
			=> _native = CometSwiftUIHost.MakeNode("vstack");

		public void ApplyProperty(PropertyId id, in PropertyValue value) { }
		public void SetEventSink(ICometEventSink? sink) { }

		// Content is managed internally (there is none on iOS).
		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }
		public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;
		public void Arrange(Rect frame) { }
		public void Dispose() { }
	}
}
#endif
