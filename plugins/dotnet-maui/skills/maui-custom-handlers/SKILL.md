---
name: maui-custom-handlers
description: >-
  Implement/migrate MAUI visual handlers. USE FOR: `EntryHandler.Mapper.AppendToMapping`, scoped `BorderlessEntry` type guards, renderer-to-handler migration, `ConnectHandler`/`DisconnectHandler` cleanup, `PropertyMapper`, `CommandMapper`, custom `ViewHandler`, `ConfigureMauiHandlers`. DO NOT USE FOR: non-visual platform APIs, full Xamarin migration, native SDK bindings, or backend code.
---

# MAUI Custom Handlers

Use this skill when a MAUI visual element needs native platform behavior. Keep
handlers focused on views. Route non-visual APIs to platform services and route
large native SDK surfaces to slim bindings.

## Response Checklist

- Use handler language explicitly: `AppendToMapping`, `PropertyMapper`,
  `CommandMapper`, and `ViewHandler`.
- For renderer migration, map subscription/setup to `ConnectHandler` and cleanup
  to `DisconnectHandler`.
- Keep mapper changes scoped to the intended control type, not all controls.

## Decision Tree

| Need | Prefer |
| --- | --- |
| Tweak an existing control for the whole app | `EntryHandler.Mapper.AppendToMapping` in `MauiProgram.cs` (or the matching concrete control handler mapper) |
| Tweak only specific control instances | Subclass the control and guard mapper logic with `if (view is MyControl)` |
| Add a reusable cross-platform control | Custom `ViewHandler<TVirtualView,TPlatformView>` |
| Map bindable properties to native properties | `PropertyMapper` entries |
| Invoke actions such as play/pause/scroll | `CommandMapper` entries |
| Use camera, sensors, payment, health, or background APIs | Platform service through DI |
| Wrap a third-party Android/iOS/macOS SDK | Slim binding plus a platform service facade |

## Workflow

1. Inspect the target frameworks and existing UI architecture.
2. Define the cross-platform view API first: bindable properties, commands, and
   events that make sense to app code.
3. Choose global mapper customization, subclass-scoped mapper customization, or a
   full custom handler.
4. Put native implementation in partial handler files or `#if` guarded blocks for
   the exact platforms supported by the project.
5. Register handlers in `MauiProgram.cs`:

   ```csharp
   builder.ConfigureMauiHandlers(handlers =>
   {
       handlers.AddHandler<CameraPreview, CameraPreviewHandler>();
   });
   ```

6. Use `ConnectHandler` to subscribe native events and allocate native resources.
7. Use `DisconnectHandler` to unsubscribe, stop timers, clear delegates, and
   dispose only native objects owned by the handler. Call
   `base.DisconnectHandler(platformView)` as the final statement so the base
   handler clears its own state.
8. Build each targeted platform and verify the behavior with UI automation or
   DevFlow when available.

## Scoped Mapper Customization

For an existing MAUI control, avoid an unscoped global mapper when only one
control instance should change:

```csharp
public class BorderlessEntry : Entry
{
}

EntryHandler.Mapper.AppendToMapping("Borderless", (handler, view) =>
{
    if (view is not BorderlessEntry)
        return;

#if ANDROID
    handler.PlatformView.Background = null;
#elif IOS || MACCATALYST
    handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#elif WINDOWS
    handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
#endif
});
```

Use a unique mapping key. Do not repeatedly append the same mapping from page
constructors; register once during app startup or from a guarded initialization
path.

## Custom Handler Shape

```csharp
public class MeterView : View
{
    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(double), typeof(MeterView), 0d);

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}

public partial class MeterViewHandler
{
    public static readonly IPropertyMapper<MeterView, MeterViewHandler> Mapper =
        new PropertyMapper<MeterView, MeterViewHandler>(ViewHandler.ViewMapper)
        {
            [nameof(MeterView.Value)] = MapValue
        };

    public MeterViewHandler() : base(Mapper)
    {
    }

    public static partial void MapValue(MeterViewHandler handler, MeterView view);
}
```

Implement `CreatePlatformView`, `ConnectHandler`, `DisconnectHandler`, and
mapping partials per platform. Put the `ViewHandler<TVirtualView, TPlatformView>`
base class on the platform partial so native types stay out of shared files:

```csharp
// Platforms/Android/MeterViewHandler.android.cs
public partial class MeterViewHandler : ViewHandler<MeterView, Android.Widget.FrameLayout>
{
    protected override Android.Widget.FrameLayout CreatePlatformView() => new(Context);

    public static partial void MapValue(MeterViewHandler handler, MeterView view)
    {
        // Update handler.PlatformView from view.Value.
    }
}
```

## Property and Command Mappers

- Use `PropertyMapper` for state that should update when a bindable property
  changes.
- Use `CommandMapper` for imperative requests such as `Play`, `Pause`,
  `ScrollTo`, or `Reload`.
- Mapper methods should be idempotent. They may be called more than once.
- Validate `handler.PlatformView` and `handler.VirtualView` assumptions through
  types, not broad try/catch blocks.

## Renderer Migration Notes

When replacing Xamarin.Forms renderers:

1. Move renderer logic that creates native controls into `CreatePlatformView`.
2. Move `OnElementChanged` subscriptions into `ConnectHandler`.
3. Move renderer cleanup into `DisconnectHandler`, and end with
   `base.DisconnectHandler(platformView)`.
4. Replace `OnElementPropertyChanged` switch statements with `PropertyMapper`
   entries.
5. Replace renderer actions with `CommandMapper` entries or view methods that
   call `Handler?.Invoke`.

## Validation Checklist

- The handler is registered in one startup location.
- Mapper keys are unique and scoped when customization should not be global.
- Native event subscriptions are unsubscribed in `DisconnectHandler`.
- No platform namespace leaks into shared code unintentionally.
- The implementation builds for every target framework it is included in.
- Non-visual SDK work is not hidden in a handler.
