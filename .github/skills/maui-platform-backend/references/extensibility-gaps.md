# Known MAUI Extensibility Gaps & Workarounds

These are known issues where MAUI's architecture makes it difficult for third-party backends. Track [dotnet/maui#34099](https://github.com/dotnet/maui/issues/34099) for the umbrella issue.

---

## Gap 1: Essentials Static `Default` Properties

**Issue**: [dotnet/maui#34100](https://github.com/dotnet/maui/issues/34100)

**Problem**: `Preferences.Default`, `FilePicker.Default`, `Clipboard.Default`, etc. use internal `SetDefault()` methods. DI registration alone is NOT sufficient — static properties resolve to reference assembly stubs that throw `NotImplementedInReferenceAssemblyException`.

**Impact**: Any app code using `XXX.Default` (which is common and documented) crashes on custom backends.

**Workaround**:
```csharp
// For EACH Essentials service, register in DI AND set the static default via reflection:
services.AddSingleton<IPreferences>(new MyPreferencesImpl());

var setDefault = typeof(Preferences).GetMethod("SetDefault",
    BindingFlags.Static | BindingFlags.NonPublic);
setDefault?.Invoke(null, new object[] { new MyPreferencesImpl() });
```

**Best practice**: Wrap all reflection calls in try/catch — internal API could change at any time.

---

## Gap 2: `MainThread.BeginInvokeOnMainThread`

**Issue**: [dotnet/maui#34101](https://github.com/dotnet/maui/issues/34101)

**Problem**: `MainThread.BeginInvokeOnMainThread()` throws on custom backends even though `Application.Current.Dispatcher` works fine.

**Workaround**:
```csharp
// Replace MainThread usage with Dispatcher throughout your codebase:
// ❌ MainThread.BeginInvokeOnMainThread(() => { ... });
// ✅ Application.Current?.Dispatcher.Dispatch(() => { ... });
```

**Recommendation**: Document for app developers that `Dispatcher` should be used instead of `MainThread` for cross-platform compatibility with custom backends.

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

**Problem**: `AlertManager` and `IAlertManagerSubscription` are internal types. Custom backends cannot subscribe to dialog requests through any public API.

**Workaround**: Create a `DispatchProxy` implementation that intercepts method calls on the internal interface:

```csharp
// 1. Get the internal types via reflection
var amType = typeof(Window).Assembly
    .GetType("Microsoft.Maui.Controls.Platform.AlertManager");
var iamsType = amType.GetNestedType("IAlertManagerSubscription",
    BindingFlags.Public | BindingFlags.NonPublic);

// 2. Create a DispatchProxy that intercepts the three methods:
//    - OnAlertRequested
//    - OnActionSheetRequested
//    - OnPromptRequested
var proxy = DispatchProxy.Create(iamsType, typeof(YourAlertProxy));

// 3. Register the proxy in DI
services.AddSingleton(iamsType, proxy);

// 4. In your proxy's Invoke method, extract parameters via reflection,
//    show native dialogs, and complete the TaskCompletionSource result
```

**Reference**: See `platforms/Linux.Gtk4/src/Linux.Gtk4/Platform/GtkAlertManager.cs` for a complete implementation.

---

## Gap 6: Reference Assembly Exceptions

**Related to**: [dotnet/maui#34222](https://github.com/dotnet/maui/issues/34222)

**Problem**: MAUI's reference assemblies throw `NotImplementedInReferenceAssemblyException` when methods are called on unrecognized platforms. This happens when services aren't properly wired up before the app accesses them.

**Workaround**: Ensure ALL service implementations are wired in via both:
1. DI registration (`services.AddSingleton<IFoo>(new MyFoo())`)
2. Static `SetDefault()` reflection (for Essentials services)

Guard app code that might run before DI is configured. Register services in the app host builder extension method, which runs early in the lifecycle.

---

## Summary

| Gap | Issue | Status | Workaround Complexity |
|-----|-------|--------|-----------------------|
| Essentials SetDefault | #34100 | 🔴 Open | Medium (reflection per service) |
| MainThread | #34101 | 🔴 Open | Low (use Dispatcher) |
| Resizetizer standalone | #34102 | ✅ Fixed | N/A |
| Resizetizer extension | #34222 | 🔴 Open | Medium (AfterTargets hook) |
| BlazorWebView | #34103 | 🔴 Open | Medium (replicate registrations) |
| Alert/Dialog | #34104 | 🔴 Open | High (DispatchProxy + heavy reflection) |
| Reference assemblies | Related | N/A | Low (ensure early registration) |
