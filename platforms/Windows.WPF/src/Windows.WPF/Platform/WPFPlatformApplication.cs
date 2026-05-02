#nullable enable
using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// IPlatformApplication implementation for WPF.
	/// Bridges the MAUI application lifecycle to the WPF Application.
	/// </summary>
	public class WPFPlatformApplication : IPlatformApplication
	{
		readonly IApplication _application;
		readonly IServiceProvider _services;

		public WPFPlatformApplication(IApplication application, IServiceProvider services)
		{
			_application = application;
			_services = services;
		}

		public IApplication Application => _application;
		public IServiceProvider Services => _services;

		/// <summary>
		/// Set this as the current platform application.
		/// </summary>
		public static void SetCurrent(IApplication application, IServiceProvider services)
		{
			IPlatformApplication.Current = new WPFPlatformApplication(application, services);
		}
	}
}
