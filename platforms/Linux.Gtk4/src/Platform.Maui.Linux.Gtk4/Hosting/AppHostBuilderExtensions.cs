using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Animations;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;
using Platform.Maui.Linux.Gtk4.Graphics;
using Platform.Maui.Linux.Gtk4.Handlers;
using Platform.Maui.Linux.Gtk4.Platform;

namespace Platform.Maui.Linux.Gtk4.Hosting;

public static partial class AppHostBuilderExtensions
{
	public static MauiAppBuilder UseMauiAppLinuxGtk4<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
		this MauiAppBuilder builder)
		where TApp : class, IApplication
	{
		builder.UseMauiApp<TApp>();
		builder.SetupDefaults();
		return builder;
	}

	static IMauiHandlersCollection AddMauiControlsHandlers(this IMauiHandlersCollection handlersCollection)
	{
		handlersCollection.AddHandler<Application, ApplicationHandler>();
		handlersCollection.AddHandler<Microsoft.Maui.Controls.Window, WindowHandler>();
		handlersCollection.AddHandler<ContentPage, PageHandler>();
		handlersCollection.AddHandler<Layout, LayoutHandler>();
		handlersCollection.AddHandler<ContentView, ContentViewHandler>();
		handlersCollection.AddHandler<IContentView, ContentViewHandler>();
		handlersCollection.AddHandler<Label, LabelHandler>();
		handlersCollection.AddHandler<Button, ButtonHandler>();
		handlersCollection.AddHandler<Entry, EntryHandler>();
		handlersCollection.AddHandler<Editor, EditorHandler>();
		handlersCollection.AddHandler<CheckBox, CheckBoxHandler>();
		handlersCollection.AddHandler<Switch, SwitchHandler>();
		handlersCollection.AddHandler<Slider, SliderHandler>();
		handlersCollection.AddHandler<ProgressBar, ProgressBarHandler>();
		handlersCollection.AddHandler<ActivityIndicator, ActivityIndicatorHandler>();
		handlersCollection.AddHandler<Image, ImageHandler>();
		handlersCollection.AddHandler<Picker, PickerHandler>();
		handlersCollection.AddHandler<DatePicker, DatePickerHandler>();
		handlersCollection.AddHandler<TimePicker, TimePickerHandler>();
		handlersCollection.AddHandler<Stepper, StepperHandler>();
		handlersCollection.AddHandler<RadioButton, RadioButtonHandler>();
		handlersCollection.AddHandler<SearchBar, SearchBarHandler>();
		handlersCollection.AddHandler<ScrollView, ScrollViewHandler>();
		handlersCollection.AddHandler<Border, BorderHandler>();
#pragma warning disable CS0618
		handlersCollection.AddHandler<Frame, FrameHandler>();
#pragma warning restore CS0618
		handlersCollection.AddHandler<ImageButton, ImageButtonHandler>();
		handlersCollection.AddHandler<WebView, WebViewHandler>();
		handlersCollection.AddHandler<NavigationPage, NavigationPageHandler>();
		handlersCollection.AddHandler<TabbedPage, TabbedPageHandler>();
		handlersCollection.AddHandler<FlyoutPage, FlyoutPageHandler>();
		handlersCollection.AddHandler<Shell, ShellHandler>();

		// Phase 6: Advanced handlers
		handlersCollection.AddHandler<CollectionView, CollectionViewHandler>();
#pragma warning disable CS0618
		handlersCollection.AddHandler<ListView, ListViewHandler>();
		handlersCollection.AddHandler<TableView, TableViewHandler>();
#pragma warning restore CS0618
		handlersCollection.AddHandler<GraphicsView, GraphicsViewHandler>();
		handlersCollection.AddHandler<RefreshView, RefreshViewHandler>();
		handlersCollection.AddHandler<SwipeView, SwipeViewHandler>();
		handlersCollection.AddHandler<CarouselView, CarouselViewHandler>();
		handlersCollection.AddHandler<IndicatorView, IndicatorViewHandler>();

		// BoxView / Shapes
		// Register each shape type individually for reliable handler resolution.
#pragma warning disable CS0618
		handlersCollection.AddHandler<BoxView, BoxViewHandler>();
#pragma warning restore CS0618
		handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Rectangle, ShapeViewHandler>();
		handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Ellipse, ShapeViewHandler>();
		handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Line, ShapeViewHandler>();
		handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Polyline, ShapeViewHandler>();
		handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Polygon, ShapeViewHandler>();
		handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Path, ShapeViewHandler>();

		return handlersCollection;
	}

	static MauiAppBuilder SetupDefaults(this MauiAppBuilder builder)
	{
		// Register AppInfo so AppInfo.RequestedTheme returns GTK's system theme
		GtkAppInfoImplementation.Register();

		builder.Services.AddSingleton<IDispatcherProvider>(svc => new GtkDispatcherProvider());

		// Register GTK alert/dialog handler for DisplayAlert/ActionSheet/Prompt
		GtkAlertManager.Register(builder.Services);

		builder.Services.AddScoped(svc =>
		{
			var provider = svc.GetRequiredService<IDispatcherProvider>();
			if (DispatcherProvider.SetCurrent(provider))
				svc.GetService<ILogger<Dispatcher>>()?.LogWarning("Replaced an existing DispatcherProvider.");

			return Dispatcher.GetForCurrentThread()!;
		});

		builder.Services.RemoveAll<IFontRegistrar>();
		builder.Services.RemoveAll<IFontManager>();
		builder.Services.RemoveAll<IEmbeddedFontLoader>();

		builder.Services.AddSingleton<IEmbeddedFontLoader>(svc =>
			new FileSystemEmbeddedFontLoader(Path.Combine(Path.GetTempPath(), "maui-gtk4-font-cache"), svc));
		builder.Services.AddSingleton<GtkFontRegistrar>();
		builder.Services.AddSingleton<IGtkFontRegistry>(svc => svc.GetRequiredService<GtkFontRegistrar>());
		builder.Services.AddSingleton<IFontRegistrar>(svc => svc.GetRequiredService<GtkFontRegistrar>());
		builder.Services.AddSingleton<GtkFontManager>();
		builder.Services.AddSingleton<IGtkFontManager>(svc => svc.GetRequiredService<GtkFontManager>());
		builder.Services.AddSingleton<IFontManager>(svc => svc.GetRequiredService<GtkFontManager>());

		// Animation ticker — drives all MAUI animations (TranslateTo, FadeTo, etc.)
		// Must RemoveAll first to override MAUI's default Ticker registration.
		builder.Services.RemoveAll<ITicker>();
		builder.Services.AddSingleton<ITicker>(svc => new GtkPlatformTicker());

		// Named font sizes (FontSize="Title", etc.)
		Microsoft.Maui.Controls.DependencyService.Register<Microsoft.Maui.Controls.Internals.IFontNamedSizeService, GtkFontNamedSizeService>();

		// Graphics platform services
		builder.Services.AddSingleton<IStringSizeService, CairoStringSizeService>();
		builder.Services.AddSingleton<IBitmapExportService, CairoBitmapExportService>();
		builder.Services.AddSingleton<IImageLoadingService, CairoImageLoadingService>();

		builder.ConfigureMauiHandlers(handlers =>
		{
			handlers.AddMauiControlsHandlers();
		});

		return builder;
	}
}
