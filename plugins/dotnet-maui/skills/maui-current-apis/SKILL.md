---
name: maui-current-apis
description: >-
  Guard .NET MAUI code generation and fixes against obsolete Xamarin.Forms-era
  APIs and version-specific MAUI API changes. USE FOR: creating or updating MAUI
  XAML/C#, resolving obsolete warnings, enforcing TFM inspection before any code
  change — never outputs .NET 10-only APIs when the project targets net8 or net9,
  replacing Xamarin.Forms or Xamarin.Essentials patterns, safe area migration
  (SafeAreaEdges for .NET 10+), Shell/window API choices, and preventing
  deprecated agent suggestions. DO NOT USE FOR: SDK/workload version discovery
  (use dotnet-workload-info), runtime UI inspection (use maui-devflow-debug), or
  full Xamarin migration planning.
---

# MAUI Current APIs

Use this skill before changing MAUI app code when API currency matters. The goal
is to inspect the project target first, then select APIs that match that target
instead of generating stale Xamarin.Forms or early MAUI patterns.

## Workflow

1. Inspect the target frameworks and package versions before recommending APIs:

   ```bash
   grep -R -n --include="*.csproj" --include="Directory.Build.props" "<TargetFramework" . 2>/dev/null
   grep -R -n --include="*.csproj" --include="Directory.Build.props" --include="Directory.Packages.props" \
     "Microsoft.Maui.Controls\\|UseMaui\\|MauiVersion" . 2>/dev/null
   ```

2. Identify whether the project targets .NET 8, .NET 9, .NET 10, or multiple
   versions. Do not assume `net10.0`.
3. Prefer APIs from `Microsoft.Maui.*` namespaces. Do not add `Xamarin.Forms` or
   `Xamarin.Essentials` namespaces to MAUI projects.
4. If replacing an obsolete API, explain the replacement and why it matches the
   detected target.
5. Keep platform-specific code behind `#if ANDROID`, `#if IOS`, `#if
   MACCATALYST`, `#if WINDOWS`, or an injected platform service.
6. Build the changed project for the relevant target framework.

## Common Agent Traps

| Avoid | Prefer |
| --- | --- |
| `using Xamarin.Forms;` | `using Microsoft.Maui.Controls;` |
| `using Xamarin.Essentials;` | `using Microsoft.Maui.ApplicationModel`, `Microsoft.Maui.Devices`, `Microsoft.Maui.Storage`, or the specific MAUI namespace |
| `Device.BeginInvokeOnMainThread(...)` | `MainThread.BeginInvokeOnMainThread(...)` |
| `Device.StartTimer(...)` | `Dispatcher.StartTimer(...)` or `IDispatcherTimer` |
| `Device.RuntimePlatform` for platform behavior | `DeviceInfo.Platform`, `OnPlatform`, or compile-time platform services |
| `MessagingCenter` for new app architecture | `WeakReferenceMessenger`, events, interfaces, or another explicit messaging abstraction |
| Old Xamarin.Forms custom renderers for new code | MAUI handlers, property mappers, or platform services |
| Hard-coded safe area padding | `SafeAreaEdges` / safe area APIs for the detected MAUI version |
| Coordinate-based UI automation | Stable `AutomationId` plus DevFlow queries |
| `Application.Current.MainPage = ...` for routine navigation | Shell navigation with registered routes, or set `Window.Page` explicitly for intentional app reset flows |

## Safe Area Guidance

For .NET 10+ safe area work, prefer `SafeAreaEdges` over legacy iOS-only safe
area patterns. Confirm the target framework before changing safe area code:

```xml
<Grid SafeAreaEdges="Container">
    ...
</Grid>
```

Use platform-specific fallbacks only when the app targets an older MAUI version
where the newer API is unavailable.

## Window and Shell Guidance

- In multi-window code, do not blindly rely on a global main page. Use the
  current `Window`, `Shell.Current`, or an injected navigation abstraction that
  matches the app's architecture.
- For Shell apps, use registered routes and `GoToAsync` instead of manually
  replacing root pages unless the app intentionally owns the full window flow.
- For navigation parameters, prefer `IQueryAttributable` or `[QueryProperty]`
  and URL-encode route values when building URI strings.

## Validation Checklist

- The edited project target framework was inspected.
- No `Xamarin.Forms` or `Xamarin.Essentials` namespace was introduced.
- Replacements are compatible with the detected MAUI version.
- Platform-specific code is isolated behind target-specific files, partial
  classes, DI abstractions, or `#if` guards.
- The changed target framework builds.
