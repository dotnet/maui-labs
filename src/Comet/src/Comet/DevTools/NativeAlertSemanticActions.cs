#nullable enable
using System;

namespace Comet.DevTools
{
	/// <summary>Publishes active native alert actions to the DevFlow semantic tree without
	/// materializing fake controls or bypassing the alert's action dispatcher.</summary>
	internal sealed class NativeAlertSemanticActions : IDisposable
	{
		AlertDialog _dialog;
		readonly AlertDialogActionDispatcher _actions;
		readonly Action _onPresentationDismissed;
		CometDevRegistry.SemanticActionRegistration? _confirm;
		CometDevRegistry.SemanticActionRegistration? _dismiss;
		bool _isOpen;
		bool _disposed;
		long _presentation;

		public NativeAlertSemanticActions(
			AlertDialog dialog,
			AlertDialogActionDispatcher actions,
			Action onPresentationDismissed)
		{
			_dialog = dialog;
			_actions = actions;
			_onPresentationDismissed = onPresentationDismissed;
		}

		public void UpdateOwner(AlertDialog dialog)
		{
			_dialog = dialog;
			_actions.UpdateOwner(dialog);
			if (_isOpen)
				Rebuild();
		}

		public void UpdateOpenState(bool isOpen)
		{
			_actions.UpdateOpenState(isOpen);
			if (_isOpen == isOpen)
			{
				// Registry subtree cleanup can invalidate native-action proxies while the
				// retained alert presentation remains open. A later true state must restore
				// discovery rather than trusting only this adapter's cached boolean.
				if (isOpen && !RegistrationsAreActive())
					Rebuild();
				return;
			}
			_isOpen = isOpen;
			if (isOpen)
			{
				_presentation++;
				Rebuild();
			}
			else
				Release();
		}

		public void OnNativePresentationDismissed()
		{
			UpdateOpenState(false);
			_onPresentationDismissed();
		}

		void InvokeConfirm()
		{
			var presentation = _presentation;
			// Dismiss the native alert BEFORE firing the action — the action may pop the
			// page, removing the SwiftUI view that owns the .alert binding.
			DismissInvokedPresentation(presentation);
			// Enqueue the action on the next main-queue turn so SwiftUI processes the
			// dialogOpen=false binding update and dismisses the native alert before the
			// action runs (which may pop the page and orphan the alert view).
			DeferAction(_actions.InvokeConfirm);
		}

		void InvokeDismiss()
		{
			var presentation = _presentation;
			DismissInvokedPresentation(presentation);
			DeferAction(_actions.InvokeDismiss);
		}

		/// <summary>Runs <paramref name="action"/> on the next main-queue turn when a
		/// dispatcher is available (live app). Falls back to synchronous execution in
		/// test environments where no dispatcher is registered.</summary>
		static void DeferAction(Action action)
		{
			if (CometDevRegistry.MainThreadEnqueue is { } enqueue)
				enqueue(action);
			else
				action();
		}

		void DismissInvokedPresentation(long presentation)
		{
			if (!_disposed && _isOpen && presentation == _presentation)
				OnNativePresentationDismissed();
		}

		void Rebuild()
		{
			Release();
			if (_disposed)
				return;

			_confirm = CometDevRegistry.RegisterSemanticAction(
				_dialog,
				_actions.ConfirmLabel,
				_actions.ConfirmAutomationId,
				InvokeConfirm);
			if (_actions.DismissLabel is not null)
			{
				_dismiss = CometDevRegistry.RegisterSemanticAction(
					_dialog,
					_actions.DismissLabel,
					_actions.DismissAutomationId,
					InvokeDismiss);
			}
		}

		void Release()
		{
			_confirm?.Dispose();
			_confirm = null;
			_dismiss?.Dispose();
			_dismiss = null;
		}

		bool RegistrationsAreActive() =>
			_confirm?.IsActive == true &&
			(_actions.DismissLabel is null || _dismiss?.IsActive == true);

		public void Dispose()
		{
			if (_disposed)
				return;
			_disposed = true;
			_isOpen = false;
			Release();
		}
	}
}
