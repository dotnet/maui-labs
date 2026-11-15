# Known MAUI Extensibility Gaps & Workarounds

These are known issues where MAUI's architecture makes it difficult for third-party backends. Track [dotnet/maui#34099](https://github.com/dotnet/maui/issues/34099) for the umbrella issue.

---

## Gap 1: Essentials Static `Default` Properties

**Issue**: [dotnet/maui#34100](https://github.com/dotnet/maui/issues/34100)

**Status**: ✅ Resolved in MAUI 11 by the public `builder.ConfigureEssentials()`.

Register each implementation in DI and apply `ConfigureEssentials()` (MAUI's default builder already does). During `Build()`, MAUI's Essentials initializer assigns every registered Essentials interface to its static facade (`Preferences.Default`, `AppInfo.Current`, `WebAuthenticator.Default`, `AppActions.Current`, ...) and restores the previous facades when the app is disposed. No reflection:

```csharp
builder.Services.TryAddSingleton<IPreferences, MyPreferencesImpl>();
// ... one TryAddSingleton per service ...
builder.ConfigureEssentials();
```

---

## Gap 2: `MainThread.BeginInvokeOnMainThread`

**Issue**: [dotnet/maui#34101](https://github.com/dotnet/maui/issues/34101)

**Status**: ✅ Resolved in MAUI 11. `ConfigureEssentials()`'s initializer bridges `MainThread.IsMainThread` and `BeginInvokeOnMainThread` from the application dispatcher: the keyed `IDispatcher` registered under `typeof(IApplication)`, otherwise the dispatcher the backend's `IDispatcherProvider` returns on the thread that builds the app. Register a thread-independent application dispatcher in the backend's `UseMauiApp*` extension:

```csharp
builder.Services.TryAddKeyedSingleton<IDispatcher>(typeof(IApplication), (_, _) => new MyUiThreadDispatcher());
builder.ConfigureEssentials();
```

If the backend wraps the service provider in its own `IMauiContext`, implement `IKeyedServiceProvider` on the wrapper so keyed lookups reach the container.

---

## Gap 3: Resizetizer Extensibility

**Issues**: [dotnet/maui#34102](https://github.com/dotnet/maui/issues/34102), [dotnet/maui#34222](https://github.com/dotnet/maui/issues/34222)

**Status**:
- **#34102 (Standalone access)**: ✅ **CLOSED/FIXED** — Resizetizer can now be referenced independently
- **#34222 (Extension targets)**: 🔴 **OPEN** — no official hook targets or public item group contracts

**Problem**: `ResizetizeImages` target couples processing and platform-specific output injection in one step. No `DependsOnTargets` properties or hook targets for custom backends.

**Workaround** (for #34222):
```xml
<!-- Hook into Resizetizer output using AfterTargets -->
<Target Name="_MyPlatformInjectImages"
        AfterTargets="ResizetizeImages">
  <ItemGroup>
    <!-- Consume the internal item group (fragile — could change) -->
    <MyPlatformResource Include="@(_ResizetizerCollectedImages)" />
  </ItemGroup>
</Target>
```

**For app icons**: Implement a custom build target — icon formats are highly platform-specific. See `platforms/Linux.Gtk4/src/Linux.Gtk4/buildTransitive/` for the GTK4 approach (hicolor icon theme).

---

## Gap 4: BlazorWebView Registration

**Issue**: [dotnet/maui#34103](https://github.com/dotnet/maui/issues/34103)

**Problem**: `AddMauiBlazorWebView()` only registers handlers for built-in platforms (iOS, Android, Windows, macOS Catalyst). Custom backends must bypass it entirely.

**Workaround**:
```csharp
// In MauiProgram.cs — conditionally bypass:
#if MY_PLATFORM
    builder.Services.AddMyPlatformBlazorWebView();
#else
    builder.Services.AddMauiBlazorWebView();
#endif

// Your extension method replicates the shared service registrations
// from AddMauiBlazorWebView() and adds your platform handler.
```

**Reference**: See `platforms/Linux.Gtk4/src/Linux.Gtk4.BlazorWebView/BlazorWebViewExtensions.cs` for the GTK4 approach.

---

## Gap 5: Alert/Dialog System

**Issue**: [dotnet/maui#34104](https://github.com/dotnet/maui/issues/34104)

**Status**: ✅ Resolved in MAUI 11 — `Microsoft.Maui.Controls.Platform.IAlertManagerSubscription` is public. Implement `OnAlertRequested`, `OnActionSheetRequested` and `OnPromptRequested`, show native dialogs and complete the arguments' result, then register the subscription as a DI singleton:

```csharp
services.AddSingleton<IAlertManagerSubscription, MyAlertSubscription>();
```

MAUI's `Window` subscribes its `AlertManager` when the page handler changes, so the backend needs no reflection to call `Subscribe()`.

**Reference**: See `platforms/Linux.Gtk4/src/Linux.Gtk4/Platform/GtkAlertManager.cs` for a complete implementation.

---

## Gap 6: Reference Assembly Exceptions

**Related to**: [dotnet/maui#34222](https://github.com/dotnet/maui/issues/34222)

**Problem**: MAUI's reference assemblies throw `NotImplementedInReferenceAssemblyException` when methods are called on unrecognized platforms. This happens when services aren't properly wired up before the app accesses them.

**Workaround**: Ensure ALL service implementations are wired in via both:
1. DI registration (`services.TryAddSingleton<IFoo, MyFoo>()`)
2. `builder.ConfigureEssentials()`, which assigns Essentials registrations to the static facades during `Build()`

Guard app code that might run before DI is configured. Register services in the app host builder extension method, which runs early in the lifecycle.

---

## Summary

| Gap | Issue | Status | Workaround Complexity |
|-----|-------|--------|-----------------------|
| Essentials static facades | #34100 | ✅ Fixed (MAUI 11 `ConfigureEssentials`) | N/A |
| MainThread | #34101 | ✅ Fixed (MAUI 11 dispatcher bridge) | N/A |
| Resizetizer standalone | #34102 | ✅ Fixed | N/A |
| Resizetizer extension | #34222 | 🔴 Open | Medium (AfterTargets hook) |
| BlazorWebView | #34103 | 🔴 Open | Medium (replicate registrations) |
| Alert/Dialog | #34104 | ✅ Fixed (MAUI 11 public `IAlertManagerSubscription`) | N/A |
| Reference assemblies | Related | N/A | Low (ensure early registration) |
