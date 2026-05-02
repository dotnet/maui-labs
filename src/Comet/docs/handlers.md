# Handler Architecture and Customization

Comet builds on .NET MAUI's handler architecture. Every Comet view maps to a
MAUI handler that creates and manages the platform-native control. This document
explains how the handler system works, how handlers are registered, and how to
customize or create new ones.


## Overview

The handler pipeline works as follows. For a high-level view of how handlers
fit into the framework architecture, see the
[Architecture Overview](architecture.md).

1. A Comet `View` subclass (e.g., `Button`, `Text`) implements one or more MAUI
   interfaces (e.g., `ITextButton`, `ILabel`).
2. The handler registry maps the Comet type to a MAUI handler type (e.g.,
   `ButtonHandler`, `LabelHandler`).
3. When a view needs to appear on screen, the framework resolves the handler,
   calls `SetVirtualView()`, and the handler creates the native platform control.
4. Property changes on the Comet view flow through the handler's property mapper
   to update the native control.


## Handler Registration

All handler mappings are registered in `AppHostBuilderExtensions.UseCometHandlers()`,
which is called automatically by `UseCometApp<T>()`.

```csharp
// In MauiProgram.cs
var builder = MauiApp.CreateBuilder();
builder.UseCometApp<MyApp>(); // registers all Comet handlers
```

Internally, `UseCometHandlers()` calls `ConfigureMauiHandlers()` with a
dictionary mapping every Comet view type to its handler:

```csharp
builder.ConfigureMauiHandlers(handlers => handlers.AddHandlers(
	new Dictionary<Type, Type>
	{
		{ typeof(Text), typeof(LabelHandler) },
		{ typeof(Button), typeof(ButtonHandler) },
		{ typeof(TextField), typeof(EntryHandler) },
		{ typeof(SecureField), typeof(EntryHandler) },
		{ typeof(TextEditor), typeof(EditorHandler) },
		{ typeof(Toggle), typeof(SwitchHandler) },
		{ typeof(Slider), typeof(SliderHandler) },
		{ typeof(Image), typeof(ImageHandler) },
		{ typeof(ListView), typeof(ListViewHandler) },
		{ typeof(CollectionView), typeof(CollectionViewHandler) },
		{ typeof(NavigationView), typeof(NavigationViewHandler) },
		{ typeof(TabView), typeof(TabViewHandler) },
		{ typeof(ScrollView), typeof(ScrollViewHandler) },
		{ typeof(RadioButton), typeof(RadioButtonHandler) },
		{ typeof(Picker), typeof(PickerHandler) },
		{ typeof(Spacer), typeof(SpacerHandler) },
		{ typeof(GraphicsView), typeof(GraphicsViewHandler) },
		// ... and more
	}));
```

Layout containers (`AbstractLayout`, `AbsoluteLayout`, `FlexLayout`, `Border`,
`RadioGroup`) all map to `LayoutHandler`. The application class `CometApp` maps
to `ApplicationHandler`.


## CometViewHandler

`CometViewHandler` is a partial class that bridges Comet's virtual view tree to
native platform views. It has platform-specific implementations in separate
files:

```
Handlers/View/CometViewHandler.cs          -- cross-platform base
Handlers/View/CometViewHandler.iOS.cs      -- iOS (UIView-based CometView)
Handlers/View/CometViewHandler.Android.cs  -- Android (ViewGroup-based CometView)
Handlers/View/CometViewHandler.Windows.cs  -- Windows (Grid-based CometView)
```

The cross-platform file defines gesture management:

```csharp
public partial class CometViewHandler
{
	public static void AddGestures(IViewHandler handler, IView view)
	{
		if (view is not IGestureView ig)
			return;
		foreach (var g in ig.Gestures)
			handler.AddGesture(g);
	}

	public static void AddGesture(IViewHandler handler, IView view, object arg)
	{
		if (arg is Gesture g)
			handler.AddGesture(g);
	}

	public static void RemoveGesture(IViewHandler handler, IView view, object arg)
	{
		if (arg is Gesture g)
			handler.RemoveGesture(g);
	}
}
```

### Platform CometView Classes

Each platform has a `CometView` class that implements `IReloadHandler` for hot
reload support:

- **iOS/Mac Catalyst:** `CometView : UIView, IReloadHandler` -- manages a child
  UIView resolved from the Comet view tree via `ToPlatform()`.
- **Android:** `CometView : AViewGroup, IReloadHandler` -- manages a child
  Android View.
- **Windows:** `CometView : Grid, IReloadHandler` -- manages child UI elements
  within a WinUI Grid.

Each CometView:
1. Calls `GetView()` to resolve the Comet `Body` to a concrete view.
2. Calls `ToPlatform()` to obtain the native control from the handler.
3. Manages the native view lifecycle and layout.
4. Uses `GetContentTypeHashCode()` for handler reuse -- if the view type has
   not changed, the existing handler is reused rather than recreated.
5. Implements `IReloadHandler.Reload()` to rebuild the view tree during hot
   reload.


## Property Mappers

MAUI handlers use `PropertyMapper` dictionaries to map virtual view properties to
native platform updates. Comet extends these mappers through `AppendToMapping`,
`PrependToMapping`, and `ModifyMapping`.

### AppendToMapping

Adds behavior **after** the default mapping runs. This is the most common
customization pattern. `UseCometHandlers()` uses this extensively:

```csharp
// Add gesture recognition to all views
ViewHandler.ViewMapper.AppendToMapping(
	nameof(IGestureView.Gestures),
	CometViewHandler.AddGestures);

// Apply shadow from environment
ViewHandler.ViewMapper.AppendToMapping("CometShadow", (handler, view) =>
{
	if (view is not View cometView)
		return;
	var shadow = cometView.GetEnvironment<Shadow>(EnvironmentKeys.View.Shadow);
	if (shadow == null)
		return;
	// Apply shadow to platform view...
});

// Apply border styling
LayoutHandler.Mapper.AppendToMapping("CometBorderStyling", (handler, view) =>
{
	if (view is not Border border)
		return;
	// Apply border radius, stroke, background...
});
```

### PrependToMapping

Runs **before** the default mapping. Useful for preprocessing:

```csharp
ButtonHandler.Mapper.PrependToMapping(nameof(ITextButton.Text), (handler, view) =>
{
	// Custom logic before the default text mapping
	if (view is View cometView)
	{
		var customText = cometView.GetEnvironment<string>("CustomButtonPrefix");
		// Modify state before default handler runs
	}
});
```

### ModifyMapping

Wraps an existing mapping with conditional logic. The original mapping is passed
as a delegate:

```csharp
SliderHandler.Mapper.ModifyMapping(nameof(ISlider.Value), (handler, view, original) =>
{
	// Only update the slider value when the user is not actively dragging
	if (view is View cometView)
	{
		var isDragging = cometView.GetEnvironment<bool>("IsDragging");
		if (!isDragging)
			original?.Invoke(handler, view);
	}
});
```


## Customizing Existing Handlers

### From Application Code

You can customize any handler at app startup:

```csharp
public static MauiApp CreateMauiApp()
{
	var builder = MauiApp.CreateBuilder();
	builder.UseCometApp<MyApp>();

	// Customize the Entry handler for all TextField instances
	EntryHandler.Mapper.AppendToMapping("CustomEntry", (handler, view) =>
	{
#if __IOS__
		handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.RoundedRect;
#elif ANDROID
		handler.PlatformView.SetBackgroundColor(
			global::Android.Graphics.Color.Transparent);
#endif
	});

	return builder.Build();
}
```

### Weak Reference Event Tracking

Comet uses `ConditionalWeakTable` to track event handler subscriptions on
platform views. This prevents duplicate subscriptions when `SetVirtualView` is
called multiple times (common during hot reload):

```csharp
// iOS example from AppHostBuilderExtensions
static readonly ConditionalWeakTable<UITextField, EventHandler>
	_entryEditingChangedHandlers = new();

EntryHandler.Mapper.AppendToMapping("CometTextChanged", (handler, view) =>
{
	var entry = handler.PlatformView;
	if (_entryEditingChangedHandlers.TryGetValue(entry, out _))
		return; // already subscribed

	EventHandler h = (s, e) => { /* handle text change */ };
	entry.EditingChanged += h;
	_entryEditingChangedHandlers.AddOrUpdate(entry, h);
});
```


## Creating a Custom Handler

To create a handler for a new custom control, follow this pattern:

### Step 1: Define the View

```csharp
namespace MyApp
{
	public class RatingView : View
	{
		public RatingView(int maxStars = 5)
		{
			MaxStars = maxStars;
		}

		readonly State<int> _value = 0;
		public int Value
		{
			get => _value.Value;
			set => _value.Value = value;
		}

		public int MaxStars { get; }
	}
}
```

### Step 2: Create the Handler (Cross-Platform Base)

```csharp
namespace MyApp.Handlers
{
	public partial class RatingViewHandler
	{
		public static readonly PropertyMapper<RatingView, RatingViewHandler> Mapper =
			new(ViewHandler.ViewMapper)
			{
				[nameof(RatingView.Value)] = MapValue,
				[nameof(RatingView.MaxStars)] = MapMaxStars,
			};

		public RatingViewHandler() : base(Mapper)
		{
		}

		static partial void MapValue(
			RatingViewHandler handler, RatingView view);
		static partial void MapMaxStars(
			RatingViewHandler handler, RatingView view);
	}
}
```

### Step 3: Platform Implementation (iOS Example)

Create a file named `RatingViewHandler.iOS.cs`:

```csharp
#if __IOS__
namespace MyApp.Handlers
{
	public partial class RatingViewHandler
		: ViewHandler<RatingView, UIKit.UIView>
	{
		protected override UIKit.UIView CreatePlatformView()
		{
			return new UIKit.UIView(); // your native iOS view
		}

		static partial void MapValue(
			RatingViewHandler handler, RatingView view)
		{
			// Update native view with view.Value
		}

		static partial void MapMaxStars(
			RatingViewHandler handler, RatingView view)
		{
			// Update native view with view.MaxStars
		}
	}
}
#endif
```

### Step 4: Register the Handler

```csharp
builder.ConfigureMauiHandlers(handlers =>
{
	handlers.AddHandler<RatingView, RatingViewHandler>();
});
```


## Handler File Inventory

The `Handlers/` directory is organized by control:

| Directory | Handler | Platform Files |
|-----------|---------|----------------|
| `View/` | `CometViewHandler` | iOS, Android, Windows |
| `CometHost/` | `CometHostHandler` | iOS, Android, Windows |
| `Navigation/` | `NavigationViewHandler` | iOS, Android, Windows, Standard |
| `ListView/` | `ListViewHandler` | iOS, Android, Windows |
| `CollectionView/` | `CollectionViewHandler` | iOS, Android, Windows, Standard |
| `ScrollView/` | `ScrollViewHandler` | iOS, Android, Windows, Standard |
| `TabView/` | `TabViewHandler` | iOS, Android, Windows, Standard |
| `RadioButton/` | `RadioButtonHandler` | iOS, Android, Windows, Standard |
| `ShapeView/` | `ShapeViewHandler` | (cross-platform only) |
| `Spacer/` | `SpacerHandler` | (cross-platform only) |
| `NativeHost/` | `NativeHostHandler` | iOS, Android, Windows |
| `MauiViewHost/` | `MauiViewHostHandler` | (cross-platform only) |

Standard files (`*.Standard.cs`) provide no-op stubs for `netstandard` targets.


## Platform-Specific File Conventions

Comet uses a file-naming convention enforced by `Directory.Build.targets` to
include or exclude files based on the target framework:

| Suffix / Folder | Included When |
|-----------------|---------------|
| `*.iOS.cs` or `iOS/` | `net10.0-ios` or `net10.0-maccatalyst` |
| `*.Android.cs` or `Android/` | `net10.0-android` |
| `*.Windows.cs` or `Windows/` | `net10.0-windows` |
| `*.Mac.cs` or `MacCatalyst/` | `net10.0-maccatalyst` |
| `*.Standard.cs` or `Standard/` | `netstandard` or `net6.0`/`net7.0` |

This allows a single project to contain all platform implementations. The build
system automatically selects the correct files for each target framework.


## CometHostHandler

`CometHostHandler` is used internally to host Comet views inside container cells
(e.g., CollectionView data templates). It manages:

- View resolution via `GetView()` and `ToPlatform()`
- Size calculation via `GetDesiredSize()`
- Nested container views (`CometHostContainerView`)
- Handler reuse across cell recycling

Application code rarely interacts with `CometHostHandler` directly.


## Handler Lifecycle

1. **Resolution:** When a view first appears, the framework resolves its handler
   from the registration dictionary.
2. **SetVirtualView:** The handler receives the Comet view instance. Property
   mappers fire to sync all properties to the native control.
3. **Updates:** When a property changes on the Comet view (through state changes
   or environment updates), `ViewHandler.UpdateValue(propertyName)` triggers the
   relevant mapper entry.
4. **Hot Reload:** During hot reload, `IReloadHandler.Reload()` is called.
   CometView implementations call `SetView(CurrentView, true)` to force a full
   view tree rebuild.
5. **Disposal:** When the view is removed from the tree, the handler and its
   native control are cleaned up.


## Style Resolution Mappers

Before property-specific mappers run, Comet registers style resolution mappers
that resolve the current control style (e.g., `ButtonConfiguration`,
`ToggleConfiguration`). These ensure that themed values are available when
individual property mappers execute.

Style resolution is registered in `RegisterStyleResolutionMappers()` within
`AppHostBuilderExtensions` and runs automatically during startup. For details
on the styling system, see the [Styling and Theming Guide](styling.md).


## See Also

- [Architecture Overview](architecture.md) -- how handlers fit into the overall
  framework layer stack, including the diff algorithm and view pipeline.
- [Control Catalog](controls.md) -- the complete control-to-handler mapping and
  fluent API reference for every control.
- [MAUI Integration Guide](maui-interop.md) -- embedding native platform views
  and MAUI controls using NativeHost and MauiViewHost.
- [Contributing Guide](contributing.md) -- step-by-step instructions for adding
  new handlers to the framework.
