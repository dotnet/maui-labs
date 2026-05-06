using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Animations;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Platform;
using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Hosting;

public static partial class AppHostBuilderExtensions
{
    public static MauiAppBuilder UseMauiAppMacOS<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
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
        handlersCollection.AddHandler<ContentPage, ContentPageHandler>();
        handlersCollection.AddHandler<Layout, LayoutHandler>();
        handlersCollection.AddHandler<ContentView, ContentViewHandler>();
        handlersCollection.AddHandler<ContentPresenter, ContentViewHandler>();
        handlersCollection.AddHandler<Label, LabelHandler>();
        handlersCollection.AddHandler<Button, ButtonHandler>();
        handlersCollection.AddHandler<Entry, EntryHandler>();
        handlersCollection.AddHandler<Editor, EditorHandler>();
        handlersCollection.AddHandler<CheckBox, CheckBoxHandler>();
        handlersCollection.AddHandler<Switch, SwitchHandler>();
        handlersCollection.AddHandler<Slider, SliderHandler>();
        handlersCollection.AddHandler<ProgressBar, ProgressBarHandler>();
        handlersCollection.AddHandler<ActivityIndicator, ActivityIndicatorHandler>();
        handlersCollection.AddHandler(typeof(Microsoft.Maui.Controls.Image), typeof(ImageHandler));
        handlersCollection.AddHandler<ImageButton, ImageButtonHandler>();
        handlersCollection.AddHandler<Picker, PickerHandler>();
        handlersCollection.AddHandler<DatePicker, DatePickerHandler>();
        handlersCollection.AddHandler<TimePicker, TimePickerHandler>();
        handlersCollection.AddHandler<ScrollView, ScrollViewHandler>();
        handlersCollection.AddHandler<Border, BorderHandler>();
        handlersCollection.AddHandler<Microsoft.Maui.Controls.Frame, BorderHandler>();
        handlersCollection.AddHandler<NavigationPage, NavigationPageHandler>();
        handlersCollection.AddHandler<Shell, ShellHandler>();
        handlersCollection.AddHandler<ShellItem, ShellItemHandler>();
        handlersCollection.AddHandler<ShellSection, ShellSectionHandler>();
        handlersCollection.AddHandler<ShellContent, ShellContentHandler>();
        handlersCollection.AddHandler<TabbedPage, TabbedPageHandler>();
        handlersCollection.AddHandler<FlyoutPage, FlyoutPageHandler>();
        handlersCollection.AddHandler<Stepper, StepperHandler>();
        handlersCollection.AddHandler<RadioButton, RadioButtonHandler>();
        handlersCollection.AddHandler<SearchBar, SearchBarHandler>();
        handlersCollection.AddHandler<CollectionView, CollectionViewHandler>();
		handlersCollection.AddHandler<CarouselView, CarouselViewHandler>();
        handlersCollection.AddHandler<ListView, ListViewHandler>();
        handlersCollection.AddHandler<TableView, TableViewHandler>();
        handlersCollection.AddHandler<RefreshView, RefreshViewHandler>();
        handlersCollection.AddHandler<SwipeView, SwipeViewHandler>();
        handlersCollection.AddHandler<IndicatorView, IndicatorViewHandler>();
        handlersCollection.AddHandler<GraphicsView, GraphicsViewHandler>();
        handlersCollection.AddHandler<WebView, WebViewHandler>();

#pragma warning disable CS0618
        handlersCollection.AddHandler(typeof(Microsoft.Maui.IShapeView), typeof(Microsoft.Maui.Platforms.MacOS.Handlers.ShapeViewHandler));
#pragma warning restore CS0618
        handlersCollection.AddHandler<BoxView, Microsoft.Maui.Platforms.MacOS.Handlers.ShapeViewHandler>();

        // Register concrete Shape types to ensure our handler is used (not MAUI's built-in stub)
        handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Rectangle, Microsoft.Maui.Platforms.MacOS.Handlers.ShapeViewHandler>();
        handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Ellipse, Microsoft.Maui.Platforms.MacOS.Handlers.ShapeViewHandler>();
        handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Line, Microsoft.Maui.Platforms.MacOS.Handlers.ShapeViewHandler>();
        handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Polyline, Microsoft.Maui.Platforms.MacOS.Handlers.ShapeViewHandler>();
        handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Polygon, Microsoft.Maui.Platforms.MacOS.Handlers.ShapeViewHandler>();
        handlersCollection.AddHandler<Microsoft.Maui.Controls.Shapes.Path, Microsoft.Maui.Platforms.MacOS.Handlers.ShapeViewHandler>();

        handlersCollection.AddHandler<Controls.MapView, MapViewHandler>();

        return handlersCollection;
    }

    static MauiAppBuilder SetupDefaults(this MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<IDispatcherProvider>(svc => new MacOSDispatcherProvider());

        // Replace the default System.Timers.Timer-based ticker with one using NSTimer on the main run loop.
        // This ensures animation property updates happen on the main thread (required for AppKit).
        builder.Services.Replace(ServiceDescriptor.Scoped<ITicker>(svc => new MacOSTicker()));

        // Register macOS embedded font loader for CTFontManager registration
        builder.Services.Replace(ServiceDescriptor.Singleton<IEmbeddedFontLoader>(svc =>
            new MacOSEmbeddedFontLoader(svc)));

        // Register macOS font registrar that finds fonts in the bundle's Fonts/ directory
        builder.Services.Replace(ServiceDescriptor.Singleton<IFontRegistrar>(svc =>
            new MacOSFontRegistrar(svc.GetRequiredService<IEmbeddedFontLoader>())));

        // Register macOS font manager for proper font resolution
        builder.Services.Replace(ServiceDescriptor.Singleton<IFontManager>(svc =>
            new MacOSFontManager(svc.GetRequiredService<IFontRegistrar>(), svc)));

        AlertManagerSubscription.Register(builder.Services);

        builder.Services.AddScoped(svc =>
        {
            var provider = svc.GetRequiredService<IDispatcherProvider>();
            if (DispatcherProvider.SetCurrent(provider))
                svc.GetService<ILogger<Dispatcher>>()?.LogWarning("Replaced an existing DispatcherProvider.");

            return Dispatcher.GetForCurrentThread()!;
        });

        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddMauiControlsHandlers();
        });

        return builder;
    }
}
