using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Windows.WPF;
using PlatformView = System.Windows.Application;

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ApplicationHandler : ElementHandler<IApplication, System.Windows.Application>
	{

		internal const string TerminateCommandKey = "Terminate";

		public static IPropertyMapper<IApplication, ApplicationHandler> Mapper = new PropertyMapper<IApplication, ApplicationHandler>(ElementMapper)
		{
		};

		public static CommandMapper<IApplication, ApplicationHandler> CommandMapper = new(ElementCommandMapper)
		{
			[TerminateCommandKey] = MapTerminate,
			[nameof(IApplication.OpenWindow)] = MapOpenWindow,
			[nameof(IApplication.CloseWindow)] = MapCloseWindow,
		};

		ILogger<ApplicationHandler>? _logger;

		public ApplicationHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public ApplicationHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public ApplicationHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		ILogger? Logger =>
			_logger ??= MauiContext?.Services.GetService<ILogger<ApplicationHandler>>();

		protected override PlatformView CreatePlatformElement()
		{
			var app = MauiContext?.Services.GetService<PlatformView>() ?? throw new InvalidOperationException($"MauiContext did not have a valid application.");

			// Wire lifecycle events
			LifecycleManager.RegisterLifecycleEvents(app, VirtualView);

			return app;
		}

		public static void MapTerminate(ApplicationHandler handler, IApplication application, object? args)
		{
			handler.PlatformView?.Shutdown();
		}

		public static void MapOpenWindow(ApplicationHandler handler, IApplication application, object? args)
		{
			// Multi-window support: create a new WPF Window for the MAUI Window
			if (args is IWindow mauiWindow && handler.MauiContext != null)
			{
				try
				{
					var newWindow = new System.Windows.Window
					{
						Title = mauiWindow.Title ?? "Window",
						Width = double.IsNaN(mauiWindow.Width) || mauiWindow.Width < 0 ? 800 : mauiWindow.Width,
						Height = double.IsNaN(mauiWindow.Height) || mauiWindow.Height < 0 ? 600 : mauiWindow.Height,
						Content = new WindowRootViewContainer(),
					};

					if (mauiWindow.Content != null)
					{
						var platformContent = Microsoft.Maui.Platform.ElementExtensions.ToPlatform((IElement)mauiWindow.Content, handler.MauiContext);
						if (newWindow.Content is WindowRootViewContainer container)
							container.AddPage((System.Windows.FrameworkElement)platformContent);
					}

					newWindow.Show();
				}
				catch { }
			}
		}

		public static void MapCloseWindow(ApplicationHandler handler, IApplication application, object? args)
		{
			if (args is IWindow window)
			{
				(window.Handler?.PlatformView as System.Windows.Window)?.Close();
			}
		}
	}
}