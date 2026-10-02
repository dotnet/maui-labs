# Scene lifecycle audit

Inspect the exact MAUI package source/API for the app's release. These are
conceptual mappings, not a search-and-replace script.

| Existing application callback | Scene registration | Important difference |
|---|---|---|
| OnActivated | SceneOnActivated | UIScene instead of UIApplication |
| OnResignActivation | SceneOnResignActivation | UIScene instead of UIApplication |
| DidEnterBackground | SceneDidEnterBackground | Per-scene, not process-wide |
| WillEnterForeground | SceneWillEnterForeground | Per-scene, potentially multiple windows |
| OpenUrl | SceneOpenUrl | UIScene and NSSet&lt;UIOpenUrlContext&gt;, not a single URL/options pair; returns bool |
| ContinueUserActivity | SceneContinueUserActivity | UIScene and NSUserActivity; returns bool, without the old restoration callback |
| FinishedLaunching | Keep application startup | AppDelegate.Window is null under scenes; move window work later |
| PerformActionForShortcutItem override | iOSLifecycle.PerformActionForShortcutItem | Registration takes UIApplication, UIApplicationShortcutItem, UIOperationHandler; no custom AppDelegate override dispatch in scene mode |

Configure these through `builder.ConfigureLifecycleEvents(events => events.AddiOS(...))`
using `Microsoft.Maui.LifecycleEvents` and appropriate `#if IOS || MACCATALYST`
guards. Do not erase unrelated Android/Windows registrations.

Do not leave custom activation/background behavior solely in AppDelegate
because it compiled before migration. In scene mode the scene owns these
transitions: move that behavior to the corresponding Scene* registration (or
the verified scene override), explicitly deciding its per-window semantics.
Retaining an old override as a compatibility fallback is not a substitute for
wiring the scene path. Verify that the original side effects still occur.

The MAUI lifecycle delegates `SceneOpenUrl(UIScene, NSSet<UIOpenUrlContext>)`
and `SceneContinueUserActivity(UIScene, NSUserActivity)` return **bool**. Return
whether the app handled the input. Do not infer a MauiUISceneDelegate override
signature from native UIKit documentation: inspect the installed MAUI base
class or its exact release source before generating an override. Inspect all
URL contexts instead of treating the set as a single URL.

## Custom delegates

MauiUISceneDelegate owns window creation and raises the scene lifecycle events.
When overriding methods declared on the installed MAUI scene delegate, retain the base
implementation. Preserve application-specific logic, guard against duplicate
delivery, and explicitly decide whether it belongs to one scene or all scenes.
Avoid registering the same handler through both overrides and lifecycle events.

In MAUI 10.0.110, the managed activation override is `OnActivated(UIScene)`,
not `DidBecomeActive`. Always use the declared MAUI method, not the Objective-C
selector's spelling.

The tagged 10.0.110 class derives from `UIResponder` and implements
`IUIWindowSceneDelegate`; its warm-link overrides are
`public override bool OpenUrl(UIScene scene, NSSet<UIOpenUrlContext> urlContexts)`
and `public override bool ContinueUserActivity(UIScene scene, NSUserActivity userActivity)`.
It does not expose `override void OpenUrlContexts(...)`. Call the respective
base method unconditionally before combining its result with custom handling;
`customHandled || base.OpenUrl(...)` would skip MAUI/Essentials forwarding when
the custom handler returns true. Prefer Scene* registrations when no override
is needed.

Configuration names also affect behavior: MAUI's default `GetConfiguration`
returns `__MAUI_DEFAULT_SCENE_CONFIGURATION__`, and the base `WillConnect`
creates the MAUI window only for that name. A custom **registered delegate**
can keep the default configuration name and use the normal window path.
For an existing different configuration name, inspect the app's dynamic
`GetConfiguration` and manual window creation before claiming it is valid.
Preserve intentional custom behavior; do not silently rename configurations or
assume any manifest automatically results in a MAUI window.

`SceneWillConnect(UIScene, UISceneSession, UISceneConnectionOptions)` is raised
before window creation. Extract `UrlContexts` and `UserActivities` into pending
app-specific work and drain it when the intended MAUI Window/navigation host is
ready. Cover both URL contexts and user activities; migrating only URL contexts
can still lose cold universal links. Do not navigate immediately from
SceneWillConnect or FinishedLaunching.
Do not assume a later SceneOpenUrl event repeats a cold link.

Check native collection types before applying LINQ. The cold connection-option
properties can be non-generic Foundation `NSSet`, unlike the typed
`NSSet<UIOpenUrlContext>` passed to `SceneOpenUrl`. With those bindings, use
`connectionOptions.UrlContexts?.ToArray<UIOpenUrlContext>() ?? []` and
`connectionOptions.UserActivities?.ToArray<NSUserActivity>() ?? []` to obtain typed
elements before enumeration/LINQ. Treat an absent collection as no cold payload:
an ordinary launch without a URL must still reach `base.WillConnect`, even if
the native getter returns null despite its binding annotation.
A direct `UrlContexts.FirstOrDefault()` can
fail to compile; a non-generic enumeration can lose the element's `Url` API.
Use `Foundation` and `UIKit` and verify against the actual Apple reference pack.

For warm links, inspect every relevant URL context/user activity and route only
recognized app links. Preserve scheme/universal-link validation and entitlements.
Essentials WebAuthenticator forwarding handles its pending authentication flow,
not the app's independent routing contract.

## Quick-action completion ownership

The default MAUI builder already registers Essentials forwarding. Keep it
exactly once by relying on that registration; do not call
`Platform.PerformActionForShortcutItem` in a second custom handler.

A logging-only custom observer still completes its own callback:

```csharp
#if IOS || MACCATALYST
builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios
    .PerformActionForShortcutItem((application, shortcut, completionHandler) =>
    {
        System.Diagnostics.Debug.WriteLine($"Quick action: {shortcut.Type}");
        completionHandler(false);
    })));
#endif
```

A real custom handler reports true only when it handles the action, false for
an unknown action or a failed attempt. Each registration completes exactly once;
do not omit its completion because the default Essentials handler also runs.
For async logic, use a single completion site after an awaited handled/unhandled
decision, account for exceptions with repository-standard logging, and avoid
early completion followed by a second completion in a finally block.

## Tagged implementation references

- [MauiUISceneDelegate](https://github.com/dotnet/maui/blob/10.0.110/src/Core/src/Platform/iOS/MauiUISceneDelegate.cs)
- [iOSLifecycle](https://github.com/dotnet/maui/blob/10.0.110/src/Core/src/LifecycleEvents/iOS/iOSLifecycle.cs)
- [Essentials integration](https://github.com/dotnet/maui/blob/10.0.110/src/Core/src/Hosting/EssentialsMauiAppBuilderExtensions.cs)
- [MauiUIApplicationDelegate](https://github.com/dotnet/maui/blob/10.0.110/src/Core/src/Platform/iOS/MauiUIApplicationDelegate.cs)
