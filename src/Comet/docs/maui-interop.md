# MAUI Integration Guide

Comet views and .NET MAUI controls can coexist in the same application. This
guide covers three integration patterns and explains when to use each one.


## Overview of Integration Patterns

| Pattern | Direction | Class | Use Case |
|---------|-----------|-------|----------|
| MAUI-in-Comet | Embed MAUI inside Comet | `MauiViewHost` | Third-party MAUI controls |
| Comet-in-MAUI | Embed Comet inside MAUI | `CometHost` | Gradual migration |
| Native-in-Comet | Embed platform views | `NativeHost` | Platform-specific UI |


## Pattern 1: Embedding MAUI Views in Comet (MauiViewHost)

`MauiViewHost` wraps any `Microsoft.Maui.IView` (XAML control, third-party
control, or code-behind view) so it can appear inside a Comet view tree. For
the handler that powers this, see the
[Handler Architecture Guide](handlers.md).

### MauiViewHost API

```csharp
public class MauiViewHost : View
{
	// Wrap an existing MAUI view instance
	public MauiViewHost(IView view);

	// Lazy creation -- the factory runs once, on first access
	public MauiViewHost(Func<IView> factory);

	// The hosted MAUI view (thread-safe lazy initialization)
	public IView HostedView { get; }
}
```

### Basic Usage

```csharp
using Comet;
using Microsoft.Maui.Controls;

public class ChartPage : View
{
	[Body]
	View body() =>
		VStack(
			Text("Sales Dashboard"),
			new MauiViewHost(new Label
			{
				Text = "MAUI Label inside Comet",
				FontSize = 18,
				HorizontalTextAlignment = TextAlignment.Center
			}).Frame(height: 44),
			new MauiViewHost(() => CreateChart())
				.Frame(width: 300, height: 200)
		);

	static IView CreateChart()
	{
		// Expensive control created lazily
		return new Grid
		{
			Children =
			{
				new BoxView { Color = Colors.Blue },
				new Label { Text = "Chart Placeholder" }
			}
		};
	}
}
```

### Lazy Factory Pattern

The factory constructor defers creation until the view is first accessed. This
is useful for expensive controls or controls that require a `MauiContext`:

```csharp
// Factory runs once, result is cached (thread-safe)
var host = new MauiViewHost(() =>
{
	var chart = new ThirdPartyChartControl();
	chart.DataSource = LoadData();
	return chart;
});
```

The factory uses double-checked locking internally. Once created, the
`HostedView` property returns the cached instance on subsequent accesses.

### Sizing and Measurement

`MauiViewHost` attempts to measure the hosted view through its handler. If
the hosted view does not yet have a handler, it falls back to frame constraints
or available size. Always provide explicit dimensions for controls that cannot
self-size:

```csharp
new MauiViewHost(new SomeControl())
	.Frame(width: 300, height: 200)
```

The default minimum height is 44 points when no other size information is
available.

### Disposal

`MauiViewHost` disconnects the hosted view's handler and disposes it (if
`IDisposable`) when the host is disposed. This happens automatically when the
view leaves the tree.


## Pattern 2: Embedding Comet Views in MAUI (CometHost)

`CometHost` is a `Microsoft.Maui.Controls.View` that hosts a Comet `View`
inside a MAUI page. Use this when your app shell is MAUI (Shell, ContentPage,
NavigationPage) and you want to embed Comet views as sections of the UI.

### CometHost API

```csharp
public class CometHost : Microsoft.Maui.Controls.View,
	IContentView, IVisualTreeElement
{
	// BindableProperty for XAML or code binding
	public static readonly BindableProperty CometViewProperty;

	public Comet.View CometView { get; set; }

	public CometHost();
	public CometHost(Comet.View view);
}
```

### Usage in a MAUI ContentPage

```csharp
using Comet;
using Microsoft.Maui.Controls;

public class HybridPage : ContentPage
{
	public HybridPage()
	{
		Content = new CometHost(new MyCometDashboard());
	}
}
```

### Usage as the App Root

The CometControlsGallery sample uses `CometHost` at the root level, wrapping
a Comet `SidebarLayout` inside a standard MAUI `ContentPage`:

```csharp
public class App : Microsoft.Maui.Controls.Application
{
	protected override Window CreateWindow(IActivationState activationState)
	{
		var page = new ContentPage
		{
			Padding = 0,
			Content = new CometHost(new SidebarLayout())
		};
		return new Window(page);
	}
}
```

This pattern keeps the MAUI application lifecycle and window management intact
while delegating the entire visual tree to Comet.

### Usage in XAML

Since `CometHost` has a `BindableProperty`, it can also be used from XAML:

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:comet="clr-namespace:Comet;assembly=Comet">
    <comet:CometHost x:Name="cometHost" />
</ContentPage>
```

```csharp
// Code-behind
cometHost.CometView = new MyCometView();
```

### How CometHost Renders Content

`CometHost` implements `IContentView`. When MAUI asks for the presented
content, it calls `CometView.GetView()` to resolve the Comet view's body
(the rendered output), then returns that as the `IView` for measurement and
arrangement. This avoids handler circularity -- the `CometHostHandler` renders
the body content directly rather than trying to host the Comet view through
`CometViewHandler`.

### Platform Handlers for CometHost

Each platform has a handler that creates a container and renders the Comet
content into it:

| Platform | Container | Layout Method |
|----------|-----------|---------------|
| iOS / Mac Catalyst | `UIView` subclass | `LayoutSubviews()` |
| Android | `FrameLayout` subclass | `OnLayout()` |
| Windows | WinUI `Canvas` subclass | `ArrangeOverride()` |

All handlers follow the same sequence: `ConnectHandler` resolves the Comet
view body, converts it to a platform view via `ToPlatform()`, and adds it
to the container. Layout passes measure and arrange the virtual view, then
size the native content to match.


## Pattern 3: Embedding Native Platform Views (NativeHost)

`NativeHost` embeds platform-native views (UIKit, Android, WinUI) directly
into a Comet view tree. Use this for platform APIs with no MAUI equivalent.

### NativeHost API

```csharp
public partial class NativeHost : View, INativeHost
{
	// Create with a factory that receives the MauiContext
	public NativeHost(
		Func<IMauiContext, object> factory,
		bool ownsNativeView = true);

	// Wrap an existing native view instance
	public NativeHost(
		object nativeView,
		bool ownsNativeView = false);

	// Lifecycle callbacks (fluent API, all return NativeHost)
	public NativeHost OnConnect(Action<object, IMauiContext> action);
	public NativeHost OnUpdate(Action<object, IMauiContext> action);
	public NativeHost OnDisconnect(Action<object> action);

	// Push a value to the native view
	public NativeHost Sync<T>(T value, Action<object, T> apply);

	// Override measurement
	public NativeHost MeasureUsing(Func<Size, Size> measure);

	// Retrieve the native view (typed)
	public bool TryGetNativeView<T>(out T resolvedView) where T : class;

	// Whether this host owns (and should dispose) the native view
	public bool OwnsNativeView { get; }
}
```

### Basic Usage

```csharp
public class NativeMapPage : View
{
	[Body]
	View body() =>
		VStack(
			Text("Native Map"),
			new NativeHost(mauiContext =>
			{
#if __IOS__
				return new MapKit.MKMapView
				{
					MapType = MapKit.MKMapType.Standard,
					ShowsUserLocation = true
				};
#elif __ANDROID__
				return new Android.Widget.TextView(mauiContext.Context)
				{
					Text = "Map placeholder on Android"
				};
#else
				return new object();
#endif
			}).Frame(height: 300)
		);
}
```

### Lifecycle Callbacks

`NativeHost` provides three lifecycle hooks in a fluent API:

```csharp
new NativeHost(mauiContext =>
{
#if __IOS__
	return new UIKit.UILabel();
#else
	return new object();
#endif
})
.OnConnect((nativeView, mauiContext) =>
{
	// Called when the native view is connected to the handler.
	// Configure the view here.
#if __IOS__
	if (nativeView is UIKit.UILabel label)
	{
		label.Text = "Connected";
		label.TextColor = UIKit.UIColor.SystemBlue;
	}
#endif
})
.OnUpdate((nativeView, mauiContext) =>
{
	// Called when the view needs to be updated.
	// Sync Comet state to native properties here.
})
.OnDisconnect(nativeView =>
{
	// Called when the native view is disconnected.
	// Clean up resources here.
})
.Frame(height: 44);
```

Callbacks are stored in lists, so multiple `OnConnect`, `OnUpdate`, or
`OnDisconnect` calls accumulate rather than replacing each other.

### Syncing State to Native Views

The `Sync<T>` method pushes a value to the native view whenever the view
updates:

```csharp
readonly State<string> message = "Hello";

new NativeHost(mauiContext =>
{
#if __IOS__
	return new UIKit.UILabel();
#else
	return new object();
#endif
})
.Sync(message.Value, (nativeView, text) =>
{
#if __IOS__
	if (nativeView is UIKit.UILabel label)
		label.Text = text;
#endif
})
.Frame(height: 44);
```

### Custom Measurement

Override how the native view is measured:

```csharp
new NativeHost(mauiContext =>
{
#if __IOS__
	return new UIKit.UILabel { Text = "Custom size" };
#else
	return new object();
#endif
})
.MeasureUsing(available => new Size(200, 44))
.Frame(height: 44);
```

### View Ownership

The `ownsNativeView` parameter controls disposal:

- `true` (default for factory constructor): The handler disposes the native
  view when disconnected.
- `false` (default for instance constructor): The caller manages the native
  view's lifetime.


## Using MAUI Services from Comet Views

Comet views can access the MAUI dependency injection container through
`MauiContext`:

```csharp
public class ServicePage : View
{
	[Body]
	View body()
	{
		// Access services from the DI container
		var serviceProvider = this.GetMauiContext()?.Services;
		var logger = serviceProvider?.GetService<ILogger<ServicePage>>();

		return VStack(
			Text("Service access demo"),
			Button("Log", () =>
			{
				logger?.LogInformation("Button tapped");
			})
		);
	}
}
```

For cleaner architecture, register and inject services in
`CreateMauiApp()`:

```csharp
public static MauiApp CreateMauiApp()
{
	var builder = MauiApp.CreateBuilder();
	builder.UseCometApp<MyApp>();

	// Register services
	builder.Services.AddSingleton<IDataService, DataService>();
	builder.Services.AddTransient<INavigationService, NavigationService>();

	return builder.Build();
}
```


## When to Use Each Pattern

### Use MauiViewHost When:

- Integrating third-party MAUI controls (Syncfusion, Telerik, DevExpress).
- Reusing existing MAUI views during a migration to Comet.
- The control has a MAUI handler and does not need direct platform access.

### Use CometHost When:

- Your app uses MAUI Shell or NavigationPage as the root.
- Migrating incrementally: replace individual pages with Comet while keeping
  the MAUI navigation structure.
- You need MAUI features that Comet does not wrap (e.g., Shell search, flyout).

### Use NativeHost When:

- You need direct access to platform APIs (MapKit, Camera, ARKit).
- No MAUI control exists for the functionality you need.
- Performance-critical rendering that benefits from direct platform access.

### Decision Matrix

```
Need to embed a MAUI control in Comet?
  --> MauiViewHost

Need to embed a Comet view in a MAUI page?
  --> CometHost

Need direct platform API access?
  --> NativeHost

Building a new app entirely in Comet?
  --> Use CometApp + NavigationView/TabView/CometShell
      No interop layer needed
```

For the full handler system that powers these integration patterns, see the
[Handler Architecture Guide](handlers.md).


## See Also

- [Handler Architecture](handlers.md) -- the handler system that powers
  MauiViewHost, CometHost, and NativeHost integration.
- [Control Catalog](controls.md) -- Comet controls that can host MAUI content
  and the CometApp entry point.
- [Architecture Overview](architecture.md) -- the view pipeline that connects
  Comet views to native platform controls.


## Complete Integration Example

An app that uses all three patterns:

```csharp
using Comet;
using Microsoft.Maui.Controls;
using static Comet.CometControls;

// MAUI Shell app with Comet views embedded
public class App : Microsoft.Maui.Controls.Application
{
	protected override Window CreateWindow(IActivationState activationState)
	{
		return new Window(new ContentPage
		{
			// Pattern 2: CometHost wrapping the entire Comet UI
			Content = new CometHost(new MainView())
		});
	}
}

public class MainView : View
{
	[Body]
	View body() =>
		VStack(
			Text("Integration Demo"),

			// Pattern 1: MAUI control inside Comet
			new MauiViewHost(new ProgressBar { Progress = 0.7 })
				.Frame(height: 20),

			// Pattern 3: Native view inside Comet
			new NativeHost(ctx =>
			{
#if __IOS__
				return new UIKit.UIActivityIndicatorView(
					UIKit.UIActivityIndicatorViewStyle.Large);
#elif __ANDROID__
				return new Android.Widget.ProgressBar(ctx.Context);
#else
				return new object();
#endif
			}).Frame(width: 44, height: 44)
		);
}
```
