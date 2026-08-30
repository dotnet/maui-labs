# .NET MAUI Labs

Experimental packages and tooling for .NET MAUI. This repository hosts pre-release projects that are in active development and may ship independently.

> ⚠️ **These packages are experimental.** APIs may change between releases. These packages are not covered by the [.NET MAUI Support Policy](https://dotnet.microsoft.com/platform/support/policy/maui) and are provided as-is.

## Products

At a glance:

| Product | What it is |
|---------|------------|
| [Cli](#cli) | `maui` global tool for environment diagnostics, device management, Apple/Android setup, app automation, and rapid prototyping. |
| [Comet](#comet) | Experimental MVU UI framework for .NET MAUI with C# fluent UI, signals, and reactive state. |
| [Go](#go) | Single-file Comet app server and companion app for rapid prototyping. |
| [DevFlow](#devflow) | Runtime app automation, inspection, debugging, and MCP tooling for .NET MAUI apps. |
| [AI Extensions](#ai-extensions) | Source-generated `Microsoft.Extensions.AI` tool bindings for MAUI and .NET apps. |
| [macOS AppKit Backend](#macos-appkit-backend) | Native AppKit backend for running MAUI apps as macOS apps without Mac Catalyst. |
| [WPF Backend](#wpf-backend) | WPF-based Windows desktop backend for .NET MAUI apps. |
| [Essentials.AI](#essentialsai) | On-device AI APIs for chat completion, embeddings, and tool calling in MAUI apps. |
| [AppProjectReference](#appprojectreference) | MSBuild package for referencing MAUI app projects and consuming their platform artifacts. |

### Cli

A command-line tool for .NET MAUI development environment setup, device management, and app automation.

- **Environment diagnostics** (`maui doctor`) with auto-fix capabilities
- **Android SDK and JDK management** (`maui android`) — install, update, and configure
- **Emulator management** (`maui android emulator`) — create, start, stop, and delete Android emulators
- **Apple platform management** (`maui apple`) — Xcode, simulator, and runtime management (macOS)
- **Device listing** (`maui device list`) across all connected platforms
- **DevFlow app automation** (`maui devflow`) — visual tree inspection, element interaction, screenshots, WebView/CDP automation, network monitoring, profiling, storage access, real-time log/sensor streaming, and MCP server for AI agents
- **MAUI Go** (`maui go`) — create, serve, and upgrade single-file Comet Go projects for rapid prototyping
- **Version info** (`maui version`)
- **Global options** — `--json` for CI pipelines, `--verbose`, `--dry-run`, `--ci`

| Package | Description |
|---------|-------------|
| [![NuGet: Microsoft.Maui.Cli](https://img.shields.io/nuget/v/Microsoft.Maui.Cli.svg?label=Microsoft.Maui.Cli)](https://www.nuget.org/packages/Microsoft.Maui.Cli/) | CLI global tool (`maui`) |

```bash
# Microsoft.Maui.Cli is currently released as a pre-release, so make sure to use the --prerelease flag
dotnet tool install -g Microsoft.Maui.Cli --prerelease
maui doctor
```

### Comet

Experimental MVU UI framework for .NET MAUI — C# fluent UI, signals/reactive state, single-file apps via Comet Go.

| Package | Description |
|---------|-------------|
| `Comet` | Core MVU framework |
| `Comet.SourceGenerator` | Roslyn source generators for Comet |
| `Comet.Layout.Yoga` | Yoga layout integration |

### Go

Single-file Comet apps server + companion app for rapid prototyping (alpha; sister to Comet).

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.Go.Server` | Comet Go server for hosting single-file apps |

### DevFlow

A comprehensive MAUI testing, automation, and debugging toolkit. The DevFlow CLI is integrated into the `maui` CLI as `maui devflow` — see [Cli](#cli) above.

- **In-app HTTP agent** for visual tree inspection, element interaction, and screenshots
- **Blazor CDP bridge** for Chrome DevTools Protocol on Blazor WebViews
- **MCP server** for AI agent integration (via `maui devflow mcp`)
- **Platform drivers** for iOS, Android, Mac Catalyst, Windows, and Linux/GTK
- **Network monitoring** and **performance profiling**
- **Real-time streaming** — WebSocket channels for logs, network requests, sensor data, profiler samples, and UI events
- **Storage access** — read/write app preferences and secure storage
- **Device introspection** — battery, connectivity, geolocation, display info, and permissions
- **Evidence bundles** — on-demand, redacted `.mauitrace` snapshots for bug reports, with a preview before anything is shared (`maui devflow evidence`)

| Package | Description |
|---------|-------------|
| [![NuGet: Microsoft.Maui.DevFlow.Agent](https://img.shields.io/nuget/v/Microsoft.Maui.DevFlow.Agent.svg?label=Microsoft.Maui.DevFlow.Agent)](https://www.nuget.org/packages/Microsoft.Maui.DevFlow.Agent/) | In-app agent for MAUI automation |
| [![NuGet: Microsoft.Maui.DevFlow.Agent.Core](https://img.shields.io/nuget/v/Microsoft.Maui.DevFlow.Agent.Core.svg?label=Microsoft.Maui.DevFlow.Agent.Core)](https://www.nuget.org/packages/Microsoft.Maui.DevFlow.Agent.Core/) | Platform-agnostic agent core |
| [![NuGet: Microsoft.Maui.DevFlow.Agent.Gtk](https://img.shields.io/nuget/v/Microsoft.Maui.DevFlow.Agent.Gtk.svg?label=Microsoft.Maui.DevFlow.Agent.Gtk)](https://www.nuget.org/packages/Microsoft.Maui.DevFlow.Agent.Gtk/) | GTK/Linux agent |
| [![NuGet: Microsoft.Maui.DevFlow.Blazor](https://img.shields.io/nuget/v/Microsoft.Maui.DevFlow.Blazor.svg?label=Microsoft.Maui.DevFlow.Blazor)](https://www.nuget.org/packages/Microsoft.Maui.DevFlow.Blazor/) | Blazor WebView CDP bridge |
| [![NuGet: Microsoft.Maui.DevFlow.Blazor.Gtk](https://img.shields.io/nuget/v/Microsoft.Maui.DevFlow.Blazor.Gtk.svg?label=Microsoft.Maui.DevFlow.Blazor.Gtk)](https://www.nuget.org/packages/Microsoft.Maui.DevFlow.Blazor.Gtk/) | WebKitGTK CDP bridge |
| [![NuGet: Microsoft.Maui.DevFlow.Driver](https://img.shields.io/nuget/v/Microsoft.Maui.DevFlow.Driver.svg?label=Microsoft.Maui.DevFlow.Driver)](https://www.nuget.org/packages/Microsoft.Maui.DevFlow.Driver/) | Platform driver library |
| [![NuGet: Microsoft.Maui.DevFlow.Testing](https://img.shields.io/nuget/v/Microsoft.Maui.DevFlow.Testing.svg?label=Microsoft.Maui.DevFlow.Testing)](https://www.nuget.org/packages/Microsoft.Maui.DevFlow.Testing/) | Experimental, framework-neutral flow contracts and runner. Android is an engineering pilot; the official MAUI Windows second-platform contract is implemented but not yet qualified by accepted runtime evidence. WPF is a separate experimental backend. |
| [![NuGet: Microsoft.Maui.DevFlow.Logging](https://img.shields.io/nuget/v/Microsoft.Maui.DevFlow.Logging.svg?label=Microsoft.Maui.DevFlow.Logging)](https://www.nuget.org/packages/Microsoft.Maui.DevFlow.Logging/) | Buffered JSONL file logger |

### AI Extensions

AI integration packages for `Microsoft.Extensions.AI` and .NET MAUI apps.

#### AI Attributes

Source-generated AI tool discovery — annotate methods or property accessors with `[ExportAIFunction]` to create AI-callable tools. Composed or auto-generated tool contexts, DI-aware parameter binding, approval gates, AOT-friendly.

| Package | Description |
|---------|-------------|
| [![NuGet: Microsoft.Maui.AI.Attributes](https://img.shields.io/nuget/v/Microsoft.Maui.AI.Attributes.svg?label=Microsoft.Maui.AI.Attributes)](https://www.nuget.org/packages/Microsoft.Maui.AI.Attributes/) | Source-generated AI tool contexts for `Microsoft.Extensions.AI` |

### macOS AppKit Backend

A native macOS AppKit backend for .NET MAUI — run MAUI apps as true AppKit apps with NSWindow, NSButton, NSScrollView, native menu bar, sidebar flyout, and more. An alternative to Mac Catalyst.

- **Native AppKit controls** — NSTextField, NSButton, NSSwitch, NSSlider, NSImageView, and more
- **Navigation** — Shell, NavigationPage, TabbedPage, FlyoutPage with sidebar
- **Blazor WebView** — via WKWebView
- **MapKit** — native MapView integration
- **Essentials** — AppInfo, Battery, Clipboard, Geolocation, Preferences, SecureStorage, Sensors
- **DevFlow QA** — optional experimental AppKit flow artifacts are labeled `backend=appkit`; they never count as Mac Catalyst qualification

| Package | Description |
|---------|-------------|
| [![NuGet: Microsoft.Maui.Platforms.MacOS](https://img.shields.io/nuget/v/Microsoft.Maui.Platforms.MacOS.svg?label=Microsoft.Maui.Platforms.MacOS)](https://www.nuget.org/packages/Microsoft.Maui.Platforms.MacOS/) | Core AppKit backend — handlers, hosting, MapKit |
| [![NuGet: Microsoft.Maui.Platforms.MacOS.Essentials](https://img.shields.io/nuget/v/Microsoft.Maui.Platforms.MacOS.Essentials.svg?label=Microsoft.Maui.Platforms.MacOS.Essentials)](https://www.nuget.org/packages/Microsoft.Maui.Platforms.MacOS.Essentials/) | Essentials APIs for macOS |
| [![NuGet: Microsoft.Maui.Platforms.MacOS.BlazorWebView](https://img.shields.io/nuget/v/Microsoft.Maui.Platforms.MacOS.BlazorWebView.svg?label=Microsoft.Maui.Platforms.MacOS.BlazorWebView)](https://www.nuget.org/packages/Microsoft.Maui.Platforms.MacOS.BlazorWebView/) | Blazor Hybrid via WKWebView |

### WPF Backend

A WPF-based alternative to the official WinUI backend for .NET MAUI. Run MAUI apps on Windows desktops using native WPF controls with 22+ fully implemented controls, Shell navigation, Blazor WebView, and 14 Essentials APIs.

- **22+ controls** — Label, Button, Entry, Editor, Image, CheckBox, Switch, Slider, Picker, DatePicker, and more
- **Navigation** — Shell (flyout + tabs + URI routing), NavigationPage, TabbedPage, FlyoutPage, modal pages
- **Blazor WebView** — via WebView2 and AspNetCore.Components.WebView.Wpf
- **Essentials** — AppInfo, DeviceInfo, Connectivity, Preferences, SecureStorage, Clipboard, Screenshot, and more

| Package | Description |
|---------|-------------|
| [![NuGet: Microsoft.Maui.Platforms.Windows.WPF](https://img.shields.io/nuget/v/Microsoft.Maui.Platforms.Windows.WPF.svg?label=Microsoft.Maui.Platforms.Windows.WPF)](https://www.nuget.org/packages/Microsoft.Maui.Platforms.Windows.WPF/) | Core WPF backend — handlers, hosting, Blazor WebView |
| [![NuGet: Microsoft.Maui.Platforms.Windows.WPF.Essentials](https://img.shields.io/nuget/v/Microsoft.Maui.Platforms.Windows.WPF.Essentials.svg?label=Microsoft.Maui.Platforms.Windows.WPF.Essentials)](https://www.nuget.org/packages/Microsoft.Maui.Platforms.Windows.WPF.Essentials/) | Essentials APIs for WPF |

### Essentials.AI

On-device AI capabilities for .NET MAUI via `Microsoft.Extensions.AI` abstractions. On Apple platforms, wraps Apple Intelligence (Foundation Models) for chat completion with streaming and tool calling, and Apple NaturalLanguage APIs for on-device embeddings.

- **`IChatClient`** backed by Apple Intelligence on iOS, macOS, and Mac Catalyst
- **Streaming infrastructure** — progressive JSON deserialization of LLM responses
- **NL embeddings** — on-device semantic search via Apple's NaturalLanguage framework (`NLEmbeddingGenerator`)
- **Tool calling** — function-calling support for on-device models

| Package | Description |
|---------|-------------|
| [![NuGet: Microsoft.Maui.Essentials.AI](https://img.shields.io/nuget/v/Microsoft.Maui.Essentials.AI.svg?label=Microsoft.Maui.Essentials.AI)](https://www.nuget.org/packages/Microsoft.Maui.Essentials.AI/) | On-device AI APIs for MAUI |

### AppProjectReference

An MSBuild package that lets test projects, packaging projects, or CI tools declare a MAUI app as a build-time dependency and consume its platform artifacts (`.apk`, `.ipa`, `.app`, `.msix`) as MSBuild items with rich metadata.

```xml
<MauiAppProjectReference Include="..\MyApp\MyApp.csproj" />
```

Built artifacts are exposed as `@(MauiAppArtifact)` items with `ArtifactType`, `ApplicationId`, `Installable`, `Launchable`, and other metadata — no manual path hunting required.

| Package | Description |
|---------|-------------|
| [![NuGet: Microsoft.Maui.Build.AppProjectReference](https://img.shields.io/nuget/v/Microsoft.Maui.Build.AppProjectReference.svg?label=Microsoft.Maui.Build.AppProjectReference)](https://www.nuget.org/packages/Microsoft.Maui.Build.AppProjectReference/) | Build-time app project reference with artifact discovery |

## Agent Skills

This repository is also a marketplace for distributable agent skills for .NET MAUI development. Skills are organized as plugins compatible with Copilot CLI, Claude Code, and VS Code.

| Plugin | Description |
|--------|-------------|
| [`dotnet-maui`](plugins/dotnet-maui/) | MAUI development: DevFlow automation, profiling, accessibility, platform bindings, diagnostics, session review |

```bash
# Install via Copilot CLI
/plugin marketplace add dotnet/maui-labs
/plugin install dotnet-maui@dotnet-maui-labs
```

See [plugins/](plugins/) for the full catalog and [plugins/CONTRIBUTING.md](plugins/CONTRIBUTING.md) for how to add skills.

## Nightly Builds

Preview packages from `main` are published automatically to the dotnet10 feed:

```
https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet10/nuget/v3/index.json
```

Add this feed to your `NuGet.config`:

```xml
<packageSources>
  <add key="dotnet10" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet10/nuget/v3/index.json" />
</packageSources>
```

These are CI builds from `main` only — PR builds are not published. Use wildcard versions (e.g., `0.1.0-preview.*`) to get the latest.

## Getting Started

See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions and development setup.

For the formal DevFlow HTTP and WebSocket contract, see [`docs/DevFlow/spec`](docs/DevFlow/spec/README.md).

For AI Extensions usage and samples, see [`src/AIExtensions/README.md`](src/AIExtensions/README.md) and [`samples/AIExtensions.Sample.Garden`](samples/AIExtensions.Sample.Garden/README.md).

## Support

See [SUPPORT.md](.github/SUPPORT.md) for how to file issues, get help, and the support policy for this repository.
