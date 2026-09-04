#nullable enable
#if ANDROID
using AndroidX.Compose.Runtime;
using Comet.Backend;
using ComposeAlertDialog = AndroidX.Compose.AlertDialog;

namespace Comet.Platform.Compose
{
	/// <summary>Renders a Comet <see cref="Comet.AlertDialog"/> as the real Material 3
	/// <c>AlertDialog</c> widget (the same control the gold-standard Jetchat uses for its
	/// <c>FunctionalityNotAvailablePopup</c>): a modal popup with its own overlay window + scrim,
	/// composed only while the Comet <c>Dialog_IsOpen</c> signal is true. Owns its slot children
	/// (<see cref="IBackendManagesOwnContent"/>) so they're laid out by Material inside the dialog,
	/// not in the parent Yoga tree, and the node measures to zero in its parent's layout.</summary>
	sealed class ComposeAlertDialogNode : ComposeNode, IBackendManagesOwnContent
	{
		AlertDialog _dialog;
		readonly BackendContext _context;
		readonly AndroidX.Compose.MutableState<bool> _open = new(false);
		ComposeNode? _text, _confirm, _title, _dismiss;

		public ComposeAlertDialogNode(AlertDialog dialog, BackendContext context)
		{
			_dialog = dialog;
			_context = context;
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Dialog_IsOpen)
				_open.Value = value.AsBool;
		}

		/// <summary>Re-point at the new AlertDialog; only a hot reload re-materializes the slot
		/// views (text/buttons/title) so the changed code renders.</summary>
		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not AlertDialog dialog)
				return;
			_dialog = dialog;
			if (!isHotReload)
				return;
			_text = _confirm = _title = _dismiss = null;
		}

		void EnsureContent()
		{
			if (_text is not null)
				return;

			// Materialize the slot views once. They carry no Yoga frame, so Material lays them out
			// inside the dialog natively (intrinsic sizing), exactly like Compose's AlertDialog slots.
			_text = (ComposeNode)CometBackendBridge.Materialize(_dialog.Text, _context);
			_confirm = (ComposeNode)CometBackendBridge.Materialize(_dialog.ConfirmButton, _context);
			if (_dialog.Title is not null)
				_title = (ComposeNode)CometBackendBridge.Materialize(_dialog.Title, _context);
			if (_dialog.DismissButton is not null)
				_dismiss = (ComposeNode)CometBackendBridge.Materialize(_dialog.DismissButton, _context);
		}

		public override void Render(IComposer composer)
		{
			// Closed → compose nothing, so the dialog window is absent (Compose tears it down).
			if (!_open.Value)
				return;

			EnsureContent();

			var dialog = new ComposeAlertDialog(
				onDismissRequest: () => Sink?.OnEvent(EventIds.DialogDismissed))
			{
				Text = _text!,
				ConfirmButton = _confirm!,
			};
			if (_title is not null) dialog.Title = _title;
			if (_dismiss is not null) dialog.DismissButton = _dismiss;

			dialog.Render(composer);
		}
	}
}
#endif
