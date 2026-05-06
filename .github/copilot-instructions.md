# Copilot Instructions for maui-labs

These instructions guide GitHub Copilot and other AI code generation tools when working with the maui-labs repository.

## Platform-Specific Code

DevFlow targets multiple platforms via multi-targeting. The pattern:

- **`Microsoft.Maui.DevFlow.Agent.Core`** targets `net10.0` — all platform-agnostic code lives here.
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

## Agent Architecture (Core/Platform Pattern)

When adding new features to the DevFlow agent:

1. **Add the virtual method in `Agent.Core/DevFlowAgentService.cs`** — this is the platform-agnostic base
2. **Override in `Agent/DevFlowAgentService.cs`** with `#if` directives for platform-specific behavior
3. **Override in `Agent.Gtk/GtkAgentService.cs`** for Linux/GTK if needed

Example:
```csharp
// In Agent.Core/DevFlowAgentService.cs
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

Agent HTTP endpoints are defined in `DevFlowAgentService.cs` (in Agent.Core). When adding new endpoints:

1. Register the route in the `ConfigureRoutes()` method: `_server.MapGet("/api/myendpoint", HandleMyEndpoint);`
2. Implement the handler as `protected virtual async Task<HttpResponse> HandleMyEndpoint(HttpRequest request)`
3. For POST endpoints that accept JSON bodies, define a DTO class at the bottom of the file
4. **Add a corresponding method in `AgentClient`** (in `Microsoft.Maui.DevFlow.Driver`) — this is the public API

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

## Driver Library (AgentClient)

`Microsoft.Maui.DevFlow.Driver/AgentClient.cs` is the **public API for NuGet consumers**. Changes to method signatures are:

- **Binary breaking** — existing compiled code stops working
- **Source breaking** — existing source code fails to compile

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
              demands: ImageOverride -equals windows.vs2026preview.scout.amd64
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
            #   - Provision .NET SDK via Arcade (eng\common\dotnet.cmd --info)
            #   - Install MAUI workloads (.dotnet\dotnet workload install maui ...)
            #   - Install Android SDK dependencies
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
            image: windows.vs2026preview.scout.amd64
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
            image: windows.vs2026preview.scout.amd64
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

- **Package glob pattern**: example: `Microsoft.Maui.{Product}.*.nupkg` — use the actual `<PackageId>` prefix from your `.csproj` files (e.g., Linux GTK4 uses `Microsoft.Maui.Platforms.Linux.Gtk4.*.nupkg`).
- **`dependsOn: [Validate, publish_using_darc]`**: these stages come from the Arcade post-build template (`eng/common/templates-official/post-build/post-build.yml`) and must always be listed.
- **Signing**: All shipped NuGet packages must build on Windows so MicroBuild/ESRP can sign the DLLs. If the product is Linux-only, build *and pack* on Windows (signing), then optionally add a separate Linux verification job (see the `LinuxGtk4_LinuxVerify` job for the pattern).
- **`publishFeedCredentials`**: Always use `'nuget.org (dotnetframework)'` — this is the service connection configured in the Azure DevOps project.

## Maui.Client Conventions (Future — Not Yet Present)

A Client product (`src/Client/`) is planned but not yet present in this repository. When added, it will use a DI-based architecture with provider interfaces:

- `IAndroidProvider` — discovers and installs Android SDK components
- `IJdkManager` — manages JDK installations
- `IDeviceManager` — lists and manages Android emulators
- `IDoctorService` — runs environment health checks

Define an interface first, implement it, register in `Program.Services`.
