using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.WPF;
using Application = System.Windows.Application;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	public abstract class MauiWPFApplication : Application, IPlatformApplication
	{
		protected abstract MauiApp CreateMauiApp();


		protected override void OnStartup(StartupEventArgs args)
		{
			base.OnStartup(args);

			IPlatformApplication.Current = this;
			var mauiApp = CreateMauiApp();

			var rootContext = new WPFMauiContext(mauiApp.Services);

			var applicationContext = rootContext.MakeApplicationScope(this);

			Services = applicationContext.Services;

			Application = Services.GetRequiredService<IApplication>();

			// Wire up ApplicationHandler (replaces removed SetApplicationHandler API)
			var appHandler = new Microsoft.Maui.Handlers.WPF.ApplicationHandler();
			appHandler.SetMauiContext(applicationContext);
			appHandler.SetVirtualView(Application);

			// Apply platform theme to MAUI Application so AppThemeBinding resolves correctly
			ThemeManager.ApplyThemeToApplication();

			this.CreatePlatformWindow(Application, args);
		}

		public static new MauiWPFApplication Current => (MauiWPFApplication)System.Windows.Application.Current;

		public IServiceProvider Services { get; protected set; } = null!;

		public IApplication Application { get; protected set; } = null!;
	}
}
