# Platform-Specific Guides

Comet targets Android, iOS, Mac Catalyst, and Windows through .NET MAUI's
multi-targeting system. This guide covers platform-specific patterns, file
organization conventions, conditional compilation, and platform API access.


## File Organization

Platform-specific code is included or excluded at build time via
`Directory.Build.targets`. Two conventions are supported:

**File suffix convention:**

| Suffix | Target |
|--------|--------|
| `*.iOS.cs` | iOS and Mac Catalyst |
| `*.Android.cs` | Android |
| `*.Windows.cs` | Windows |
| `*.Mac.cs` | Mac Catalyst only |
| `*.MaciOS.cs` | Both Mac Catalyst and iOS |
| `*.Standard.cs` | netstandard / net6.0 / net7.0 |

**Folder convention:**

| Folder | Target |
|--------|--------|
| `iOS/` | iOS and Mac Catalyst |
| `Android/` | Android |
| `Windows/` | Windows |
| `Mac/` or `MacCatalyst/` | Mac Catalyst only |
| `MaciOS/` | Both Mac Catalyst and iOS |
| `Standard/` | netstandard / net6.0 / net7.0 |

Files not matching the active target framework are excluded from compilation
and added as `None` items instead. This means platform-specific files compile
only for their intended target without requiring `#if` directives at the file
level.

### Example Directory Layout

```
src/Comet/
  Handlers/
    CometViewHandler.cs          <-- shared
    CometViewHandler.iOS.cs      <-- iOS + Mac Catalyst
    CometViewHandler.Android.cs  <-- Android
    CometViewHandler.Windows.cs  <-- Windows
  Platform/
    iOS/
      iOSExtensions.cs
      CUITapGestures.cs
    Android/
      CometTouchGestureListener.cs
      HandlerExtensions.cs
    Windows/
      HandlerExtensions.cs
```


## Conditional Compilation

When you need platform-specific logic within a shared file, use preprocessor
directives. The target framework defines these symbols:

| Symbol | Platform |
|--------|----------|
| `__IOS__` | iOS |
| `MACCATALYST` | Mac Catalyst |
| `__ANDROID__` | Android |
| `WINDOWS` | Windows (WinUI) |

Common patterns:

```csharp
#if __IOS__ || MACCATALYST
using UIKit;
#elif __ANDROID__
using Android.Views;
#elif WINDOWS
using Microsoft.UI.Xaml;
#endif

public void ConfigurePlatform()
{
#if __IOS__ || MACCATALYST
	// iOS and Mac Catalyst share UIKit
	UIApplication.SharedApplication.StatusBarHidden = true;
#elif __ANDROID__
	// Android-specific configuration
	var activity = Platform.CurrentActivity;
#elif WINDOWS
	// WinUI-specific configuration
	var appWindow = GetAppWindow();
#endif
}
```

The `AppHostBuilderExtensions.cs` file demonstrates this pattern extensively,
using `ConditionalWeakTable` to track platform-specific event subscriptions for
controls like `UITextField` (iOS), `EditText` (Android), and WinUI text boxes.


## Handler Registration

Comet registers handlers for all its controls in
`AppHostBuilderExtensions.UseCometHandlers()`. This is called automatically by
`UseCometApp<T>()` or can be called directly. For the complete handler mapping,
see the [Handler Architecture Guide](handlers.md).

```csharp
var builder = MauiApp.CreateBuilder();
builder.UseMauiApp<App>();
builder.UseCometHandlers();
```

The method registers handlers for core Comet types (`View`, `NavigationView`,
`TabView`, `MauiViewHost`, `NativeHost`, `CometHost`) and configures property
mappers for MAUI controls to wire up Comet's environment-based property system.


## iOS and Mac Catalyst

iOS and Mac Catalyst share the same codebase through UIKit. Files suffixed
with `.iOS.cs` or placed in `iOS/` folders compile for both platforms.

### CometView on iOS

The platform view for Comet views on iOS is a `UIView` subclass. The
`CometViewHandler.iOS.cs` provides the handler that bridges Comet's virtual
view tree to UIKit:

```csharp
// Platform container for Comet content on iOS
public class CometHostContainerView : UIView
{
	public override void LayoutSubviews()
	{
		base.LayoutSubviews();
		// Measures and arranges the virtual content
		// Re-resolves the virtual view from the root Comet View
		// in case its body was rebuilt during a state change
		_virtualView?.Measure(Bounds.Width, Bounds.Height);
		_virtualView?.Arrange(new Rect(0, 0, Bounds.Width, Bounds.Height));
		_contentView.Frame = Bounds;
	}

	public override CGSize SizeThatFits(CGSize size)
	{
		// Delegates measurement to the virtual view
		var measured = _virtualView.Measure(size.Width, size.Height);
		return new CGSize(measured.Width, measured.Height);
	}
}
```

Key behaviors:

- `ClipsToBounds = true` by default for proper content clipping.
- `AutoresizingMask` is set to `FlexibleWidth | FlexibleHeight` on content
  views for automatic resizing.
- The handler re-resolves the virtual view on every layout pass to pick up
  state-driven body rebuilds.

### Safe Area and Status Bar

On iOS, safe area insets are handled automatically by MAUI's layout system.
Comet views inherit this behavior. For custom safe area handling, access the
platform view:

```csharp
#if __IOS__
public class SafeAreaPage : View
{
	[Body]
	View body() =>
		VStack(
			Text("Content respects safe area")
		).Padding(new Thickness(0, 44, 0, 34));
	// Manual padding matching safe area insets
}
#endif
```

### Mac Catalyst Window Sizing

Control window dimensions on Mac Catalyst using lifecycle events. The
CometControlsGallery sample demonstrates this:

```csharp
#if MACCATALYST
builder.ConfigureLifecycleEvents(events =>
{
	events.AddiOS(ios =>
	{
		ios.SceneWillConnect((scene, session, options) =>
		{
			if (scene is UIKit.UIWindowScene windowScene)
			{
				windowScene.SizeRestrictions.MinimumSize =
					new CoreGraphics.CGSize(900, 600);
				windowScene.SizeRestrictions.MaximumSize =
					new CoreGraphics.CGSize(2000, 1400);
			}
		});
	});
});
#endif
```

Note that Mac Catalyst uses the `AddiOS` lifecycle event API because it is
built on the iOS/UIKit foundation.


## Android

### CometView on Android

The Android platform view is a `FrameLayout` subclass:

```csharp
public class CometHostContainerView : FrameLayout
{
	protected override void OnLayout(
		bool changed, int left, int top, int right, int bottom)
	{
		base.OnLayout(changed, left, top, right, bottom);
		// Converts pixel dimensions to device-independent pixels
		var density = Context?.Resources?.DisplayMetrics?.Density ?? 1;
		var widthDp = (right - left) / density;
		var heightDp = (bottom - top) / density;
		_virtualView.Measure(widthDp, heightDp);
		_virtualView.Arrange(new Rect(0, 0, widthDp, heightDp));
	}
}
```

Key behaviors:

- Content views use `MatchParent` layout parameters for both width and height.
- All measurements convert from pixels to density-independent pixels using the
  display density factor.
- The container view is a `FrameLayout` (single-child layout).

### Android Gesture Handling

Comet provides `CometTouchGestureListener` in `Platform/Android/` that bridges
Comet's gesture system to Android's touch event system. Touch events are
intercepted and dispatched to the appropriate `Gesture` objects (tap, pan,
swipe, pinch).

### Event Handler Deduplication

On Android, Comet uses `ConditionalWeakTable` to prevent duplicate event
handler subscriptions when handlers are reconnected during hot reload or view
recycling:

```csharp
#if ANDROID
static readonly ConditionalWeakTable<object, object>
	_entryTextChangedHandlers = new();
// Handler code checks the weak table before subscribing
#endif
```

This pattern applies to all interactive controls: text fields, sliders,
switches, search bars, and checkboxes.


## Windows (WinUI)

### CometView on Windows

The Windows platform view is a WinUI `Canvas` subclass:

```csharp
public class CometHostContainerPanel : Canvas
{
	protected override Windows.Foundation.Size ArrangeOverride(
		Windows.Foundation.Size finalSize)
	{
		_virtualView?.Arrange(new Rect(0, 0,
			finalSize.Width, finalSize.Height));
		_contentElement.Arrange(new Windows.Foundation.Rect(0, 0,
			finalSize.Width, finalSize.Height));
		return base.ArrangeOverride(finalSize);
	}

	protected override Windows.Foundation.Size MeasureOverride(
		Windows.Foundation.Size availableSize)
	{
		_virtualView?.Measure(
			availableSize.Width, availableSize.Height);
		_contentElement.Measure(availableSize);
		return base.MeasureOverride(availableSize);
	}
}
```

Key behaviors:

- Content is added to `Canvas.Children` and participates in WinUI's measure
  and arrange cycle.
- The `Canvas`-based container allows absolute positioning of content.

### Windows-Specific Imports

Windows handlers reference `Microsoft.UI.Xaml` types. The
`AppHostBuilderExtensions.cs` includes Win2D for graphics:

```csharp
#if WINDOWS
using Microsoft.Maui.Graphics.Win2D;
#endif
```


## Writing Platform-Specific Code

### Using Partial Classes

The recommended pattern for platform-specific behavior is partial classes with
platform-suffixed files:

```csharp
// MyService.cs (shared)
public partial class MyService
{
	public partial string GetPlatformName();
}

// MyService.iOS.cs
public partial class MyService
{
	public partial string GetPlatformName() => "iOS";
}

// MyService.Android.cs
public partial class MyService
{
	public partial string GetPlatformName() => "Android";
}

// MyService.Windows.cs
public partial class MyService
{
	public partial string GetPlatformName() => "Windows";
}
```

### Using Conditional Compilation in Shared Files

For small platform differences, use `#if` directives:

```csharp
public void ConfigureHttpClient(HttpClient client)
{
#if __ANDROID__
	// Android requires AndroidMessageHandler for TLS 1.3
	var handler = new Xamarin.Android.Net.AndroidMessageHandler();
	client = new HttpClient(handler);
#elif __IOS__
	// iOS uses NSUrlSession by default
	var handler = new NSUrlSessionHandler();
	client = new HttpClient(handler);
#endif
}
```

### Platform-Specific Service Registration

Register platform services in `CreateMauiApp()` using conditional compilation:

```csharp
public static MauiApp CreateMauiApp()
{
	var builder = MauiApp.CreateBuilder();
	builder.UseCometApp<MyApp>();

#if __IOS__
	builder.Services.AddSingleton<IFileService, iOSFileService>();
#elif __ANDROID__
	builder.Services.AddSingleton<IFileService, AndroidFileService>();
#elif WINDOWS
	builder.Services.AddSingleton<IFileService, WindowsFileService>();
#endif

	return builder.Build();
}
```

### Accessing Platform Views from Handlers

Access the underlying platform view through the handler:

```csharp
public class PlatformAccessPage : View
{
	[Body]
	View body() =>
		new NativeHost(mauiContext =>
		{
#if __IOS__
			var label = new UIKit.UILabel
			{
				Text = "Native iOS Label",
				TextColor = UIKit.UIColor.SystemBlue,
				Font = UIKit.UIFont.PreferredBody
			};
			return label;
#elif __ANDROID__
			var textView = new Android.Widget.TextView(
				mauiContext.Context)
			{
				Text = "Native Android TextView"
			};
			return textView;
#elif WINDOWS
			var textBlock = new Microsoft.UI.Xaml.Controls.TextBlock
			{
				Text = "Native WinUI TextBlock"
			};
			return textBlock;
#else
			return new object();
#endif
		}).Frame(height: 44);
}
```


## Platform Handler Summary

Comet's handler architecture maps each platform to a container type:

| Platform | Container Type | Base Class |
|----------|---------------|------------|
| iOS / Mac Catalyst | `CometHostContainerView` | `UIView` |
| Android | `CometHostContainerView` | `FrameLayout` |
| Windows | `CometHostContainerPanel` | `Canvas` |

All three handlers follow the same pattern:

1. `CreatePlatformView()` creates the native container.
2. `ConnectHandler()` resolves the Comet view's body and converts it to a
   platform view via `ToPlatform()`.
3. Layout methods (`LayoutSubviews`, `OnLayout`, `ArrangeOverride`) measure
   and arrange the virtual view, then position the native content.
4. `DisconnectHandler()` clears the content and releases references.

For details on the handler architecture, see the
[Handler Architecture Guide](handlers.md).

The `GetDesiredSize` override on all platforms delegates to
`IContentView.CrossPlatformMeasure()` for proper auto-sizing, falling back to
default dimensions (400x800) when constraints are infinite.


## See Also

- [Handler Architecture](handlers.md) -- how Comet handlers work, property
  mappers, and creating custom platform handlers.
- [MAUI Integration Guide](maui-interop.md) -- embedding native platform views
  in Comet and accessing platform APIs.
- [Contributing Guide](contributing.md) -- platform-specific file conventions
  and code organization guidelines for contributors.
