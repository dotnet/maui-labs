using Microsoft.Extensions.Logging;
using Microsoft.Maui.Chat.Controls;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent;
#endif

namespace ChatControls.Sample;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseChatControls()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.AddMauiDevFlowAgent();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
