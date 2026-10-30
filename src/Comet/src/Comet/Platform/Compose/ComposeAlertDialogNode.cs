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
		readonly AndroidX.Compose.MutableState<int> _contentVersion = new(0);
		ComposeNode? _text, _confirm, _title, _dismiss;
		OwnedContentGeneration? _generation;
		bool _disposed;

		public ComposeAlertDialogNode(AlertDialog dialog, BackendContext context)
		{
			_dialog = dialog;
			_context = context;
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Dialog_IsOpen)
			{
				_open.Value = value.AsBool;
				if (!value.AsBool)
					ReleaseContent();
			}
		}

		/// <summary>Re-point at the new AlertDialog and replace its slot generation so changed
		/// text/buttons render even when the dialog remains open across a normal owner transfer.</summary>
		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not AlertDialog dialog)
				return;
			ReleaseContent();
			_dialog = dialog;

			// The open state can remain true across owner replacement. Bump a Compose-observed
			// value so the released generation is rebuilt immediately without a close/reopen.
			_contentVersion.Value++;
		}

		void EnsureContent()
		{
			if (_disposed || _generation is not null)
				return;
			using var hold = Comet.Reactive.ReactiveScheduler.HoldFlushes();

			// Materialize the slot views once. They carry no Yoga frame, so Material lays them out
			// inside the dialog natively (intrinsic sizing), exactly like Compose's AlertDialog slots.
			var generation = new OwnedContentGeneration(_dialog, _context);
			try
			{
				_text = (ComposeNode)generation.Materialize(_dialog.Text);
				_confirm = (ComposeNode)generation.Materialize(_dialog.ConfirmButton);
				if (_dialog.Title is not null)
					_title = (ComposeNode)generation.Materialize(_dialog.Title);
				if (_dialog.DismissButton is not null)
					_dismiss = (ComposeNode)generation.Materialize(_dialog.DismissButton);
				_generation = generation;
			}
			catch
			{
				_text = _confirm = _title = _dismiss = null;
				generation.Dispose();
				throw;
			}
		}

		void ReleaseContent()
		{
			var generation = _generation;
			_generation = null;
			_text = _confirm = _title = _dismiss = null;
			generation?.Dispose();
		}

		public override void Render(IComposer composer)
		{
			_ = _contentVersion.Value;

			// Closed → compose nothing, so the dialog window is absent (Compose tears it down).
			if (_disposed)
				return;
			if (!_open.Value)
			{
				ReleaseContent();
				return;
			}

			EnsureContent();
			if (_text is null || _confirm is null)
				return;

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

		public override void Dispose()
		{
			if (_disposed)
				return;
			_disposed = true;
			ReleaseContent();
			base.Dispose();
		}
	}
}
#endif
