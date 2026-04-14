using System;

namespace Microsoft.Maui.LifecycleEvents.WPF
{
	public static class WPFLifecycle
	{
		public delegate void OnActivated(System.Windows.Window window, EventArgs args);
		//public delegate void OnClosed(System.Windows.Window window, System.Windows.WindowEventArgs args);
		//public delegate void OnLaunched(System.Windows.Application application, UI.Xaml.LaunchActivatedEventArgs args);
		//public delegate void OnLaunching(System.Windows.Application application, UI.Xaml.LaunchActivatedEventArgs args);
		//public delegate void OnVisibilityChanged(System.Windows.Window window, System.Windows.WindowVisibilityChangedEventArgs args);
		//public delegate void OnPlatformMessage(System.Windows.Window window, WindowsPlatformMessageEventArgs args);
		//public delegate void OnWindowCreated(System.Windows.Window window);
		//public delegate void OnResumed(System.Windows.Window window);
		//public delegate void OnPlatformWindowSubclassed(System.Windows.Window window, WindowsPlatformWindowSubclassedEventArgs args);

		// Internal events
		internal delegate void OnMauiContextCreated(IMauiContext mauiContext);
	}
}
