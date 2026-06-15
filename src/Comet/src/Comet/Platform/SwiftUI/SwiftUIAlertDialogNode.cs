#nullable enable
#if IOS
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.SwiftUI
{
	/// <summary>iOS placeholder for <see cref="Comet.AlertDialog"/>. The SwiftUI <c>.alert</c> is a
	/// follow-up; this is an empty own-content node so the shared Jetchat tree still materializes on
	/// iOS without rendering the dialog's slot views inline. Measures to zero and shows nothing.</summary>
	sealed class SwiftUIAlertDialogNode : ICometBackendNode, IBackendManagesOwnContent, ISwiftUINativeNode
	{
		readonly CometNode _native;

		public CometNode Native => _native;

		public SwiftUIAlertDialogNode() => _native = CometSwiftUIHost.MakeNode("vstack");

		public void ApplyProperty(PropertyId id, in PropertyValue value) { }
		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }
		public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;
		public void Arrange(Rect frame) { }
		public void SetEventSink(ICometEventSink? sink) { }
		public void Dispose() { }
	}
}
#endif
