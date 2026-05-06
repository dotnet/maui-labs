using System;
using System.Windows;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// Bridges WPF Application/Window lifecycle events to MAUI lifecycle.
	/// </summary>
	public static class LifecycleManager
	{
		public static void RegisterLifecycleEvents(System.Windows.Application wpfApp, IApplication mauiApp)
		{
			wpfApp.Activated += (s, e) =>
			{
				try
				{
					// MAUI doesn't have a direct Activated on IApplication,
					// but we can invoke the lifecycle event
					foreach (var window in mauiApp.Windows)
					{
						if (window is Microsoft.Maui.Controls.Window mauiWindow)
						{
							var method = typeof(Microsoft.Maui.Controls.Window).GetMethod("OnActivated",
								System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
							method?.Invoke(mauiWindow, null);
						}
					}
				}
				catch { }
			};

			wpfApp.Deactivated += (s, e) =>
			{
				try
				{
					foreach (var window in mauiApp.Windows)
					{
						if (window is Microsoft.Maui.Controls.Window mauiWindow)
						{
							var method = typeof(Microsoft.Maui.Controls.Window).GetMethod("OnDeactivated",
								System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
							method?.Invoke(mauiWindow, null);
						}
					}
				}
				catch { }
			};

			wpfApp.Exit += (s, e) =>
			{
				try
				{
					foreach (var window in mauiApp.Windows)
					{
						if (window is Microsoft.Maui.Controls.Window mauiWindow)
						{
							var method = typeof(Microsoft.Maui.Controls.Window).GetMethod("OnStopped",
								System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
							method?.Invoke(mauiWindow, null);
						}
					}
				}
				catch { }
				ThemeManager.Shutdown();
			};
		}

		public static void RegisterWindowLifecycleEvents(System.Windows.Window wpfWindow, Microsoft.Maui.Controls.Window mauiWindow)
		{
			wpfWindow.Activated += (s, e) =>
			{
				try
				{
					var method = typeof(Microsoft.Maui.Controls.Window).GetMethod("OnActivated",
						System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
					method?.Invoke(mauiWindow, null);
				}
				catch { }
			};

			wpfWindow.Deactivated += (s, e) =>
			{
				try
				{
					var method = typeof(Microsoft.Maui.Controls.Window).GetMethod("OnDeactivated",
						System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
					method?.Invoke(mauiWindow, null);
				}
				catch { }
			};

			wpfWindow.Closing += (s, e) =>
			{
				try
				{
					var method = typeof(Microsoft.Maui.Controls.Window).GetMethod("OnBackgrounding",
						System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
					method?.Invoke(mauiWindow, null);
				}
				catch { }
			};

			wpfWindow.Closed += (s, e) =>
			{
				try
				{
					var method = typeof(Microsoft.Maui.Controls.Window).GetMethod("OnDestroying",
						System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
					method?.Invoke(mauiWindow, null);
				}
				catch { }
			};
		}
	}
}
