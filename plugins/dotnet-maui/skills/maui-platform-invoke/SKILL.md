---
name: maui-platform-invoke
description: >-
  Add native platform APIs through MAUI services/lifecycle hooks. USE FOR: DI wrappers (`IAppReviewService`, `ICameraService`), `Permissions.CheckStatusAsync`/`RequestAsync`, AndroidManifest.xml, Info.plist, entitlements, partial platform files, `ConfigureLifecycleEvents` `AddAndroid`/`AddiOS`/`AddWindows`, services vs handlers vs bindings. DO NOT USE FOR: visual handlers, native SDK bindings, backend.
---

# MAUI Platform Invoke

Use this skill when a MAUI app needs platform APIs that are not already exposed
by a cross-platform MAUI API. Prefer small, testable abstractions over scattered
`#if` blocks.

## Response Checklist

- Define a DI abstraction first (for example `IAppReviewService`) and inject it
  into app services or view models.
- Mention required permission flow and platform metadata files
  (`AndroidManifest.xml`, `Info.plist`, capabilities/entitlements).
- Put lifecycle guidance in `ConfigureLifecycleEvents` (`AddAndroid`, `AddiOS`,
  `AddWindows`) instead of page constructors.

## Choose the Right Extension Point

| Need | Prefer |
| --- | --- |
| Existing cross-platform Essentials API covers it | `Microsoft.Maui.ApplicationModel`, `Devices`, `Storage`, etc. |
| Non-visual platform API or OS service | DI interface with per-platform implementation |
| Native view/control behavior | Handler mapper or custom handler |
| Platform lifecycle callback | `ConfigureLifecycleEvents` |
| Third-party SDK with many native types | Slim binding plus a narrow app service |
| One small compile-time constant | `#if` or `OnPlatform` |

## Platform Service Pattern

1. Define an interface in shared code:

   ```csharp
   public interface IAppReviewService
   {
       Task RequestReviewAsync(CancellationToken cancellationToken = default);
   }
   ```

2. Implement it per platform using target-specific files:

   ```csharp
   // Platforms/Android/AppReviewService.android.cs
   public sealed class AppReviewService : IAppReviewService
   {
       public Task RequestReviewAsync(CancellationToken cancellationToken = default)
       {
           // Call Android APIs or a bound SDK here.
           return Task.CompletedTask;
       }
   }
   ```

3. Register only the platforms that have implementations in `MauiProgram.cs`:

   ```csharp
   #if ANDROID
   builder.Services.AddSingleton<IAppReviewService, AppReviewService>();
   #endif
   ```

   When adding iOS, Mac Catalyst, Windows, or MAUI Labs AppKit support, add the
   matching platform implementation file first and then extend the guard, for
   example `#if IOS || MACCATALYST` or `#if MACOS`.

4. Inject `IAppReviewService` into view models or application services. Keep page
   constructors simple.

## Partial Classes and Conditional Compilation

- Prefer platform-specific files under `Platforms/<Platform>/` for code that
  imports native namespaces.
- Use `partial` classes when one cross-platform type needs per-platform method
  bodies.
- Use `#if ANDROID`, `#if IOS`, `#if MACCATALYST`, `#if IOS || MACCATALYST`,
  `#if MACOS`, and `#if WINDOWS` intentionally. iOS and Mac Catalyst often share
  UIKit APIs, but AppKit macOS does not.
- Avoid `Device.RuntimePlatform` for behavior that can be decided at compile
  time.

## Permissions and Capabilities

Before calling a platform API:

1. Check whether MAUI has a `Permissions.*` helper for the capability.
2. Add required manifest, Info.plist, entitlements, or package declarations.
3. Request permission from UI-safe code before invoking the service.
4. Handle denied/restricted states explicitly and surface user-actionable
   recovery instructions.

```csharp
var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
if (status != PermissionStatus.Granted)
    status = await Permissions.RequestAsync<Permissions.Camera>();

if (status != PermissionStatus.Granted)
{
    if (Shell.Current is not null)
    {
        await Shell.Current.DisplayAlert(
            "Camera permission required",
            "Enable camera access in Settings to use this feature.",
            "OK");
    }
    return;
}
```

Do not swallow permission failures or return fake success. In a service layer,
return a result such as `PermissionStatus` or `bool` and let the caller surface
the denial in UI.

## Lifecycle Hooks

Use lifecycle hooks when the native API depends on app/window lifecycle:

```csharp
builder.ConfigureLifecycleEvents(events =>
{
#if ANDROID
    events.AddAndroid(android => android
        .OnResume(activity => { /* refresh platform state */ }));
#elif IOS || MACCATALYST
    events.AddiOS(ios => ios
        .OnActivated(application => { /* refresh platform state */ }));
#elif WINDOWS
    events.AddWindows(windows => windows
        .OnWindowCreated(window => { /* configure native window */ }));
#endif
});
```

Keep lifecycle code small. Forward work into registered services when state must
be shared with view models.

## Validation Checklist

- A cross-platform interface isolates native API calls.
- Platform implementation files compile only for their intended target.
- Required permissions, manifests, plist entries, entitlements, or capabilities
  are documented and added.
- Denied permissions are handled explicitly.
- Native lifecycle subscriptions are paired with cleanup where applicable.
- Visual customization is not implemented as a generic platform service.
