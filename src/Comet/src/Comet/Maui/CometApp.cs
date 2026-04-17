using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;

namespace Comet
{

	public class CometApp : View, IApplication, IMauiContextHolder
	{
		const string MauiWindowIdKey = "__MAUI_WINDOW_ID__";
		public CometApp()
		{
			CurrentApp = this;
#if __IOS__
			ModalView.PerformPresent = (o) => ThreadHelper.RunOnMainThread(()=> {
				var vc = PresentingViewController;
				vc?.PresentViewController(new Comet.iOS.CometViewController{MauiContext = o.GetMauiContext(),CurrentView = o}, true, null);
			});
			ModalView.PerformDismiss = () => ThreadHelper.RunOnMainThread( ()=> PresentingViewController?.DismissViewController(true, null));
#elif ANDROID

			ModalView.PerformPresent = Comet.Android.Controls.ModalManager.ShowModal;
			ModalView.PerformDismiss = Comet.Android.Controls.ModalManager.DismisModal;
#endif
		}
		public static CometApp CurrentApp { get; protected set; }
		public static CometWindow CurrentWindow { get; protected set; }
		public static IMauiContext MauiContext => CometContext.Current ??
			CurrentWindow?.MauiContext ??
			(CurrentApp as IMauiContextHolder)?.MauiContext;

		public static float DisplayScale => CurrentWindow?.DisplayScale ?? 1;
		List<IWindow> windows = new List<IWindow>();
		public IReadOnlyList<IWindow> Windows => windows;


		IWindow IApplication.CreateWindow(IActivationState activationState)
		{
			((IMauiContextHolder)this).MauiContext = activationState.Context;

			windows.Add(CurrentWindow = new CometWindow
			{
				MauiContext = activationState.Context,
				Content = this,
			});
			return CurrentWindow;
		}

		void IApplication.ThemeChanged()
		{
			// System theme changes are observed here but the framework no longer
			// forcibly swaps the active Comet theme. Apps that want to follow the
			// platform light/dark setting should subscribe to
			// Microsoft.Maui.ApplicationModel.AppInfo.Current.RequestedThemeChanged
			// (or override this method) and call ThemeManager.SetTheme(...) with
			// their own Theme instance. See STYLE_THEME_SPEC.md §6.5.
		}

		IMauiContext IMauiContextHolder.MauiContext { get; set; }

		IReadOnlyList<IWindow> IApplication.Windows => windows;
		void IApplication.OpenWindow(IWindow window)
		{
			if (window is CometWindow cwindow)
				OpenWindow(cwindow);
		}

		public virtual void OpenWindow(CometWindow window)
		{


			var state = new PersistedState
			{
				[MauiWindowIdKey] = window.Id
			};

			ViewHandler?.Invoke(nameof(IApplication.OpenWindow), new OpenWindowRequest(State: state));
		}

		void IApplication.CloseWindow(IWindow window) => ViewHandler?.Invoke(nameof(IApplication.CloseWindow), window);


#if __IOS__
		internal static UIKit.UIViewController PresentingViewController
		{
			get
			{
				var window = UIKit.UIApplication.SharedApplication.ConnectedScenes
					.OfType<UIKit.UIWindowScene>()
					.SelectMany(s => s.Windows)
					.FirstOrDefault(w => w.IsKeyWindow);
				var vc = window?.RootViewController;
				while (vc?.PresentedViewController is not null)
					vc = vc.PresentedViewController;
				return vc;
			}
		}

#endif
		AppTheme IApplication.UserAppTheme => AppTheme.Unspecified;
	}
}
