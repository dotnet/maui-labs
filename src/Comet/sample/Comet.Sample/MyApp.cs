using System;
using System.Linq;
using Comet;
using Comet.Samples.Models;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;
using static Comet.CometControls;

namespace Comet.Samples
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

			builder.ConfigureFonts(fonts => {
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("fa_solid.ttf", "FontAwesome");
			});

#if DEBUG
			builder.EnableSampleRuntimeDebugging();
#endif

			return builder.Build();
		}
	}
}
