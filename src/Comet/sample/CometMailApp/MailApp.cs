namespace CometMailApp;

public class MailApp : CometApp
{
	public MailApp()
	{
		Body = CreateRootView;
	}

	public static View CreateRootView() =>
		NavigationView(new Pages.InboxPage())
			.Title("Inbox");

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

#if DEBUG
		builder.UseCometSampleDebugHost(CreateRootView);
#else
		builder.UseCometApp<MailApp>();
#endif

		builder.ConfigureFonts(fonts =>
		{
			fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
		});

#if DEBUG
		builder.EnableSampleRuntimeDebugging();
#endif

		return builder.Build();
	}
}
