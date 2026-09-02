---
name: maui-labs-platform-targeting
description: >-
  Target MAUI Labs desktop backends from MAUI apps. USE FOR: Linux GTK4 head projects, `maui-linux-gtk4`, `maui-macos`, `maui-wpf`, net10 desktop setup, `UseMauiAppLinuxGtk4`, AppKit vs Mac Catalyst, parity planning. DO NOT USE FOR: backend implementation, handlers, Essentials services.
---

# MAUI Labs Platform Targeting

Use this skill when an app developer wants to run a MAUI app on experimental
MAUI Labs backends such as Linux GTK4, native macOS AppKit, or WPF. This is for
consuming backends, not implementing them.

## Response Checklist

- Keep backend terms explicit: `maui-linux-gtk4`, `maui-macos`, `maui-wpf`.
- Show the hosting call for the selected backend (`UseMauiAppLinuxGtk4`,
  `UseMauiAppMacOS`, or `UseMauiAppWPF`).
- Distinguish AppKit (`MACOS`) from Mac Catalyst and call out experimental parity
  validation.

## Workflow

1. Confirm the app target: Linux GTK4, macOS AppKit, WPF, or a comparison between
   those experimental backends.
2. Choose the template/head-project pattern for that backend before editing app
   code.
3. Wire the backend-specific hosting call and optional Essentials package.
4. Audit shared code for platform conditionals and unsupported Essentials APIs.
5. Build and launch the selected backend, then record experimental parity gaps.

## Platform Choices

| Target | When to choose it | Key package/template |
| --- | --- | --- |
| Linux GTK4 | Native Linux desktop app using GTK4 widgets | `Microsoft.Maui.Platforms.Linux.Gtk4` / `maui-linux-gtk4` |
| macOS AppKit | Native AppKit app instead of Mac Catalyst | `Microsoft.Maui.Platforms.MacOS` / `maui-macos` |
| WPF | Windows desktop app using WPF instead of WinUI | `Microsoft.Maui.Platforms.Windows.WPF` / `maui-wpf` |

These backends are experimental. Ask the user which controls, Essentials APIs,
and desktop integrations are required before promising parity.

## Linux GTK4

Linux uses a separate head project because there is no official `net10.0-linux`
MAUI TFM.

Ubuntu/Debian:

```bash
sudo apt install libgtk-4-dev libwebkitgtk-6.0-dev \
  gobject-introspection libgirepository1.0-dev \
  gir1.2-gtk-4.0 gir1.2-webkit-6.0 pkg-config
```

Adjust package names for other distributions, for example Fedora/RHEL commonly
use `gtk4-devel` and `webkitgtk6.0-devel`.

```bash
dotnet new install Microsoft.Maui.Platforms.Linux.Gtk4.Templates --prerelease
dotnet new maui-linux-gtk4 -n MyApp.Linux
dotnet run --project MyApp.Linux
```

For an existing app, create `MyApp.Linux` next to the shared MAUI project and
reference the shared app/project. Pin concrete package versions for production;
the version below is a placeholder to replace with the selected prerelease:

```xml
<ProjectReference Include="../MyApp/MyApp.csproj" />
<PackageReference Include="Microsoft.Maui.Platforms.Linux.Gtk4" Version="0.6.0-preview.1" />
<PackageReference Include="Microsoft.Maui.Platforms.Linux.Gtk4.Essentials" Version="0.6.0-preview.1" />
```

```csharp
builder
    .UseMauiAppLinuxGtk4<App>()
    .AddLinuxGtk4Essentials();
```

If the app does not need Linux Essentials services, omit the Essentials package
and `AddLinuxGtk4Essentials()` call.

## macOS AppKit

Use this when the app should be a native AppKit app, not Mac Catalyst.

```bash
dotnet new install Microsoft.Maui.Platforms.MacOS.Templates --prerelease
dotnet new maui-macos -n MyApp.MacOS
dotnet run --project MyApp.MacOS
```

Manual projects target `net10.0-macos`, reference the AppKit backend packages,
and call:

```csharp
builder
    .UseMauiAppMacOS<App>()
    .AddMacOSEssentials();
```

AppKit has different UI conventions and APIs than Mac Catalyst. Do not reuse
UIKit-specific `IOS || MACCATALYST` code for AppKit without a separate `MACOS`
path.

## WPF

Use WPF when the app needs WPF hosting or desktop integration instead of WinUI:

```bash
dotnet new install Microsoft.Maui.Platforms.Windows.WPF.Templates --prerelease
dotnet new maui-wpf -n MyApp.WPF
dotnet run --project MyApp.WPF
```

Manual projects target `net10.0-windows`, set both `UseWPF` and `UseMaui`,
reference the WPF backend packages, and call:

```csharp
builder
    .UseMauiAppWPF<App>()
    .UseWPFEssentials();
```

## App Adaptation Checklist

- Factor shared UI, view models, services, and resources so experimental head
  projects can reuse them.
- Audit platform-specific code for `ANDROID`, `IOS`, `MACCATALYST`, `WINDOWS`,
  and `MACOS` assumptions. Add separate paths for AppKit, WPF, or GTK where
  behavior differs.
- Check Essentials coverage for the target backend. Some desktop APIs are
  partial or intentionally stubbed.
- Test pointer, keyboard, window sizing, menu, file picker, secure storage,
  clipboard, and WebView/Blazor Hybrid behavior if the app depends on them.
- Use DevFlow where available to inspect visual tree, screenshots, and controls.

## Do Not Confuse With Backend Implementation

If the task is to add a handler, implement an Essentials API, scaffold backend
source, or debug renderer internals in `platforms/*`, use a platform-backend
implementation skill if one is available in your environment. This skill is only
for app developers who consume the experimental platform packages.

## Validation Checklist

- The selected backend matches the app's desktop target and UI requirements.
- Required native dependencies/templates/packages are installed.
- Hosting calls match the backend: `UseMauiAppLinuxGtk4`, `UseMauiAppMacOS`, or
  `UseMauiAppWPF`.
- Experimental parity gaps are listed before shipping commitments.
- The app builds and launches on the selected platform.
