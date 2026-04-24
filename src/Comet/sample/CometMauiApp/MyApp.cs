using Comet;
using Comet.Styles;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;

namespace CometMauiApp
{
	public class MyApp : CometApp
	{
		public MyApp()
		{
			Body = CreateRootView;
		}

		public static View CreateRootView() => new MainPage();

		public static MauiApp CreateMauiApp()
		{
			var builder = MauiApp.CreateBuilder();

#if DEBUG
			builder.UseCometSampleDebugHost(CreateRootView);
#else
			builder.UseCometApp<MyApp>();
#endif

			builder.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

			// Apply the built-in light theme at startup.
			// Defaults.Light includes Material 3 color, typography, spacing, and shape tokens.
			ThemeManager.SetTheme(Defaults.Light);

	#if DEBUG
			builder.EnableSampleRuntimeDebugging();
	#endif

			return builder.Build();
		}
	}
}
