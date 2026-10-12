#nullable enable
using System.ComponentModel;
using Comet.Backend;

namespace Comet
{
	internal sealed class AlertDialogActionDispatcher
	{
		AlertDialog _dialog;
		bool _isOpen;
		bool _actionInvoked;

		public AlertDialogActionDispatcher(AlertDialog dialog)
		{
			_dialog = dialog;
			_isOpen = dialog.IsOpen.Peek();
		}

		public string Title => _dialog.Title is null ? string.Empty : TextOf(_dialog.Title);
		public string Message => TextOf(_dialog.Text);
		public string ConfirmLabel => LabelOf(_dialog.ConfirmButton, "OK");
		public string? ConfirmAutomationId => AutomationIdOf(_dialog.ConfirmButton);
		public string? DismissLabel => _dialog.DismissButton is null
			? null
			: LabelOf(_dialog.DismissButton, "Cancel");
		public string? DismissAutomationId => AutomationIdOf(_dialog.DismissButton);

		public void UpdateOwner(AlertDialog dialog) => _dialog = dialog;

		public void UpdateOpenState(bool isOpen)
		{
			if (isOpen && !_isOpen)
				_actionInvoked = false;
			_isOpen = isOpen;
		}

		public void InvokeConfirm() => InvokeOnce(_dialog.ConfirmButton);
		public void InvokeDismiss() => InvokeOnce(_dialog.DismissButton);

		void InvokeOnce(View? action)
		{
			if (_actionInvoked || action is null)
				return;

			_actionInvoked = true;
			action.OnBackendEvent(Backend.EventIds.Clicked);
		}

		static string TextOf(View view) => (view as Text)?.Value?.CurrentValue ?? string.Empty;
		static string LabelOf(View view, string fallback)
			=> (view as Button)?.Text?.CurrentValue ?? fallback;
		static string? AutomationIdOf(View? view)
			=> string.IsNullOrEmpty(view?.AutomationId) ? null : view.AutomationId;
	}

	// Backend property emission + dismiss write-back for AlertDialog (mirrors Drawer.Backend).
	public partial class AlertDialog
	{
		bool _hooked;

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);

			// Push the open state, and (once) forward future signal changes to the node so toggling
			// IsOpen shows/dismisses the dialog without a full re-render.
			node.ApplyProperty(PropertyIds.Dialog_IsOpen, PropertyValue.From(IsOpen.Peek()));

			if (!_hooked)
			{
				_hooked = true;
				IsOpen.PropertyChanged += OnOpenChanged;
			}
		}

		void OnOpenChanged(object? sender, PropertyChangedEventArgs e)
			=> Node?.ApplyProperty(PropertyIds.Dialog_IsOpen, PropertyValue.From(IsOpen.Peek()));

		protected internal override void OnBackendEvent(Backend.EventId id)
		{
			// Scrim tap / back press dismissed the dialog — reflect it back into the signal.
			if (id == Backend.EventIds.DialogDismissed)
				IsOpen.Value = false;
		}

		protected override void Dispose(bool disposing)
		{
			var slots = disposing ? GetChildren() : null;
			if (disposing && _hooked)
			{
				IsOpen.PropertyChanged -= OnOpenChanged;
				_hooked = false;
			}
			base.Dispose(disposing);
			if (!disposing || slots is null)
				return;

			DisposeOwnedSlots(slots);
			Text = null!;
			ConfirmButton = null!;
			Title = null;
			DismissButton = null;
		}
	}
}
