using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;

namespace Comet
{
	public class ThreadHelper
	{
		public static void SetFireOnMainThread(Action<Action> action) => FireOnMainThread = action;
#if __MACOS__
		// macOS (AppKit) has no MAUI MainThread implementation.
		// Use NSApplication for main-thread dispatch.
		static Action<Action> FireOnMainThread = action =>
		{
			if (Foundation.NSThread.IsMain)
				action();
			else
				AppKit.NSApplication.SharedApplication.InvokeOnMainThread(action);
		};
#else
		static Action<Action> FireOnMainThread = MainThread.BeginInvokeOnMainThread;
#endif
		public static void RunOnMainThread(Action action) => FireOnMainThread.Invoke(action);
	}
}