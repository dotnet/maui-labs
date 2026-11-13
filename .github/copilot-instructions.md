# Copilot Instructions for maui-labs

These instructions guide GitHub Copilot and other AI code generation tools when working with the maui-labs repository.

## Platform-Specific Code

DevFlow targets multiple platforms via multi-targeting. The pattern:

- **`Microsoft.Maui.DevFlow.Agent.Abstractions`** targets `net10.0` — all platform-agnostic code lives here (HTTP server, routing, `DevFlowAgentService` base class). No MAUI dependency.
- **`Microsoft.Maui.DevFlow.Agent.Core`** targets `net10.0` — MAUI UI backend (`MauiDevFlowAgentService`, `VisualTreeWalker`). Depends on `Agent.Abstractions`.
- **`Microsoft.Maui.DevFlow.Agent`** targets `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-macos`, `net10.0-windows10.0.19041.0` — platform-specific overrides.
- **`Microsoft.Maui.DevFlow.Agent.Gtk`** targets `net10.0` — Linux/GTK-specific code.

Use `#if` directives for platform code in multi-targeting projects:

```csharp
#if IOS || MACCATALYST
    // iOS and Mac Catalyst share UIKit
    return uiWindow.Screen.Scale;
#elif ANDROID
    return activity.Resources?.DisplayMetrics?.Density ?? 1.0;
#elif WINDOWS
    return xamlRoot.RasterizationScale;
#elif MACOS
    return nsWindow.BackingScaleFactor;
#endif
```

**Important**: Both `.ios.cs` and `.maccatalyst.cs` files compile for Mac Catalyst. Use `#if IOS || MACCATALYST` when code applies to both.

## Agent Architecture (Abstractions/Core/Platform Pattern)

When adding new features to the DevFlow agent:

1. **Add the virtual method in `Agent.Abstractions/DevFlowAgentService.cs`** — this is the platform-agnostic base
2. **Override in `Agent.Core/MauiDevFlowAgentService.cs`** for MAUI-specific behavior
3. **Override in `Agent/DevFlowAgentService.cs`** with `#if` directives for platform-specific behavior
4. **Override in `Agent.Gtk/GtkAgentService.cs`** for Linux/GTK if needed

Example:
```csharp
// In Agent.Abstractions/DevFlowAgentService.cs
protected virtual Task<byte[]?> CaptureFullScreenAsync() => Task.FromResult<byte[]?>(null);

// In Agent/DevFlowAgentService.cs
#if IOS || MACCATALYST
protected override Task<byte[]?> CaptureFullScreenAsync()
    => DispatchAsync(() => CaptureAllWindowsComposited());
#elif MACOS
protected override async Task<byte[]?> CaptureFullScreenAsync() { /* AppKit capture */ }
#endif
```

## MCP Tool Conventions

MCP tools live in `src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/Tools/`. When creating a new tool:

1. Create a new file in the `Tools/` directory
2. Use `[McpServerToolType]` on the class, `[McpServerTool]` on the method
3. **Every parameter must have a `[Description]`** — this is what AI agents see
4. Tool names use the `maui_` prefix and snake_case: `maui_screenshot`, `maui_tap`
5. First parameter is always `McpAgentSession session`
6. Use `session.GetAgentClientAsync(agentPort)` for agent resolution
7. **Register the tool** in `Mcp/McpServerHost.cs`: `.WithTools<YourTool>()`

```csharp
[McpServerToolType]
public sealed class MyNewTool
{
    [McpServerTool(Name = "maui_my_action"),
     Description("Clear description of what this tool does and when to use it.")]
    public static async Task<string> MyAction(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
        [Description("Describe this parameter clearly")] string requiredParam,
        [Description("Describe this optional parameter")] string? optionalParam = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        // Use agent.* methods
        return "Result";
    }
}
```

## HTTP Endpoint Conventions

Agent HTTP endpoints are defined in `DevFlowAgentService.cs` (in Agent.Abstractions). When adding new endpoints:

1. Register the route in the `RegisterRoutes()` method: `_server.MapGet("/api/myendpoint", HandleMyEndpoint);`
2. Implement the handler as `protected virtual async Task<HttpResponse> HandleMyEndpoint(HttpRequest request)`
3. For POST endpoints that accept JSON bodies, define a DTO class at the bottom of the file
4. **Add a corresponding method in `AgentClient`** (in `Microsoft.Maui.DevFlow.Client`) — this is the public API

```csharp
// In DevFlowAgentService.cs — handler
protected virtual async Task<HttpResponse> HandleMyEndpoint(HttpRequest request) { ... }

// In AgentClient.cs — public API (this is what NuGet consumers use)
public async Task<MyResult?> MyEndpointAsync(string param) { ... }
```

## CLI Command Conventions

CLI commands use **System.CommandLine** in `Program.cs`:

- Use `Option<T>` for named parameters, `Argument<T>` for positional
- Support `--json` / `--no-json` output modes via `OutputWriter`
- Use `SetHandler` with `InvocationContext` for commands with many options
- Post-action flags: `--and-screenshot`, `--and-tree`, `--and-tree-depth` for verification after mutations

## Client and Driver Libraries (AgentClient)

`Microsoft.Maui.DevFlow.Client/AgentClient.cs` is the **public API for NuGet consumers**. It lives
in the portable `Microsoft.Maui.DevFlow.Client` package (`netstandard2.0` + modern .NET) so .NET
Framework harnesses share the exact protocol behavior; `Microsoft.Maui.DevFlow.Driver` references it
and keeps the platform/native concerns (app process management, UI Automation, Skia, recording).
Changes to method signatures are:

- **Binary breaking** — existing compiled code stops working
- **Source breaking** — existing source code fails to compile

Rules when working in this area:

- Protocol DTOs are defined **once**, in Client. Never re-declare one in Driver or a test harness.
- Everything in Client must compile for `netstandard2.0`. Where a modern API has no portable
  equivalent (e.g. `SocketsHttpHandler.ConnectCallback`), guard it with `#if DEVFLOW_NETSTANDARD`
  and supply a portable path — do not fork the DTOs or the client surface.
- Missing BCL overloads belong in `Client/Compatibility/NetStandardCompat.cs` as extension methods
  with the exact framework signature, so shared sources stay free of `#if`.
- Types keep the `Microsoft.Maui.DevFlow.Driver` namespace and are re-exported from the Driver
  assembly via `Driver/TypeForwards.cs`. Adding a new public type to Client means adding a forward.
- Anything platform-specific or native stays in Driver. Do not grow the portable surface with it.

The repo is at 0.1.0-preview so breaking changes are acceptable, but:
- Document breaking changes in the PR description
- Add the `breaking-change` label to the PR
- Update parameter defaults so existing callers keep working where possible (add new params with defaults at the end)

## Test Patterns

- **Framework**: xUnit v2.9.3
- **Naming**: `MethodName_Condition_ExpectedResult` or descriptive `[Fact]` names
- **Location**: `src/DevFlow/Microsoft.Maui.DevFlow.Tests/`
- **Coverage**: coverlet.collector
- **Approach**: DevFlow tests use real Agent.Core code — they instantiate actual services and test behavior

## Arcade SDK Gotchas

- **Never modify `eng/common/`** — auto-generated by Arcade SDK, overwritten by Dependency Flow
- **Central Package Management**: specify package versions **only** in `Directory.Packages.props`, not in `.csproj` files
- **`IsShipping` and `IsPackable`**: default `false` in `Directory.Build.props`. Shipped projects explicitly set `true`.
- **Signing**: configured in `eng/Signing.props`. New third-party DLLs need a `3PartySHA2` entry.
- **Version**: defined in `eng/Versions.props` (`VersionPrefix` + `VersionSuffix`). Per-product overrides in `src/{Product}/Version.props`.

## npm Dependencies and Supply Chain

The JavaScript in this repo ships (the DevFlow Inspector client and VS Code host are packaged and
distributed), so vulnerable transitive dependencies reach users. Catch them **locally, before
opening the PR** — not weeks later via a Dependabot advisory PR.

**Whenever you add, update, or remove an npm dependency, or regenerate a lockfile:**

```bash
node eng/scripts/audit-npm.mjs
```

This audits every npm project in the repo and verifies each is registered in
`.github/dependabot.yml`. Exit `0` = clean. Exit `1` = `high`/`critical` advisories, an
unregistered project, or a missing lockfile. Exit `2` = a project could not be checked against
the advisory database. Resolve all three before opening the PR.

- **Exit `2` is not a pass.** `npm audit` also exits non-zero when it cannot reach the registry,
  so that result means no signal. `--allow-unverified` downgrades it for offline work; CI never
  passes that flag and so fails closed.
- **Register every new `package.json` in `.github/dependabot.yml` in the same PR.** Dependabot
  security alerts fire repo-wide, but routine *version* updates only run for listed directories.
  An unregistered project drifts silently until advisories pile up.
- **Commit a `package-lock.json`.** Without one there is no resolved graph to audit.
- **`devDependencies` count too** — they run on CI runners with repo credentials in scope.
- Fix with `npm audit fix` in the reported directory, re-run `npm ci && npm test`, and commit the
  updated `package-lock.json`. Remediation is required; a known advisory left in place keeps the
  check red and blocks later dependency PRs.

Full guidance: `.github/instructions/dependencies.instructions.md`.

## Dependency Updates (darc)

Upstream dependencies are managed via [darc/Maestro](https://github.com/dotnet/arcade/blob/main/Documentation/Darc.md). When updating a dependency:

### Updating Xamarin.Apple.Tools.MaciOS

```bash
darc update-dependencies --channel ".NET 10.0.1xx SDK" --name Xamarin.Apple.Tools.MaciOS
```

This updates both `eng/Version.Details.xml` (SHA + version) and `eng/Versions.props` (`XamarinAppleToolsMaciOSVersion`).

**After every update**, check the commits between the old and new SHA for changes that affect CLI behavior:

```bash
# Compare old..new SHA from Version.Details.xml
gh api repos/dotnet/macios-devtools/compare/{oldSha}...{newSha} --jq '.commits[] | .commit.message | split("\n")[0]'
```

Look for:
- New public APIs on `EnvironmentChecker`, `AppleInstaller`, `SimulatorService`, `RuntimeService` that the CLI could leverage
- Bug fixes to simctl JSON parsing, stdout pollution, or ILMerge compatibility that previously required workarounds in our code
- Breaking changes to method signatures the CLI calls (would require code updates)

If the upstream fix removes the need for a workaround in our code, simplify the CLI code accordingly in the same PR.

### Post-Update Smoke Tests (macOS only)

After updating `Xamarin.Apple.Tools.MaciOS` or making changes to `src/Cli/.../Providers/Apple/` or `src/Cli/.../Commands/AppleCommands.cs`, **run the Apple CLI smoke tests** on macOS to verify nothing regressed:

```bash
./eng/smoke-tests/apple-cli-smoke-test.sh
```

The script builds the CLI and runs these checks:
1. `maui apple xcode list --json` — detects installed Xcode
2. `maui apple runtime list --json` — lists simulator runtimes
3. `maui apple simulator list --json` — lists available simulators
4. `maui apple simulator start <name> --json` — boots a simulator
5. `maui apple simulator stop <name> --json` — shuts it down
6. `maui --dry-run apple install --json` — validates install flow (iOS default)
7. `maui --dry-run apple install --platform all --json` — validates all-platform install

You can also pass a pre-built binary: `./eng/smoke-tests/apple-cli-smoke-test.sh path/to/maui`

> **Note**: These tests require macOS with Xcode installed. They are skipped automatically on other platforms. If you are not on macOS, skip this step — CI on macOS runners will catch regressions.

## CI/CD — New Product Checklist

When adding a new product to this repo you **must** set up two CI surfaces: a GitHub Actions workflow for PR validation and a build job + publish stage in the Azure DevOps official pipeline for signing and NuGet.org publishing.

You **must** also provide documentation:

- **Product README** — create two READMEs: (1) a contributor README at the product root (`src/{Product}/README.md`) for GitHub browsing with features, build instructions, and architecture; (2) a NuGet README next to the shipping csproj (`src/{Product}/Microsoft.Maui.{Product}/README.md`) with install, quick start, and usage examples. Pack the NuGet README via `<None Include="README.md" Pack="true" PackagePath="/" />` and set `<PackRepoRootReadme>false</PackRepoRootReadme>`. Both should include: product name, features, platform support matrix, quick start, packages, requirements, and experimental status warning. Keep descriptions aligned to avoid drift. **Images in NuGet READMEs must use absolute URLs** (e.g., `https://raw.githubusercontent.com/dotnet/maui-labs/main/...`) — relative paths break on NuGet.org since images aren't inside the `.nupkg`.
- **Root README entry** — add a section under `## Products` in the repo-root `README.md` with a brief description, feature highlights, and package table.

### Step 1: GitHub Actions PR / Push Workflow

Create `.github/workflows/ci-{product}.yml`. Use the template below — replace every `{Product}` (PascalCase) and `{product}` (lowercase) placeholder.

```yaml
name: CI - {Product}

on:
  push:
    branches: [main]
    paths:
      - 'src/{Product}/**'
      # CUSTOMIZE: add paths for cross-product ProjectReference dependencies, e.g.:
      # - 'src/OtherProduct/SharedLib/**'
      - 'eng/**'
      - 'Directory.Build.props'
      - 'Directory.Build.targets'
      - 'Directory.Packages.props'
      - 'global.json'
      - 'NuGet.config'
  pull_request:
    # IMPORTANT: 'edited' is required — without it CI does not run when
    # GitHub auto-retargets a PR after a stacked branch merges.
    types: [opened, synchronize, reopened, edited]
    branches: [main]
    paths:
      - 'src/{Product}/**'
      # CUSTOMIZE: add paths for cross-product ProjectReference dependencies, e.g.:
      # - 'src/OtherProduct/SharedLib/**'
      - 'eng/**'
      - 'Directory.Build.props'
      - 'Directory.Build.targets'
      - 'Directory.Packages.props'
      - 'global.json'
      - 'NuGet.config'

jobs:
  build:
    uses: ./.github/workflows/_build.yml
    with:
      project-path: src/{Product}/{Product}.slnf   # CUSTOMIZE: path to solution filter (.slnf or .slnx)
      project-name: {product}                       # CUSTOMIZE: lowercase, used in artifact names
      run-tests: true
      pack: true
      install-workloads: true                       # CUSTOMIZE: set false for net10.0-only products (no MAUI TFMs)
      # os: '["macos-latest", "windows-latest"]'    # CUSTOMIZE: default is macOS + Windows
      # native-deps: 'sudo apt-get install ...'     # CUSTOMIZE: if Linux-only with native libs
```

#### `_build.yml` inputs reference

| Input | Default | When to change |
|-------|---------|---------------|
| `install-workloads` | `true` | Set `false` if the product targets only `net10.0` (no MAUI TFMs) |
| `os` | `["macos-latest", "windows-latest"]` | Override to `["ubuntu-24.04"]` for Linux-only products |
| `native-deps` | *(empty)* | Provide an `apt-get install` command if the product needs native libraries (e.g., GTK4) |
| `pack` | `false` | Set `true` if the product produces NuGet packages |
| `run-tests` | `true` | Set `false` only if there are no tests yet |

### Step 2: Azure DevOps Official Pipeline

The official pipeline is **`eng/pipelines/devflow-official.yml`**. It handles Arcade-based builds with MicroBuild/ESRP signing and NuGet.org publishing. For each new product, add **three** blocks:

#### a) Publish parameter (at the top, in the `parameters:` section)

```yaml
- name: publish{Product}Nuget
  displayName: 'Publish {Product} packages to NuGet.org'
  type: boolean
  default: false
```

#### b) Build job (in the `build` stage, under `jobs:`, parallel with existing jobs)

```yaml
          - job: {Product}
            displayName: {Product} - Windows
            pool:
              name: NetCore1ESPool-Internal
              demands: ImageOverride -equals windows.vs2026.amd64
            strategy:
              matrix:
                Release:
                  _BuildConfig: Release
                  _OfficialBuildArgs: /p:DotNetSignType=$(_SignType)
                    /p:TeamName=$(_TeamName)
                    /p:OfficialBuildId=$(BUILD.BUILDNUMBER)
            steps:
            - task: UseDotNet@2
              displayName: Install .NET SDK
              inputs:
                useGlobalJson: true
            # CUSTOMIZE: If the product needs MAUI workloads, add these steps
            # before the build (copy from the DevFlow job):
            #   - Install MAUI workloads through Arcade's wrapper
            #     (eng\common\dotnet.cmd workload install maui ...)
            #   - Install Android SDK dependencies through eng/common/dotnet.ps1
            #     in a pwsh step so quoted paths remain a single argument
            - script: eng\common\cibuild.cmd
                -configuration $(_BuildConfig)
                -prepareMachine
                -projects $(Build.SourcesDirectory)\src\{Product}\{Product}.slnf
                $(_OfficialBuildArgs)
              displayName: Build and Test {Product}
```

> **SDK versioning:** Use `UseDotNet@2` with an explicit `version:` matching the repo-root `global.json` SDK version, **not** `useGlobalJson: true` — the latter scans the entire checkout and can find nested `global.json` files (e.g. Comet's .NET 11 preview) that break non-Comet jobs.
>
> **Workload versioning:** Always pin workload installs with `--version <pinned>`. Check `_build.yml` for the current pinned version. Unpinned installs cause version drift between CI and official builds.
>
> **macOS/Apple builds:** Products targeting Apple TFMs (`net10.0-macos`, `-ios`, `-maccatalyst`) that are **pure managed C#** can build on Windows — the macOS workload provides Apple reference assemblies for cross-compilation, and MicroBuild signs on Windows automatically. Products with **native code** (e.g. Swift bindings) require a **two-stage build**: a macOS job that compiles native artifacts, then a Windows job that downloads them and packs/signs. Use `pool: { name: Azure Pipelines, vmImage: macos-latest-internal, os: macOS }` with `templateContext: outputs:` for the macOS stage. Add `sudo xcode-select` before workload install — check https://aka.ms/xcode-requirement for the required Xcode version. See the `EssentialsAI_macOS`/`EssentialsAI` job pair for the native two-stage pattern, and the `MacOS` job for the pure managed single-stage pattern.

#### c) Conditional publish stage (at the bottom, after the other `publish_*_nuget` stages)

This stage filters the product's `.nupkg` files from the shared `PackageArtifacts` artifact, then pushes them to NuGet.org via the `1ES.PublishNuget` task.

```yaml
    # Publish {Product} packages to NuGet.org
    - ${{ if eq(parameters.publish{Product}Nuget, true) }}:
      - stage: publish_{product}_nuget
        displayName: 'Publish {Product} to NuGet.org'
        dependsOn:
        - Validate
        - publish_using_darc
        jobs:
        - job: PrepareArtifacts
          displayName: 'Prepare {Product} Artifacts'
          timeoutInMinutes: 15
          pool:
            name: NetCore1ESPool-Internal
            image: windows.vs2026.amd64
            os: windows
          templateContext:
            outputs:
            - output: pipelineArtifact
              displayName: Publish {Product} Packages
              targetPath: '$(Pipeline.Workspace)/{Product}Packages'
              artifactName: {Product}PackagesForNuGet
          steps:
          - download: current
            artifact: PackageArtifacts
            displayName: Download PackageArtifacts
          - powershell: |
              New-Item -ItemType Directory -Force -Path '$(Pipeline.Workspace)/{Product}Packages'
              # CUSTOMIZE: glob must match the package ID prefix for this product
              Copy-Item '$(Pipeline.Workspace)/PackageArtifacts/Microsoft.Maui.{Product}.*.nupkg' '$(Pipeline.Workspace)/{Product}Packages/' -Verbose
            displayName: Filter {Product} packages

        - job: PublishNuGet
          displayName: 'Push {Product} to NuGet.org'
          dependsOn: PrepareArtifacts
          timeoutInMinutes: 30
          pool:
            name: NetCore1ESPool-Internal
            image: windows.vs2026.amd64
            os: windows
          templateContext:
            type: releaseJob
            isProduction: true
            inputs:
            - input: pipelineArtifact
              artifactName: {Product}PackagesForNuGet
              targetPath: '$(Pipeline.Workspace)/{Product}Packages'
          steps:
          - task: 1ES.PublishNuget@1
            displayName: 'Push {Product} to NuGet.org'
            inputs:
              useDotNetTask: false
              packagesToPush: '$(Pipeline.Workspace)/{Product}Packages/*.nupkg'
              packageParentPath: '$(Pipeline.Workspace)/{Product}Packages'
              nuGetFeedType: external
              publishFeedCredentials: 'nuget.org (dotnetframework)'
```

#### Key conventions

- **Adding a package to an existing product**: update the exact `.slnf` or `.slnx` used by that product's build job in this pipeline, not only the product's main solution. Then verify the publish stage's `Copy-Item` glob matches the new `<PackageId>`. A shipping project omitted from the official build input does not produce a package for release.
- **Package glob pattern**: example: `Microsoft.Maui.{Product}.*.nupkg` — use the actual `<PackageId>` prefix from your `.csproj` files (e.g., Linux GTK4 uses `Microsoft.Maui.Platforms.Linux.Gtk4.*.nupkg`).
- **`dependsOn: [Validate, publish_using_darc]`**: these stages come from the Arcade post-build template (`eng/common/templates-official/post-build/post-build.yml`) and must always be listed.
- **Signing**: All shipped NuGet packages must build on Windows so MicroBuild/ESRP can sign the DLLs. If the product is Linux-only, build *and pack* on Windows (signing), then optionally add a separate Linux verification job (see the `LinuxGtk4_LinuxVerify` job for the pattern).
- **`publishFeedCredentials`**: Always use `'nuget.org (dotnetframework)'` — this is the service connection configured in the Azure DevOps project.

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
│   │   ├── js/                                         # JS/TS Inspector hosts — npm workspaces, SHIPS
│   │   │   ├── devflow-client/                         # Shared TypeScript client library
│   │   │   ├── vscode-inspector/                       # VS Code extension host (packaged as .vsix)
│   │   │   └── test/                                   # node --test suites
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
├── docs/                                 # Contributor documentation (docs/DevFlow/inspector.md, etc.)
├── plugins/                              # Distributed agent skills (see Skills Marketplace)
├── .github/                              # Workflows, instructions, agents
│   └── extensions/maui-devflow-canvas/   # Copilot Canvas host for DevFlow (npm)
├── eng/                                  # Shared build infrastructure
│   ├── pipelines/                        # Azure DevOps pipeline definitions
│   ├── scripts/                          # Repo maintenance scripts (audit-npm.mjs)
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
5. If the product introduces a `package.json`, register its directory in `.github/dependabot.yml` and verify with `node eng/scripts/audit-npm.mjs` (see **Dependencies and Supply Chain**)

### Documentation

6. Create **two READMEs**:
   - A **contributor README** at the product root (e.g. `src/{NewProduct}/README.md`) for GitHub browsing — describes features, build instructions, architecture, and links to the NuGet README.
   - A **NuGet README** next to the shipping csproj (e.g. `src/{NewProduct}/Microsoft.Maui.{NewProduct}/README.md`) — consumer-facing with install, quick start, and usage examples. Pack it via `<None Include="README.md" Pack="true" PackagePath="/" />` in the csproj and set `<PackRepoRootReadme>false</PackRepoRootReadme>` to avoid duplicating the repo-root README. **Images must use absolute URLs** (`https://raw.githubusercontent.com/dotnet/maui-labs/main/...`) — relative paths break on NuGet.org.
   
   Both should include: product name, feature list, platform support matrix, quick start code, package table, requirements, and experimental status warning. Keep feature descriptions aligned to avoid drift.
7. Add a section for the product in the **repo-root `README.md`** under `## Products` with a brief description, feature highlights, and package table.

### CI/CD Setup

8. **GitHub Actions**: Create `.github/workflows/ci-{newproduct}.yml` calling the reusable `_build.yml` workflow. Must include `pull_request.types: [opened, synchronize, reopened, edited]` and path filters scoped to the product source plus shared build files.
9. **Azure DevOps**: Edit `eng/pipelines/devflow-official.yml` — add a publish parameter, a build job in the `build` stage, and a conditional publish stage for NuGet.org. Run workload installation through Arcade's SDK wrapper (`eng\common\dotnet.cmd workload install ...`) so it uses the same SDK that `cibuild.cmd` selects without assuming a repo-local `.dotnet` directory exists. For commands with quoted arguments or paths containing spaces, invoke `eng/common/dotnet.ps1` from a `pwsh` step instead of the CMD shim. If using `UseDotNet@2` instead, set an explicit `version:` matching `global.json` (not `useGlobalJson: true`). Pin workloads with `--version` matching `_build.yml`. Pure managed Apple products can build on Windows (workload provides reference assemblies). Products with native code (e.g. Swift) need a two-stage build: macOS compiles native + Windows packs/signs. See `EssentialsAI_macOS`/`EssentialsAI` for the native pattern, `MacOS` for the managed pattern.

> **Complete copy-paste templates** for both the GitHub Actions workflow and all three Azure DevOps blocks (parameter, build job, publish stage) are in `.github/copilot-instructions.md` under **"CI/CD — New Product Checklist"**.

## Maui.Client Conventions (Future — Not Yet Present)

A Client product (`src/Client/`) is planned but not yet present in this repository. When added, it will use a DI-based architecture with provider interfaces:

- `IAndroidProvider` — discovers and installs Android SDK components
- `IJdkManager` — manages JDK installations
- `IDeviceManager` — lists and manages Android emulators
- `IDoctorService` — runs environment health checks

Define an interface first, implement it, register in `Program.Services`.

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
