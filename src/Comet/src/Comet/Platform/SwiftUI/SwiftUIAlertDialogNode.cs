#nullable enable
#if IOS
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.SwiftUI
{
	/// <summary>Renders a Comet <see cref="Comet.AlertDialog"/> as a native SwiftUI <c>.alert</c> —
	/// the iOS counterpart of Compose's Material <c>AlertDialog</c>. SwiftUI's <c>.alert</c> takes a
	/// message string + simple buttons (not view slots), so the dialog's <c>Text</c> / first
	/// <c>ConfirmButton</c> are flattened to the alert's message + button label. <c>IsOpen</c> drives
	/// presentation; the button / scrim dismiss routes back as <c>DialogDismissed</c>.</summary>
	sealed class SwiftUIAlertDialogNode : ICometBackendNode, IBackendManagesOwnContent, ISwiftUINativeNode
	{
		readonly CometNode _native;
		ICometEventSink? _sink;

		public CometNode Native => _native;

		public SwiftUIAlertDialogNode(AlertDialog dialog)
		{
			_native = CometSwiftUIHost.MakeNode("alert");
			CometSwiftUIHost.SetString(_native, "dialogmessage", MessageOf(dialog.Text));
			CometSwiftUIHost.SetString(_native, "dialogbutton", LabelOf(dialog.ConfirmButton));
			CometSwiftUIHost.SetDialogDismissHandler(_native, () => _sink?.OnEvent(EventIds.DialogDismissed));
		}

		static string MessageOf(View view) => (view as Text)?.Value?.CurrentValue ?? string.Empty;
		static string LabelOf(View view) => (view as Button)?.Text?.CurrentValue ?? "OK";

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Dialog_IsOpen)
				CometSwiftUIHost.SetBool(_native, "dialogopen", value.AsBool);
		}

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
