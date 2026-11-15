using System;
using System.Linq;
using System.Windows;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// Bridges WPF Application lifecycle events to MAUI lifecycle.
	/// </summary>
	/// <remarks>
	/// Window lifecycle (Created, Activated, Deactivated, Stopped, Resumed, Backgrounding and
	/// Destroying) is raised through <see cref="IWindow"/> by the WPF <c>WindowHandler</c> alone, so
	/// that each event fires exactly once. Application-wide WPF activation is reported by the
	/// windows themselves and is not forwarded again here.
	/// </remarks>
	public static class LifecycleManager
	{
		public static void RegisterLifecycleEvents(System.Windows.Application wpfApp, IApplication mauiApp)
		{
			wpfApp.Exit += (s, e) =>
			{
				try
				{
					// Windows that are still open when the app exits are stopped; the tracker
					// ignores windows that already were.
					var trackers = mauiApp.Windows
						.Select(window => (window.Handler as Microsoft.Maui.Handlers.WPF.WindowHandler)?.Lifecycle)
						.OfType<WindowLifecycleTracker>()
						.ToList();
					foreach (var tracker in trackers)
						tracker.Stop();
				}
				catch { }
				ThemeManager.Shutdown();
			};
		}
	}
}
