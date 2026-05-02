using System;
namespace Comet
{
	public class ModalView : ContentView
	{

		public static void Dismiss() => PerformDismiss?.Invoke();
		public static Action PerformDismiss
		{
			get => _performDismiss;
			set => _performDismiss = value;
		}

		public static void Present(View view) => PerformPresent?.Invoke(view);
		public static Action<View> PerformPresent
		{
			get => _performPresent;
			set => _performPresent = value;
		}

		// Use WeakReference-backed delegates to avoid leaking views
		static Action _performDismiss;
		static Action<View> _performPresent;

		public static void ClearDelegates()
		{
			_performDismiss = null;
			_performPresent = null;
		}
	}
}
