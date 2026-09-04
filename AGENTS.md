# Agent Instructions

Instructions for GitHub Copilot and other AI coding agents working with the maui-labs repository.

## Repository Overview

This repository hosts experimental .NET MAUI packages. It is a **multi-product mono-repo** — each product lives under `src/{Product}/` with its own version, solution filter, and CI workflow.

### Products

| Product | Package / Tool | Description |
|---------|---------------|-------------|
| **Cli** | `Microsoft.Maui.Cli` (global tool: `maui`), `Microsoft.Maui.ProfilingHelper` | Unified MAUI command-line tool: environment diagnostics (`maui doctor`), Android SDK/JDK/emulator management, Apple platform management, device listing, `maui go` for rapid prototyping, `maui profile startup` for performance tracing, `maui project version` for project version management, `maui port check` for TCP port diagnostics, and the `maui devflow` automation surface. `Microsoft.Maui.ProfilingHelper` is a lightweight helper library injected by the CLI to drive the startup profiling exit-control handshake; it can also be referenced directly to mark startup completion via `MauiProfilingMarker.Complete()`. |
| **DevFlow** | `Microsoft.Maui.DevFlow.*` packages plus the unified `maui devflow` CLI surface | Runtime MAUI automation toolkit. In-app agent with HTTP API, visual tree inspection, CDP bridge for Blazor WebViews, MCP server for AI agents, cross-platform driver library. |
| **Comet** | `Comet`, `Comet.SourceGenerator`, `Comet.Layout.Yoga` | Experimental MVU UI framework for .NET MAUI — C# fluent UI, signals/reactive state, Yoga layout. |
| **Go** | `Microsoft.Maui.Go.Server` + Comet Go companion app | Single-file Comet apps server and companion app for rapid prototyping (alpha; sister to Comet). |
| **Essentials.AI** | `Microsoft.Maui.Essentials.AI` | On-device AI for .NET MAUI — semantic search, chat completion, embeddings, and tool use against local models. |
| **AIExtensions** | `Microsoft.Maui.AI.Attributes` | Source-generated AI tool bindings — turns decorated C# methods into `Microsoft.Extensions.AI`-callable tools using Roslyn, with DI parameter binding and AOT support. |
| **AppProjectReference** | `Microsoft.Maui.Build.AppProjectReference` | MSBuild SDK extension that enables referencing MAUI app projects from test and tooling projects. |
| **Linux GTK4** | `Microsoft.Maui.Platforms.Linux.Gtk4` + associated packages | .NET MAUI platform backend for Linux using GTK4 — handler, Essentials, BlazorWebView, and project templates. |
| **macOS AppKit** | `Microsoft.Maui.Platforms.MacOS` + associated packages | .NET MAUI platform backend for macOS AppKit — handler, Essentials, BlazorWebView, and project templates. |
| **WPF** | `Microsoft.Maui.Platforms.Windows.WPF` + associated packages | .NET MAUI platform backend for WPF — handler, Essentials, and project templates. |

### Technology Stack

- **.NET 10** (SDK version pinned in `global.json`, `rollForward: latestMinor`)
- **C#** with `LangVersion: latest`, file-scoped namespaces
- **Microsoft.DotNet.Arcade.Sdk** for build infrastructure
- **Central Package Management** — all versions in `Directory.Packages.props`
- **xUnit** v2.9.3 for testing, **coverlet** for coverage
- **System.CommandLine** 2.0.5 (stable) for CLI tooling

## Building

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (see `global.json` for exact version)
- MAUI workload: `dotnet workload install maui`

### Build Commands

```bash
# Build everything
dotnet build MauiLabs.slnx

# Build a single product (recommended for focused development)
dotnet build src/DevFlow/DevFlow.slnf

# Build via Arcade CI scripts (matches what CI runs for DevFlow)
# macOS/Linux:
./eng/common/cibuild.sh --configuration Release --prepareMachine --projects src/DevFlow/DevFlow.slnf
# Windows:
eng\common\cibuild.cmd -configuration Release -prepareMachine -projects src/DevFlow/DevFlow.slnf
```

### Build Troubleshooting

- If restore fails, check `NuGet.config` — feeds are internal dnceng proxies, not nuget.org
- If workload errors occur: `dotnet workload install maui macos maui-tizen`
- SDK version mismatch: check `global.json` vs `dotnet --version`

## Testing

```bash
# All tests
dotnet test MauiLabs.slnx

# Per-product
dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Tests/
```

- Tests run in CI on **macOS and Windows** (matrix build)
- Test results: `artifacts/TestResults/**/*.xml`
- No quarantine or outerloop test attributes are used in this repo

## Comet node-backend (Compose/SwiftUI) development

Comet is being refactored off MAUI handlers onto a retained **node backend**
(Jetpack Compose on Android, SwiftUI on iOS). This path uses **.NET 11 preview**,
pinned by `src/Comet/global.json` — NOT the repo-root .NET 10.

- **Run Comet builds from inside `src/Comet`.** Bare `dotnet` at the repo root
  resolves the .NET 10 SDK (root `global.json`) and fails net11 targets with
  NETSDK1045.
- **Host tests link Comet via a HintPath**, so rebuild it first:
  `dotnet build src/Comet/src/Comet/Comet.csproj -f net11.0-maccatalyst`
  before `dotnet test tests/Comet.Tests` — otherwise tests run a stale DLL.
- **Facade edits no longer need clean rebuilds** (fixed 2026-07-03). The old
  `ComposableLambda2: no Java peer` crash after editing `src/Comet/src/vendor/`
  was a .NET Android SDK incremental bug: `_BuildApkFastDev` didn't list
  `libxamarin-app.so` (MVID-keyed debug typemaps) in its Inputs, so the APK kept
  stale typemaps while fast deploy pushed the new assembly. Worked around in
  `src/Comet/Directory.Build.targets` (`_CometFixFastDevApkInputs`); upstream
  issue draft: `src/Comet/docs/research/upstream-issue-fastdev-typemap-staleness.md`.
  If the crash ever reappears (SDK update changing target names), fall back to
  `src/Comet/tools/clean-android.sh`. iOS: after a `Comet.SwiftUI.Shim` change,
  re-run `build-xcframework.sh` AND rm the probe `obj`/`bin` (incremental build
  won't relink the NativeReference xcframework).
- **Probe apps**: `sample/CometComposeProbe` (Android),
  `sample/CometSwiftUIProbe` (iOS). Android deploy:
  `dotnet build -t:Run -p:AndroidPackageFormat=apk`.
- **Standalone per-sample apps**: add `-p:CometSample=reply` (or `jetchat`) to
  either probe build — unique app id `com.comet.sample.<name>`, launcher name
  "Comet Reply"/"Comet Jetchat", isolated `obj|bin/sample-<name>/`, and the
  screen defaults from the app id — so samples install SIDE BY SIDE instead of
  replacing each other. The plain probe ids (`com.comet.composeprobe`,
  `com.comet.swiftuiprobe`, Jetchat default + `--es screen` / `COMET_SCREEN`
  switch) are what the smoke scripts drive — leave them installed.
- **Two devices are usually attached** — always target explicitly with
  `adb -s 13041FDD4007MT` (the physical Pixel 5). Don't use left-edge swipes to
  open drawers (triggers the system back/home gesture).
- **A black `adb screencap` usually means the display is off or the lock screen
  is up, NOT a render bug** — check `dumpsys display | grep mScreenState` and
  `keyguardShowing` before touching code. (Same lesson on the iOS simulator: its
  GPU state degrades after a couple of launches — verify on a physical device.)
- **Screenshots come scaled** ("multiply by N"); compute tap targets in the FULL
  resolution (1080-wide), not the displayed size.
- **Gold standard is local**: `~/work/compose-samples` (android/compose-samples —
  Jetchat/JetNews/Reply). Read the Kotlin directly; don't WebFetch GitHub.

### Matching the gold standard (fidelity rules) — TOP PRIORITY
The point of the Comet sample work is proving Comet drives the **EXACT native
control** the sample uses — the real Jetpack Compose widget on Android AND the
real native SwiftUI control on iOS. The sample is the proof, not the goal.
**A styled look-alike is a DEFECT even when it looks and behaves identically.**
- **Before building/claiming any control, open the gold `.kt` and read which
  composable it actually uses** (`grep -n "<Control>" <component>.kt`), then drive
  that exact widget — a `Button` is a `Button` (not a styled `Text`/`HStack`), a
  FAB is a `FloatingActionButton`, a confirm is a `TextButton`. Don't assume which
  control; VERIFY in source (the gold may hand-roll a row rather than use a Material
  item — only the source tells you).
- If the facade lacks the control or a needed param (e.g. FAB `containerColor`),
  **EXTEND the facade** — that's the "fill out Comet" work, not a license to ship a
  look-alike. Reproduce real modifiers too (`baselineHeight`, `clip(CircleShape)`).
- **Same standard on iOS/SwiftUI** — drive the native SwiftUI control, never leave a
  stub/placeholder where a native control exists.
- Don't report "faithful / pixel-exact / done" without a side-by-side against the
  gold image; state what you verified and on which device.
- Never assert an environment fact (device locked, network up, the cause of a
  black/blank shot) you haven't checked — verify first, then claim.

### DevFlow integration in CometComposeProbe

CometComposeProbe has a built-in DevFlow agent (`sample/CometComposeProbe/DevFlowHelper.cs`)
that enables `maui devflow screenshot`, `maui devflow list`, and tap/inspect from the CLI.
**Use DevFlow by default when doing Android Comet work** — it gives direct screenshot
feedback without needing `adb screencap` + Read each time.

#### Architecture (no UseMaui needed)

- `DevFlowAgentService.StartServerOnly(IAgentDispatcher)` — designed for Comet apps where
  `Application.Current` is unavailable; starts HTTP server + broker registration, no MAUI runtime.
- `ComposeProbeAgentService` overrides `HandleScreenshot` and uses Android
  `PixelCopy.Request` to capture GPU-rendered Compose content faithfully.
- `PixelCopyListener` **must live outside `#if DEBUG`** — Xamarin.Android's JNI typemap
  generator only registers `Java.Lang.Object` subclasses it sees in every build; a type
  hidden behind `#if DEBUG` is skipped in the Release codegen pass → "no Java peer type found"
  at runtime even on Debug builds after a clean deploy.
- All `PixelCopy.Request` calls must be dispatched via `activity.RunOnUiThread(...)` — the
  HTTP handler thread pool is not JNI-registered so `new PixelCopyListener(...)` would throw.

#### ADB setup (required after every app restart — port changes each launch)

The broker runs on the Mac; the agent runs on the device. Two tunnels are needed:

```bash
# 1. Device → Mac broker (reverse — device connects outward to Mac port 19223)
adb -s 13041FDD4007MT reverse tcp:19223 tcp:19223

# 2. Mac CLI → device agent (forward — Mac connects inward to device's assigned port)
# Get the port first:
adb -s 13041FDD4007MT logcat -d | grep "DevFlow agent started"
# Then forward that port (e.g. 10227):
adb -s 13041FDD4007MT forward tcp:10227 tcp:10227
```

Or read the port from the broker automatically:
```bash
PORT=$(maui devflow list | python3 -c "import sys,json; d=json.load(sys.stdin); print(d[0]['port']) if d else None")
adb -s 13041FDD4007MT forward tcp:$PORT tcp:$PORT
```

#### Typical workflow

```bash
# After deploy, set up tunnels (see above), then:
maui devflow list                              # confirm agent registered
maui devflow ui screenshot --output /tmp/s.png  # grab screenshot
```

#### Incremental deploy vs clean deploy

After editing only `DevFlowHelper.cs` (not the facade), incremental deploy is fine.
After adding or moving a `Java.Lang.Object` subclass (including `PixelCopyListener`),
**do a clean rebuild** — incremental deploy replaces the managed DLL but not the APK's
embedded Java proxy classes:

```bash
dotnet build sample/CometComposeProbe/CometComposeProbe.csproj -f net11.0-android -c Debug -t:Clean
dotnet build sample/CometComposeProbe/CometComposeProbe.csproj -f net11.0-android -c Debug -t:Run "-p:AdbTarget=-s 13041FDD4007MT"
```

## Code Conventions

- **ImplicitUsings**: enabled repo-wide
- **Nullable**: enabled repo-wide (`#nullable enable` is implicit)
- **File-scoped namespaces**: all files use `namespace X.Y.Z;` (not block-scoped)
- **No strong naming**: `SignAssembly: false`
- **Namespace pattern**: `Microsoft.Maui.DevFlow.{Component}.{SubComponent}`
- **No .editorconfig**: relies on Arcade SDK defaults
- **TreatWarningsAsErrors**: false (not enforced)

## Project Layout

```
maui-labs/
├── src/
│   ├── Cli/                              # Maui CLI product
│   │   ├── Microsoft.Maui.Cli/           # Unified `maui` CLI (includes DevFlow commands)
│   │   │   └── DevFlow/                  # DevFlow command implementation behind `maui devflow`
│   │   │       ├── Broker/               # Connection management
│   │   │       └── Mcp/Tools/            # MCP tool implementations
│   │   ├── Microsoft.Maui.Cli.UnitTests/ # CLI unit tests
│   │   └── Cli.slnf                      # Solution filter
│   ├── DevFlow/                          # DevFlow agent product
│   │   ├── Microsoft.Maui.DevFlow.Agent.Abstractions/  # Platform-agnostic base (HTTP server, routing, DevFlowAgentService)
│   │   ├── Microsoft.Maui.DevFlow.Agent.Core/          # MAUI UI backend (MauiDevFlowAgentService, VisualTreeWalker)
│   │   ├── Microsoft.Maui.DevFlow.Agent/               # Platform-specific overrides (iOS/Android/macOS/Windows)
│   │   ├── Microsoft.Maui.DevFlow.Agent.Gtk/           # GTK/Linux agent
│   │   ├── Microsoft.Maui.DevFlow.Agent.WPF/           # WPF agent
│   │   ├── Microsoft.Maui.DevFlow.Agent.Native/        # Plain .NET agent (no MAUI — Android/iOS/macOS)
│   │   ├── Microsoft.Maui.DevFlow.Agent.Native.Essentials/  # Optional add-on with Essentials support
│   │   ├── Microsoft.Maui.DevFlow.Analyzers/           # Roslyn analyzers
│   │   ├── Microsoft.Maui.DevFlow.Blazor/              # Blazor WebView CDP bridge
│   │   ├── Microsoft.Maui.DevFlow.Blazor.Gtk/          # WebKitGTK CDP bridge
│   │   ├── Microsoft.Maui.DevFlow.Client/              # Portable protocol client (AgentClient, DTOs) — netstandard2.0
│   │   ├── Microsoft.Maui.DevFlow.Client.Tests/        # Client tests (net472 + modern .NET)
│   │   ├── Microsoft.Maui.DevFlow.Driver/              # Platform drivers (process management, UI Automation)
│   │   ├── Microsoft.Maui.DevFlow.Logging/             # JSONL file logger
│   │   ├── Microsoft.Maui.DevFlow.Tests/               # xUnit tests
│   │   ├── Microsoft.Maui.DevFlow.Agent.IntegrationTests/  # Integration tests
│   │   ├── Microsoft.Maui.DevFlow.Inspector.Tests/     # Inspector tests
│   │   ├── Shared.Essentials/                          # Shared Essentials code (compiled into Agent.Core and Agent.Native.Essentials)
│   │   └── DevFlow.slnf                               # Solution filter
│   ├── AI/                               # Essentials.AI product
│   │   └── Microsoft.Maui.Essentials.AI/ # On-device AI package
│   ├── AIExtensions/                     # AI Extensions product
│   │   ├── Microsoft.Maui.AI.Attributes/           # Runtime library (attributes + AIToolContext base class)
│   │   └── Microsoft.Maui.AI.Attributes.Generators/ # Roslyn incremental source generator
│   ├── AppProjectReference/              # AppProjectReference product
│   │   └── Microsoft.Maui.Build.AppProjectReference/ # MSBuild SDK extension
│   ├── Comet/                            # Comet MVU framework
│   │   ├── src/Comet/                    # Core MVU framework
│   │   ├── src/Comet.SourceGenerator/    # Roslyn source generators
│   │   ├── src/Comet.Layout.Yoga/        # Yoga layout integration
│   │   ├── tests/Comet.Tests/            # xUnit tests
│   │   └── sample/                       # Sample Comet apps
│   └── Go/                               # Comet Go (single-file apps)
│       ├── Server/Microsoft.Maui.Go.Server/  # Comet Go server
│       ├── CompanionApp/                 # Comet Go companion MAUI app
│       └── Shared/                       # Shared Comet Go code
├── platforms/                            # Platform backend products
│   ├── Linux.Gtk4/                       # Linux GTK4 platform backend
│   ├── MacOS/                            # macOS AppKit platform backend
│   └── Windows.WPF/                      # WPF platform backend
├── samples/                              # Sample MAUI apps (not shipped)
├── playground/                           # Manual test/scratch apps
├── eng/                                  # Shared build infrastructure
│   ├── pipelines/                        # Azure DevOps pipeline definitions
│   ├── Versions.props                    # Central version definitions
│   ├── Signing.props                     # Code signing configuration
│   ├── Publishing.props                  # NuGet publishing config
│   └── common/                           # Arcade SDK (DO NOT MODIFY)
├── Directory.Build.props                 # Global MSBuild properties
├── Directory.Build.targets               # Global MSBuild targets
├── Directory.Packages.props              # Central Package Management
├── global.json                           # SDK version pinning
├── NuGet.config                          # NuGet feed configuration
└── MauiLabs.slnx                         # Full solution
```

### Key Configuration Files

| File | Purpose |
|------|---------|
| `global.json` | .NET SDK version and Arcade SDK version |
| `Directory.Build.props` | Global properties: TFMs, nullable, implicit usings, platform versions |
| `Directory.Packages.props` | All NuGet package versions (Central Package Management) |
| `eng/Versions.props` | Product version (`0.1.0-preview`), dependency versions |
| `eng/Signing.props` | Code signing: Microsoft cert for first-party, 3PartySHA2 for third-party |
| `eng/Publishing.props` | Arcade publishing version |
| `src/{Product}/Version.props` | Per-product version override |

## Packaging and Signing

- Packages are built by the Arcade SDK's `Pack` target
- **PackAsTool**: The user-facing global tool is `maui`; DevFlow functionality is exposed via `maui devflow`
- **IsShipping/IsPackable**: Default `false` in `Directory.Build.props`; shipped projects override to `true`
- **Signing**: `eng/Signing.props` configures Microsoft .NET certificate for first-party DLLs, `3PartySHA2` for third-party dependencies, `NuGet` certificate for `.nupkg` files
- **Version flow**: `eng/Versions.props` defines `VersionPrefix`/`VersionSuffix`, Arcade SDK applies them

## CI/CD

### GitHub Actions (PR validation)

Each product has its own workflow file: `.github/workflows/ci-{product}.yml`, calling the shared `_build.yml` reusable workflow.

- **Matrix**: macOS + Windows (configurable per product via `os` input)
- **Path-filtered**: only triggers for changed product paths + shared build infrastructure (`eng/**`, `Directory.Build.props`, etc.)
- **`pull_request.types`**: Must always include `[opened, synchronize, reopened, edited]` — the `edited` type ensures CI re-runs when GitHub auto-retargets a PR after a stacked branch merges
- Steps: restore → build → test → upload test results + packages

Existing workflows: `ci-ai.yml`, `ci-cli.yml`, `ci-comet.yml`, `ci-devflow.yml`, `ci-essentialsai.yml`, `ci-appprojectreference.yml`, `ci-linux-gtk4.yml`, `ci-macos-appkit.yml`, `ci-wpf.yml`

### Azure DevOps (official builds)

- **Single pipeline**: `eng/pipelines/devflow-official.yml` — all products build in parallel
- Builds, signs (MicroBuild/ESRP), and publishes to internal feeds via Maestro/DARC
- **MicroBuild signing** enabled (`enableMicrobuild: true`) — this enforces CFS network isolation
- NuGet.org publishing: conditional stages per product, gated by boolean parameters (e.g., `publishDevFlowNuget`), using `1ES.PublishNuget@1`
- Each product has: a parameter, a build job, and a publish stage

### NuGet Feed Configuration

NuGet.config uses **internal dnceng proxy feeds only** — no direct nuget.org reference:
- `dotnet-public`, `dotnet-tools`, `dotnet-eng`, `dotnet10`, `dotnet11`, `dotnet11-transport`

**Do not** add `nuget.org` as a direct feed source. Package versions flow via Dependency Flow (Maestro/DARC).

## Adding a New Product

Each product requires source setup **and** CI/CD configuration across two systems.

### Source Setup

1. Create `src/{NewProduct}/` with `Version.props`, project folders, test project, `{NewProduct}.slnf`
2. Add projects to `MauiLabs.slnx`
3. Add package versions to `Directory.Packages.props`
4. Add signing entries in `eng/Signing.props` for any new third-party DLLs

### Documentation

5. Create **two READMEs**:
   - A **contributor README** at the product root (e.g. `src/{NewProduct}/README.md`) for GitHub browsing — describes features, build instructions, architecture, and links to the NuGet README.
   - A **NuGet README** next to the shipping csproj (e.g. `src/{NewProduct}/Microsoft.Maui.{NewProduct}/README.md`) — consumer-facing with install, quick start, and usage examples. Pack it via `<None Include="README.md" Pack="true" PackagePath="/" />` in the csproj and set `<PackRepoRootReadme>false</PackRepoRootReadme>` to avoid duplicating the repo-root README. **Images must use absolute URLs** (`https://raw.githubusercontent.com/dotnet/maui-labs/main/...`) — relative paths break on NuGet.org.
   
   Both should include: product name, feature list, platform support matrix, quick start code, package table, requirements, and experimental status warning. Keep feature descriptions aligned to avoid drift.
6. Add a section for the product in the **repo-root `README.md`** under `## Products` with a brief description, feature highlights, and package table.

### CI/CD Setup

7. **GitHub Actions**: Create `.github/workflows/ci-{newproduct}.yml` calling the reusable `_build.yml` workflow. Must include `pull_request.types: [opened, synchronize, reopened, edited]` and path filters scoped to the product source plus shared build files.
8. **Azure DevOps**: Edit `eng/pipelines/devflow-official.yml` — add a publish parameter, a build job in the `build` stage, and a conditional publish stage for NuGet.org. Run workload installation through Arcade's SDK wrapper (`eng\common\dotnet.cmd workload install ...`) so it uses the same SDK that `cibuild.cmd` selects without assuming a repo-local `.dotnet` directory exists. For commands with quoted arguments or paths containing spaces, invoke `eng/common/dotnet.ps1` from a `pwsh` step instead of the CMD shim. If using `UseDotNet@2` instead, set an explicit `version:` matching `global.json` (not `useGlobalJson: true`). Pin workloads with `--version` matching `_build.yml`. Pure managed Apple products can build on Windows (workload provides reference assemblies). Products with native code (e.g. Swift) need a two-stage build: macOS compiles native + Windows packs/signs. See `EssentialsAI_macOS`/`EssentialsAI` for the native pattern, `MacOS` for the managed pattern.

> **Complete copy-paste templates** for both the GitHub Actions workflow and all three Azure DevOps blocks (parameter, build job, publish stage) are in `.github/copilot-instructions.md` under **"CI/CD — New Product Checklist"**.

## DevFlow MCP Tools

DevFlow exposes 68 MCP tools for AI agent integration (in `src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/Tools/`):

| Tool | Purpose |
|------|---------|
| `maui_app_info` | App name, version, package, theme |
| `maui_assert` | Assert element property equals expected value |
| `maui_back` | Go back in the app navigation stack |
| `maui_batch` | Execute multiple UI actions in a single request |
| `maui_battery_info` | Battery level, state, power source |
| `maui_capabilities` | Get capabilities supported by the connected agent |
| `maui_cdp_evaluate` | Execute JavaScript in Blazor WebView via CDP |
| `maui_cdp_screenshot` | WebView screenshot via CDP |
| `maui_cdp_source` | Get WebView page source |
| `maui_cdp_webviews` | List available WebViews |
| `maui_clear` | Clear text from an element |
| `maui_connectivity` | Network access and connection profiles |
| `maui_device_info` | Device manufacturer, model, OS |
| `maui_display_info` | Screen density, size, orientation |
| `maui_element` | Get full element details |
| `maui_extension_call` | Call an extension tool on the connected DevFlow agent |
| `maui_extension_list` | List all extensions registered on the connected DevFlow agent |
| `maui_files_delete` | Delete a file from an advertised app storage root |
| `maui_files_download` | Download a file from an advertised app storage root |
| `maui_files_list` | List files and directories under an advertised app storage root |
| `maui_files_upload` | Upload a file to an advertised app storage root |
| `maui_fill` | Fill text into Entry/Editor |
| `maui_focus` | Set focus to an element |
| `maui_geolocation` | GPS coordinates |
| `maui_gesture` | Pinch/zoom, rotate, pan, swipe, double-tap, long-press |
| `maui_get_property` | Read any element property |
| `maui_get_theme` | Get the current app-scoped light/dark theme |
| `maui_hittest` | Find elements at screen coordinates |
| `maui_invoke_action` | Invoke a registered DevFlow Action by name |
| `maui_jobs_list` | List background jobs registered on the device |
| `maui_jobs_run` | Trigger a supported background job by identifier |
| `maui_key` | Send a key press to an element |
| `maui_layout_diagnostics` | Inspect rendered UI for clipped, off-window, or overlapping elements and layout issues |
| `maui_list_actions` | List all registered DevFlow Actions |
| `maui_list_agents` | List connected MAUI DevFlow agents (running apps) |
| `maui_logs` | Retrieve app logs (ILogger + WebView console) |
| `maui_navigate` | Shell navigation to a route |
| `maui_network` | List captured HTTP requests |
| `maui_network_clear` | Clear captured request buffer |
| `maui_network_detail` | Full request/response details |
| `maui_preferences_clear` | Clear all preferences |
| `maui_preferences_delete` | Delete a preference |
| `maui_preferences_get` | Read a preference value |
| `maui_preferences_list` | List preference keys |
| `maui_preferences_set` | Write a preference value |
| `maui_query` | Query elements by type, AutomationId, or text |
| `maui_query_css` | Query elements by CSS selector |
| `maui_recording_start` | Start screen recording |
| `maui_recording_status` | Check recording status |
| `maui_recording_stop` | Stop screen recording |
| `maui_resize` | Resize the app window |
| `maui_screenshot` | Capture screenshot (page, element, or fullscreen) |
| `maui_scroll` | Scroll by delta, item index, or into view |
| `maui_secure_storage_clear` | Clear all secure storage |
| `maui_secure_storage_delete` | Delete secure storage entry |
| `maui_secure_storage_get` | Read secure storage value |
| `maui_secure_storage_set` | Write secure storage value |
| `maui_select_agent` | Select a specific agent for subsequent commands |
| `maui_sensors_list` | List available device sensors |
| `maui_sensors_start` | Start a sensor |
| `maui_sensors_stop` | Stop a sensor |
| `maui_set_property` | Live-edit element properties |
| `maui_set_theme` | Set the app or emulator/simulator to light, dark, or system theme |
| `maui_status` | Agent connection status, platform, app name |
| `maui_storage_roots` | List file storage roots advertised by the app |
| `maui_tap` | Tap a UI element |
| `maui_tree` | Inspect visual tree — structured JSON hierarchy with IDs, types, bounds |
| `maui_wait` | Wait for an agent to connect |

## Important Notes

- **`eng/common/` is auto-generated by Arcade SDK** — never modify files in this directory manually.
- **`AgentClient`** (in `Microsoft.Maui.DevFlow.Client`) is the public API consumed by NuGet users. Method signature changes are **binary and source breaking** for consumers. It targets `netstandard2.0` as well as modern .NET, so anything added there must compile for both; `Microsoft.Maui.DevFlow.Driver` re-exports the types via `TypeForwards.cs` and keeps the platform/native functionality.
- The repo is at version **0.1.0-preview** — breaking changes are acceptable but should be documented.
- **Platform conditionals**: Use `#if IOS`, `#if ANDROID`, `#if MACCATALYST`, `#if MACOS`, `#if WINDOWS` for platform-specific code in multi-targeting projects.

## Skills Marketplace

This repository also distributes agent skills as plugins under `plugins/`. Use `plugins/dotnet-maui/` for app-building skills and `plugins/dotnet-maui-tooling/` for specialist DevFlow, binding, and workload diagnostic skills.

### Plugin Structure

```
plugins/<plugin-name>/
 plugin.json              # Plugin manifest (name, version, description, skills path)
 skills/
 <skill-name>/    
 SKILL.md         # Skill definition (required)        
 references/      # Supporting documentation (optional)        
```

### Skill Format

Each `SKILL.md` must have YAML frontmatter:

```yaml
---
name: skill-name
description: >-
  What this skill does. USE FOR: specific scenarios.
  DO NOT USE FOR: non-applicable contexts.
---
```

The `description` field is critical — agent runtimes read only the description to decide whether to activate the skill. Include explicit "USE FOR" and "DO NOT USE FOR" guidance.

### Adding a New Skill

See [plugins/CONTRIBUTING.md](plugins/CONTRIBUTING.md) for the full guide, including skill structure, SKILL.md format, evaluation tests, and the PR checklist.
