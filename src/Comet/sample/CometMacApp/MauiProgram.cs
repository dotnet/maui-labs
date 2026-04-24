using Comet;
using Microsoft.Maui.Hosting;

namespace CometMacApp;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseCometAppMacOS<MyApp>();
		return builder.Build();
	}
}
