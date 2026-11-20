#nullable enable
#if IOS
using Comet.Backend;
using Comet.SwiftUI.Interop;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.SwiftUI
{
	/// <summary>Renders a Comet <see cref="Comet.AlertDialog"/> as a native SwiftUI <c>.alert</c> —
	/// the iOS counterpart of Compose's Material <c>AlertDialog</c>. SwiftUI's <c>.alert</c> takes a
	/// message string + simple buttons (not view slots), so the dialog's <c>Text</c>,
	/// <c>ConfirmButton</c>, and optional <c>DismissButton</c> are flattened to native alert
	/// content. <c>IsOpen</c> drives presentation; native dismissal routes back as
	/// <c>DialogDismissed</c>, while each button invokes its source Comet action.</summary>
	sealed class SwiftUIAlertDialogNode : ICometBackendNode, IBackendManagesOwnContent, ISwiftUINativeNode
	{
		readonly CometNode _native;
		readonly AlertDialogActionDispatcher _actions;
		readonly Comet.DevTools.NativeAlertSemanticActions _semanticActions;
		ICometEventSink? _sink;

		public CometNode Native => _native;

		public SwiftUIAlertDialogNode(AlertDialog dialog)
		{
			_native = CometSwiftUIHost.MakeNode("alert");
			_actions = new AlertDialogActionDispatcher(dialog);
			_semanticActions = new Comet.DevTools.NativeAlertSemanticActions(
				dialog, _actions, OnPresentationDismissed);
			UpdateContent(dialog);
			CometSwiftUIHost.SetDialogConfirmHandler(_native, _actions.InvokeConfirm);
			CometSwiftUIHost.SetDialogDismissActionHandler(_native, _actions.InvokeDismiss);
			CometSwiftUIHost.SetDialogDismissHandler(
				_native, _semanticActions.OnNativePresentationDismissed);
		}

		void UpdateContent(AlertDialog dialog)
		{
			_semanticActions.UpdateOwner(dialog);
			CometSwiftUIHost.SetString(_native, "dialogtitle", _actions.Title);
			CometSwiftUIHost.SetString(_native, "dialogmessage", _actions.Message);
			CometSwiftUIHost.SetString(_native, "dialogbutton", _actions.ConfirmLabel);
			CometSwiftUIHost.SetString(_native, "dialogdismissbutton", _actions.DismissLabel ?? string.Empty);
			CometSwiftUIHost.SetBool(_native, "dialoghasdismissbutton", _actions.DismissLabel is not null);
		}

		void OnPresentationDismissed() => _sink?.OnEvent(EventIds.DialogDismissed);

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Dialog_IsOpen)
			{
				_semanticActions.UpdateOpenState(value.AsBool);
				CometSwiftUIHost.SetBool(_native, "dialogopen", value.AsBool);
			}
		}

		public void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is AlertDialog dialog)
				UpdateContent(dialog);
		}

		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }
		public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;
		public void Arrange(Rect frame) { }
		public void SetEventSink(ICometEventSink? sink) => _sink = sink;
		public void Dispose() => _semanticActions.Dispose();
	}
}
#endif
