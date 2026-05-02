using System;
using System.Windows;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.LifecycleEvents.WPF;
using Microsoft.Maui.WPF;
using PlatformApplication = System.Windows.Application;
using PlatformWindow = System.Windows.Window;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	public static class ApplicationExtensions
	{
		public static void CreatePlatformWindow(this PlatformApplication platformApplication, IApplication application, StartupEventArgs? args) =>
			platformApplication.CreatePlatformWindow(application, new OpenWindowRequest(new WPFPersistedState(args)));

		public static void CreatePlatformWindow(this PlatformApplication platformApplication, IApplication application, OpenWindowRequest? args)
		{
			if (application.Handler?.MauiContext is not IMauiContext applicationContext)
				return;

			var winuiWndow = new MauiWPFWindow();

			var mauiContext = applicationContext!.MakeWindowScope(winuiWndow, out var windowScope);

			//applicationContext.Services.InvokeLifecycleEvents<WindowsLifecycle.OnMauiContextCreated>(del => del(mauiContext));

			var activationState = args?.State is not null
				? new ActivationState(mauiContext, args.State)
				: new ActivationState(mauiContext);

			var window = application.CreateWindow(activationState);

			// Wire up WindowHandler (replaces removed SetWindowHandler API)
			var windowHandler = new Microsoft.Maui.Handlers.WPF.WindowHandler();
			windowHandler.SetMauiContext(mauiContext);
			windowHandler.SetVirtualView(window);

			// Dispose the per-window DI scope when the window closes so transient
			// services and any IDisposable scoped registrations are released.
			winuiWndow.Closed += (_, _) =>
			{
				try { windowScope.Dispose(); }
				catch { }
			};

			winuiWndow.Show();

			applicationContext.Services.InvokeLifecycleEvents<WPFLifecycle.OnActivated>(del => del(winuiWndow, EventArgs.Empty));
		}
	}

	public class WPFPersistedState : PersistedState
	{
		public WPFPersistedState(StartupEventArgs? startupEventArgs)
		{
			StartupEventArgs = startupEventArgs;
		}

		public StartupEventArgs? StartupEventArgs { get; }
	}

	class WPFActivationState : ActivationState
	{
		public WPFActivationState(IMauiContext context) : base(context)
		{
		}

		public WPFActivationState(IMauiContext context, IPersistedState state) : base(context, state)
		{
		}

		public WPFPersistedState? WPFPersistedState => base.State as WPFPersistedState;
	}
}