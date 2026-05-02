using BaristaNotes.Styles;
using CometBaristaNotes.Services;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Media;
using Fonts;
using Microsoft.Extensions.Configuration;
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;

namespace CometBaristaNotes;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		builder.UseCometApp<BaristaApp>()
			.UseMauiCommunityToolkit();

		// Load embedded appsettings.json into IConfiguration
		var assembly = typeof(MauiProgram).Assembly;
		var configStream = assembly.GetManifestResourceStream("CometBaristaNotes.appsettings.json");
		if (configStream is not null)
		{
			var config = new ConfigurationBuilder()
				.AddJsonStream(configStream)
				.Build();
			builder.Configuration.AddConfiguration(config);
			builder.Services.AddSingleton<IConfiguration>(config);
		}

		// Configure platform-specific handler customizations
		builder.ConfigureMauiHandlers(handlers =>
		{
			ModifyEntrys();
		});

#if DEBUG
		builder.AddMauiDevFlowAgent();
#endif

		builder.ConfigureFonts(fonts =>
		{
			fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			fonts.AddFont("Manrope-Regular.ttf", "Manrope");
			fonts.AddFont("Manrope-SemiBold.ttf", "ManropeSemibold");
			fonts.AddFont("MaterialSymbols.ttf", MaterialSymbolsFont.FontFamily);
			fonts.AddFont("coffee-icons.ttf", "coffee-icons");
		});

		// Register singleton data store with SQLite persistence
		var store = new SqliteDataStore();
		builder.Services.AddSingleton<IDataStore>(store);
		builder.Services.AddSingleton<IShotService>(store);
		builder.Services.AddSingleton<IBeanService>(store);
		builder.Services.AddSingleton<IBagService>(store);
		builder.Services.AddSingleton<IEquipmentService>(store);
		builder.Services.AddSingleton<IUserProfileService>(store);
		builder.Services.AddSingleton<IRatingService>(store);

		// Legacy alias for InMemoryDataStore.Instance fallback
		InMemoryDataStore.Instance = store;

		// Feedback, theme, and data change notification services
		builder.Services.AddSingleton<IFeedbackService, FeedbackService>();
		builder.Services.AddSingleton<IThemeService, ThemeService>();
		var notifier = new DataChangeNotifier();
		builder.Services.AddSingleton<IDataChangeNotifier>(notifier);
		store.DataChangeNotifier = notifier;

		// Register AI services (real implementations)
		builder.Services.AddSingleton<IAIAdviceService, AIAdviceService>();
		builder.Services.AddSingleton<IVisionService, MockVisionService>();

		// Register navigation and voice command services
		builder.Services.AddSingleton<INavigationRegistry, NavigationRegistry>();
		builder.Services.AddSingleton<IVoiceCommandService, VoiceCommandService>();

		// Explicit ISpeechToText registration — UseMauiCommunityToolkit() should
		// register this, but Comet's debug host path may skip it.
		builder.Services.AddSingleton<ISpeechToText>(SpeechToText.Default);
		builder.Services.AddSingleton<ISpeechRecognitionService, SpeechRecognitionService>();

		var app = builder.Build();
		ServiceHelper.Services = app.Services;
		return app;
	}

	private static void ModifyEntrys()
	{
#if IOS || MACCATALYST
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoBorder", (handler, view) => {
			// Remove border
			handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;

			// Optional: transparent background
			handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;

			// Optional: add a tiny left padding so text isn't flush
			handler.PlatformView.LeftView = new UIKit.UIView(new CoreGraphics.CGRect(0, 0, 4, 0));
			handler.PlatformView.LeftViewMode = UIKit.UITextFieldViewMode.Always;
		});

		Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("NoBorder", (handler, view) => {
			// Remove border + make background transparent
			handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
			handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;

			// Optional: add a tiny left padding so text isn't flush to the edge
			handler.PlatformView.LeftView = new UIKit.UIView(new CoreGraphics.CGRect(0, 0, 4, 0));
			handler.PlatformView.LeftViewMode = UIKit.UITextFieldViewMode.Always;
		});
#endif

#if ANDROID
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", (handler, view) =>
		{
			// Remove background/underline + any focus tint
			handler.PlatformView.Background = null;
			handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
			handler.PlatformView.BackgroundTintList =
				Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);

			// Optional: tweak padding so text isn't cramped
			handler.PlatformView.SetPadding(0, handler.PlatformView.PaddingTop, 0, handler.PlatformView.PaddingBottom);
		});

		Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("NoUnderline", (handler, view) =>
		{
			var pv = handler.PlatformView;

			// Remove default underline / background & tints
			pv.Background = null;
			pv.SetBackgroundColor(Android.Graphics.Color.Transparent);
			pv.BackgroundTintList =
				Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);

			// Optional: tighten side padding so text aligns with other controls
			pv.SetPadding(0, 0, 0, 0);
		});
#endif
	}
}
