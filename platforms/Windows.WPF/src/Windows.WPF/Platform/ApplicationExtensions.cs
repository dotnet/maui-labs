using System.Windows;
using Maui.Controls.Sample.WPF;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.LifecycleEvents;
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


			//winuiWndow.Activated += WinuiWndow_Activated;
			// TODO: SetWindowHandler was removed in newer MAUI versions — window handler setup needs rework
			// winuiWndow.SetWindowHandler(window, mauiContext);

			//applicationContext.Services.InvokeLifecycleEvents<WindowsLifecycle.OnWindowCreated>(del => del(winuiWndow));

			winuiWndow.Show();

			//void WinuiWndow_Activated(object? sender, System.EventArgs e)
			//{
			//	WPFDispatcher.ReplacHack = winuiWndow.Dispatcher;
			//	winuiWndow.SetWindowHandler(window, mauiContext);
			//	winuiWndow.Activated -= WinuiWndow_Activated;
			//}
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