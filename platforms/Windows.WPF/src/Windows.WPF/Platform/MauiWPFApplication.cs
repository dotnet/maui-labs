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

			// Override MAUI's default DeviceInfo with our WPF implementation
			// Must happen AFTER CreateMauiApp() because Build() sets the default
			OverrideEssentialsDefaults();

			Application = Services.GetRequiredService<IApplication>();

			// Wire up ApplicationHandler (replaces removed SetApplicationHandler API)
			var appHandler = new Microsoft.Maui.Handlers.WPF.ApplicationHandler();
			appHandler.SetMauiContext(applicationContext);
			appHandler.SetVirtualView(Application);

			// Apply platform theme to MAUI Application so AppThemeBinding resolves correctly
			ThemeManager.ApplyThemeToApplication();

			this.CreatePlatformWindow(Application, args);
		}

		/// <summary>
		/// Override the static Essentials defaults that MAUI sets during Build().
		/// This ensures OnIdiom, OnPlatform, etc. resolve correctly for WPF.
		/// </summary>
		void OverrideEssentialsDefaults()
		{
			try
			{
				// DeviceInfo.currentImplementation — field set by MAUI's Build()
				// Must override with WPF implementation so OnIdiom Desktop=X works
				var deviceInfoType = typeof(Microsoft.Maui.Devices.DeviceInfo);
				var field = deviceInfoType.GetField("currentImplementation",
					System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
				if (field != null)
				{
					// Get WPFDeviceInfo from DI (registered by Essentials project)
					var wpfDeviceInfo = Services.GetService(typeof(Microsoft.Maui.Devices.IDeviceInfo));
					if (wpfDeviceInfo != null && wpfDeviceInfo.GetType().Name.Contains("WPF"))
						field.SetValue(null, wpfDeviceInfo);
					else
						field.SetValue(null, new WPFDeviceInfoFallback());
				}
			}
			catch { }
		}

		/// <summary>
		/// Minimal IDeviceInfo for WPF that returns Desktop idiom.
		/// Used as fallback when the Essentials project implementation isn't available via DI.
		/// </summary>
		sealed class WPFDeviceInfoFallback : Microsoft.Maui.Devices.IDeviceInfo
		{
			public string Model => "WPF";
			public string Manufacturer => "Microsoft";
			public string Name => Environment.MachineName;
			public string VersionString => Environment.OSVersion.VersionString;
			public Version Version => Environment.OSVersion.Version;
			public Microsoft.Maui.Devices.DevicePlatform Platform => Microsoft.Maui.Devices.DevicePlatform.WinUI;
			public Microsoft.Maui.Devices.DeviceIdiom Idiom => Microsoft.Maui.Devices.DeviceIdiom.Desktop;
			public Microsoft.Maui.Devices.DeviceType DeviceType => Microsoft.Maui.Devices.DeviceType.Physical;
		}

		public static new MauiWPFApplication Current => (MauiWPFApplication)System.Windows.Application.Current;

		public IServiceProvider Services { get; protected set; } = null!;

		public IApplication Application { get; protected set; } = null!;
	}
}
