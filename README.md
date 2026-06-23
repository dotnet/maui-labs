# .NET MAUI Labs

Experimental tooling, automation, platform backends, UI frameworks, and AI integrations for .NET MAUI.

> [!WARNING]
> These projects are experimental. APIs may change between releases. Packages in this repository are not covered by the [.NET MAUI Support Policy](https://dotnet.microsoft.com/platform/support/policy/maui) and are provided as-is.

## Start here

Most workflows assume the .NET 10 SDK is installed. The MAUI CLI can check your environment and install the MAUI workload if it is missing:

```bash
dotnet tool install -g Microsoft.Maui.Cli --prerelease
maui doctor --fix
```

If you prefer to install the workload directly, run `dotnet workload install maui`.

### MAUI CLI

Install the unified `maui` command-line tool for environment setup, device management, project versioning, profiling, rapid prototyping, and DevFlow app automation.

```bash
maui doctor
```

| Task | Command |
| --- | --- |
| Diagnose your MAUI environment | `maui doctor` |
| Diagnose and auto-fix MAUI setup issues | `maui doctor --fix` |
| List connected devices and emulators | `maui device list` |
| Install Android SDK/JDK/emulator tooling | `maui android install` |
| Manage Xcode, Apple runtimes, and simulators on macOS | `maui apple --help` |
| Pin or inspect a project MAUI package version | `maui project version` |
| Profile app startup | `maui profile startup --help` |
| Create a single-file Comet Go app | `maui go create` |
| Initialize a project for DevFlow automation and local agent skills | `maui devflow init` |

Docs: [`src/Cli/README.md`](src/Cli/README.md)

### DevFlow

DevFlow is live app automation and inspection for .NET MAUI apps: similar to Playwright or Selenium, but for native MAUI UI, Blazor Hybrid WebViews, app logs, network traffic, storage, screenshots, and AI-agent workflows.

Add the in-app DevFlow Agent package to your app. If restore cannot find the preview version, add the [nightly feed](#nightly-builds).

```xml
<PackageReference Include="Microsoft.Maui.DevFlow.Agent" Version="0.1.0-preview.*" />
```

```csharp
#if DEBUG
builder.AddMauiDevFlowAgent();
#endif
```

Run your app with the DevFlow Agent registered before using `maui devflow` commands.

```bash
maui devflow init
maui devflow ui tree
maui devflow ui screenshot -o screenshot.png
maui devflow mcp
```

| Task | Command |
| --- | --- |
| Inspect the MAUI visual tree | `maui devflow ui tree` |
| Tap, fill, scroll, resize, and assert UI state | `maui devflow ui --help` |
| Capture screenshots or recordings | `maui devflow ui screenshot` / `maui devflow recording start` |
| Read app logs and network requests | `maui devflow logs` / `maui devflow network` |
| Automate Blazor Hybrid WebViews | `maui devflow webview --help` |
| Start an MCP server for AI agents | `maui devflow mcp` |
| Initialize a project for DevFlow automation and local agent skills | `maui devflow init` |

Docs: [`src/DevFlow/README.md`](src/DevFlow/README.md), [`docs/DevFlow/spec/README.md`](docs/DevFlow/spec/README.md)

## Product catalog

Use this table to discover the rest of the repository. Each product has its own README with setup steps, package names, examples, and status.

| Product | What it is | Start here |
| --- | --- | --- |
| **MAUI CLI** | Unified `maui` global tool for diagnostics, devices, Android/Apple setup, project versioning, profiling, Go, and DevFlow | [`src/Cli/README.md`](src/Cli/README.md) |
| **DevFlow** | Runtime app automation and diagnostics toolkit with CLI, HTTP API, driver library, MCP server, visual tree, screenshots, logs, network, storage, and WebView/CDP support | [`src/DevFlow/README.md`](src/DevFlow/README.md) |
| **Comet** | Experimental MVU UI framework for .NET MAUI: an alternative to XAML using C# fluent UI and reactive state | [`src/Comet/README.md`](src/Comet/README.md) |
| **Go** | Single-file Comet app prototyping with a server and companion app | [`src/Go/README.md`](src/Go/README.md) |
| **Essentials.AI** | On-device AI APIs for MAUI via `Microsoft.Extensions.AI` abstractions | [`src/AI/README.md`](src/AI/README.md) |
| **AI Extensions** | Source-generated AI tool bindings from decorated C# methods and property accessors | [`src/AIExtensions/README.md`](src/AIExtensions/README.md) |
| **AppProjectReference** | MSBuild package for referencing MAUI app projects and consuming built app artifacts from tests or tooling | [`src/AppProjectReference/README.md`](src/AppProjectReference/README.md) |
| **Linux GTK4 backend** | Experimental .NET MAUI backend for Linux desktops using GTK4 | [`platforms/Linux.Gtk4/README.md`](platforms/Linux.Gtk4/README.md) |
| **macOS AppKit backend** | Experimental native AppKit backend for MAUI apps on macOS, separate from Mac Catalyst | [`platforms/MacOS/README.md`](platforms/MacOS/README.md) |
| **WPF backend** | Experimental WPF backend for MAUI apps on Windows desktops | [`platforms/Windows.WPF/README.md`](platforms/Windows.WPF/README.md) |

## Nightly builds

Preview packages from `main` are published automatically to the dotnet10 feed:

```text
https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet10/nuget/v3/index.json
```

Add this feed to your `NuGet.config`:

```xml
<packageSources>
  <add key="dotnet10" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet10/nuget/v3/index.json" />
</packageSources>
```

These are CI builds from `main` only. PR builds are not published. Use wildcard versions such as `0.1.0-preview.*` to get the latest package from this feed.

## Contributing

See [`CONTRIBUTING.md`](.github/CONTRIBUTING.md) for repository setup, build instructions, and contribution guidance.

## Support

See [`SUPPORT.md`](.github/SUPPORT.md) for how to file issues, get help, and understand the support policy for this repository.

## For AI agents

This repository is organized so agents can discover capabilities by product area:

| If you need to... | Use |
| --- | --- |
| Set up, inspect, or repair a MAUI development environment | `maui doctor`, `maui android`, `maui apple`, [`src/Cli`](src/Cli/README.md) |
| Automate, inspect, or debug a running MAUI app | DevFlow CLI: `maui devflow`, [`src/DevFlow`](src/DevFlow/README.md) |
| Add the in-app automation runtime to a MAUI app | DevFlow Agent: `Microsoft.Maui.DevFlow.Agent`, [`src/DevFlow`](src/DevFlow/README.md) |
| Expose MAUI app automation tools to an MCP-compatible agent | `maui devflow mcp`, [`src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp`](src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/) |
| Initialize a project for DevFlow automation and local agent skills | `maui devflow init`, [`plugins/dotnet-maui`](plugins/dotnet-maui/) |
| Build MVU-style MAUI UI in C# | [`src/Comet`](src/Comet/README.md) |
| Prototype a single-file MAUI app | `maui go`, [`src/Go`](src/Go/README.md) |
| Add on-device AI or source-generated AI tools | [`src/AI`](src/AI/README.md), [`src/AIExtensions`](src/AIExtensions/README.md) |
| Target experimental desktop backends | [`platforms/Linux.Gtk4`](platforms/Linux.Gtk4/README.md), [`platforms/MacOS`](platforms/MacOS/README.md), [`platforms/Windows.WPF`](platforms/Windows.WPF/README.md) |

`maui devflow mcp` runs continuously; start it as a long-running process or configure it in your agent framework's MCP settings.

## Agent skills

This repository also distributes .NET MAUI agent skills as plugins compatible with Copilot CLI, Claude Code, and VS Code. In Copilot CLI, `/plugin install` installs the skill plugin for the agent environment; `maui devflow init` initializes DevFlow automation and local agent skills for a specific project.

```bash
/plugin marketplace add dotnet/maui-labs
/plugin install dotnet-maui@dotnet-maui-labs
```

| Plugin | Description |
| --- | --- |
| [`dotnet-maui`](plugins/dotnet-maui/) | MAUI development skills for DevFlow automation, profiling, accessibility, platform bindings, diagnostics, and session review |

See [`plugins/`](plugins/) for the full catalog and [`plugins/CONTRIBUTING.md`](plugins/CONTRIBUTING.md) for how to add skills.
