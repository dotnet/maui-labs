namespace CometTrackizerApp;

public class TrackizerCometApp : CometApp
{
	public TrackizerCometApp()
	{
		Body = CreateRootView;
	}

	public static View CreateRootView() => new AppRoot();
}

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

#if DEBUG
		builder.UseCometSampleDebugHost(TrackizerCometApp.CreateRootView);
#else
		builder.UseCometApp<TrackizerCometApp>();
#endif

		builder.ConfigureFonts(fonts =>
		{
			fonts.AddFont("Inter-Regular.ttf", "InterRegular");
		});

		RemoveBordersFromEntry();

#if DEBUG
		builder.EnableSampleRuntimeDebugging();
#endif

		return builder.Build();
	}

	static void RemoveBordersFromEntry()
	{
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("Borderless", (handler, view) =>
		{
#if IOS || MACCATALYST
			handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;
			handler.PlatformView.Layer.BorderWidth = 0;
			handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
			handler.PlatformView.TintColor = UIKit.UIColor.White;
#endif
		});
	}
}
