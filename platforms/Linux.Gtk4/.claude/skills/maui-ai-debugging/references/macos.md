# macOS (AppKit) Platform Guide

Platform-specific setup and usage for .NET MAUI apps running on macOS via Platform.Maui.MacOS (AppKit).

## Table of Contents
- [Overview](#overview)
- [Project Structure](#project-structure)
- [NuGet Packages](#nuget-packages)
- [Registration](#registration)
- [Building and Running](#building-and-running)
- [Blazor Hybrid](#blazor-hybrid)
- [Platform Differences](#platform-differences)
- [Troubleshooting](#troubleshooting)

## Overview

macOS (AppKit) apps use the community `Platform.Maui.MacOS` packages to run MAUI on native
AppKit (not Mac Catalyst). The TFM is `net10.0-macos`. Like Linux/GTK, macOS apps typically
use a **separate app head project** rather than adding `-macos` to the standard MAUI project's
TargetFrameworks.

Detect macOS projects:
```bash
grep -i 'Platform\.Maui\.MacOS\|net.*-macos' *.csproj Directory.Build.props 2>/dev/null
```

## Project Structure

macOS apps use a separate app head project (similar to Linux/GTK):

```
src/
├── MyApp/                    # Standard MAUI project (iOS, Android, Mac Catalyst, Windows)
├── MyApp.MacOS/              # macOS AppKit app head
│   ├── Program.cs            # Entry point: MacOSMauiApplication
│   ├── MauiProgram.cs        # macOS-specific builder (UseMauiAppMacOS, AddMacOSEssentials)
│   └── MyApp.MacOS.csproj    # References Platform.Maui.MacOS packages
```

Shared source files (pages, view models, services) are typically linked from the main project.

## NuGet Packages

The app project needs the Platform.Maui.MacOS packages plus the standard MauiDevFlow packages:

```xml
<ItemGroup>
  <!-- macOS platform -->
  <PackageReference Include="Platform.Maui.MacOS" Version="*" />
  <PackageReference Include="Platform.Maui.MacOS.BlazorWebView" Version="*" />  <!-- Blazor only -->
  <PackageReference Include="Platform.Maui.Essentials.MacOS" Version="*" />

  <!-- MauiDevFlow (standard packages — they include net10.0-macos support) -->
  <PackageReference Include="Redth.MauiDevFlow.Agent" Version="*" />
  <PackageReference Include="Redth.MauiDevFlow.Blazor" Version="*" />  <!-- Blazor only -->
</ItemGroup>
```

The standard `Redth.MauiDevFlow.Agent` and `Redth.MauiDevFlow.Blazor` packages include
`net10.0-macos` targets — no separate macOS-specific MauiDevFlow packages needed.

## Registration

### Program.cs (Entry Point)

```csharp
using AppKit;
using Microsoft.Maui.Platform.MacOS;
using ObjCRuntime;

namespace MyApp.MacOS;

[Register("Program")]
public class Program : MacOSMauiApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    static void Main(string[] args)
    {
        NSApplication.Init();
        NSApplication.Main(args);
    }
}
```

The `[Register]` attribute is required.

### MauiProgram.cs

```csharp
using Microsoft.Maui.Platform.MacOS;
using Microsoft.Maui.Platform.MacOS.Controls;

#if DEBUG
using MauiDevFlow.Agent;
using MauiDevFlow.Blazor;
#endif

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiAppMacOS<App>()    // NOT UseMauiApp — macOS-specific
            .AddMacOSEssentials()       // REQUIRED — without this, no window appears
            .AddMacOSBlazorWebView()    // Blazor only
            .ConfigureFonts(fonts => { /* ... */ });

#if DEBUG
        builder.AddMauiDevFlowAgent();
        builder.AddMauiBlazorDevFlowTools();  // Blazor only
#endif

        return builder.Build();
    }
}
```

**Critical:** `AddMacOSEssentials()` is required — without it the app runs but no window appears.

## Building and Running

macOS apps do NOT use `-t:Run`. Build first, then launch with `open`:

```bash
# Build
dotnet build -f net10.0-macos path/to/MyApp.MacOS

# Find and launch the .app bundle
open path/to/MyApp.MacOS/bin/Debug/net10.0-macos/osx-arm64/AppName.app
```

**Code signing:** A clean `dotnet build` produces a valid ad-hoc signature. Do NOT manually
re-sign the app — it breaks the signature (SIGKILL on launch). If the app fails to launch,
clean rebuild: `rm -rf bin obj && dotnet build`.

**Finding the .app bundle:**
```bash
find bin/Debug/net10.0-macos -name "*.app" -maxdepth 3
```

## Network Setup

**No special setup needed.** macOS apps run directly on localhost — the CLI connects
directly to `http://localhost:<port>`. No port forwarding or entitlements required.

## Blazor Hybrid

Use `MacOSBlazorWebView` instead of the standard `BlazorWebView`:

```csharp
using Microsoft.Maui.Platform.MacOS.Controls;

// In page code-behind, replace BlazorWebView with MacOSBlazorWebView
var blazorWebView = new MacOSBlazorWebView();
blazorWebView.HostPage = "wwwroot/index.html";
blazorWebView.RootComponents.Add(new RootComponent { ... });
```

Chobitsu.js is auto-injected via the Blazor JS module initializer — no manual `<script>` tag needed.

## Platform Differences

| Feature | Mac Catalyst | macOS (AppKit) |
|---------|-------------|----------------|
| TFM | `net10.0-maccatalyst` | `net10.0-macos` |
| Base framework | UIKit via Catalyst | AppKit |
| Packages | Standard MAUI | Platform.Maui.MacOS |
| Project structure | Standard MAUI single-project | Separate app head project |
| Build + Run | `dotnet build -t:Run` | `dotnet build` then `open App.app` |
| Entry point | Standard MAUI | `MacOSMauiApplication` with `[Register]` |
| Builder | `UseMauiApp<App>()` | `UseMauiAppMacOS<App>()` |
| Essentials | Built-in | `AddMacOSEssentials()` (required) |
| BlazorWebView | Standard `BlazorWebView` | `MacOSBlazorWebView` |
| Entitlements | Required (`network.server`) | Not needed |
| Native sidebar | N/A | `MacOSShell.SetUseNativeSidebar(shell, true)` |

## Troubleshooting

### App Launches But No Window Appears
**Cause:** Missing `AddMacOSEssentials()` call in MauiProgram.cs.
**Fix:** Add `.AddMacOSEssentials()` to the builder chain.

### SIGKILL on Launch (Code Signature Invalid)
**Cause:** Manually re-signing the app bundle or corrupted build artifacts.
**Fix:** Clean rebuild: `rm -rf bin obj && dotnet build -f net10.0-macos`.
Never use `codesign` manually — the build produces a valid ad-hoc signature.

### Blazor Page Shows "Loading..." Indefinitely
**Cause:** Using standard `BlazorWebView` instead of `MacOSBlazorWebView`.
**Fix:** Replace `BlazorWebView` with `MacOSBlazorWebView` from `Microsoft.Maui.Platform.MacOS.Controls`.

### No Shell Sidebar Content
**Cause:** macOS Shell needs explicit native sidebar configuration.
**Fix:** In AppShell code-behind:
```csharp
FlyoutBehavior = FlyoutBehavior.Locked;
MacOSShell.SetUseNativeSidebar(this, true);
```

### Agent Not Connecting
1. Ensure the app launched successfully (window appeared)
2. Check `maui-devflow list` — agent should register within a few seconds
3. If using an older build, clean and rebuild to pick up latest agent code
