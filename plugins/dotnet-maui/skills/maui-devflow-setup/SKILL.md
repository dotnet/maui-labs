---
name: maui-devflow-setup
description: >-
  Wire an existing .NET MAUI project for DevFlow automation by adding the
  right NuGet packages and calling AddMauiDevFlowAgent() in MauiProgram.cs.
  USE FOR: user asks to "set up DevFlow", "install MAUI DevFlow agent",
  "enable DevFlow in this app", or the SessionStart hook reports the project
  is not wired. DO NOT USE FOR: debugging an already-wired DevFlow connection
  (use devflow-connect), building / running the app, editing platform folders
  outside MauiProgram.cs, or adding DevFlow to a non-MAUI project.
---

# Wire a .NET MAUI project for DevFlow

This skill teaches you to add the DevFlow agent to an existing .NET MAUI app so
the `maui devflow` CLI and MCP tools can talk to it. You read the project,
decide what to change, and edit the files yourself — there is **no CLI helper
that rewrites the project**.

> **DevFlow is a Debug-only diagnostic.** Everything you add must be gated so
> Release builds ship without it. The package `.targets` already append the
> `DEVFLOW` compiler symbol when `EnableDevFlow=true`; your job is to set the
> property correctly and wrap the call site.

## Current shipping version

Pin to this when adding `PackageReference` entries:

```
0.1.0-preview.4
```

If a newer version is pinned in the repo's `Directory.Packages.props`, use
that one instead.

## Step 1 — Pre-flight

1. Confirm `maui --version` succeeds in a terminal. If it fails, stop and
   direct the user to install the CLI:
   ```bash
   dotnet tool install -g Microsoft.Maui.Cli --prerelease
   ```
2. Find the MAUI project. Look for a `.csproj` whose content matches any
   of these:
   - `<UseMaui>true</UseMaui>`
   - A `PackageReference` whose `Include` starts with `Microsoft.Maui.` or
     `Platform.Maui.`
   - A `ProjectReference` whose target matches the above
3. If there are multiple candidates, **ask the user which one**. Do not
   guess. Record the absolute path and work exclusively with that file.
4. If no candidate exists, stop. Say: "This doesn't look like a .NET MAUI
   project — DevFlow only supports MAUI apps today."

## Step 2 — Determine the flavor

Inspect the csproj. Pick exactly one:

| Flavor | Signal | Packages to add |
|---|---|---|
| `standard` | Default when none of the others match | `Microsoft.Maui.DevFlow.Agent` |
| `blazor` | References `Microsoft.AspNetCore.Components.WebView.Maui` | `Microsoft.Maui.DevFlow.Agent` + `Microsoft.Maui.DevFlow.Blazor` |
| `gtk` | References `Platform.Maui.Linux.Gtk4` (SDK is usually `Microsoft.NET.Sdk.Razor`) | `Microsoft.Maui.DevFlow.Agent.Gtk` |
| `blazor-gtk` | Both GTK4 *and* WebView Maui references | `Microsoft.Maui.DevFlow.Agent.Gtk` + `Microsoft.Maui.DevFlow.Blazor.Gtk` |

GTK projects often do **not** set `<UseMaui>true</UseMaui>` — detect them by
the `Platform.Maui.Linux.Gtk4` package reference.

## Step 3 — Check "already wired"

The project is already wired only when **both** are true:

1. A `PackageReference` to a `Microsoft.Maui.DevFlow.*` package exists.
2. That reference sits in an `ItemGroup` or `PropertyGroup` with
   `Label="DevFlow"`, or the csproj contains the comment marker
   `<!-- <DevFlow> -->` fencing the DevFlow block.

If both hold → report "already wired", run `maui devflow diagnose`, and stop.

If a bare `Microsoft.Maui.DevFlow.*` reference exists **without** the
`Label="DevFlow"` marker, the user is managing it manually. **Do not edit
it.** Tell the user what you found and ask whether to take over (promote
their block to the standard labeled shape) or leave it alone.

## Step 4 — Check for Central Package Management (CPM)

Walk upward from the csproj looking for `Directory.Packages.props` with
`<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`. If
present, you will put versions there and leave versions off the
`PackageReference` in the csproj.

## Step 5 — Edit the csproj

Add a labeled `PropertyGroup` (anywhere; keep it near other PropertyGroups):

```xml
<PropertyGroup Label="DevFlow">
  <EnableDevFlow Condition="'$(EnableDevFlow)' == '' AND '$(Configuration)' == 'Debug'">true</EnableDevFlow>
</PropertyGroup>
```

Add a labeled `ItemGroup` guarded by the property. The exact `PackageReference`
set comes from the flavor table above. Example for `blazor` (non-CPM):

```xml
<!-- <DevFlow> -->
<ItemGroup Label="DevFlow" Condition="'$(EnableDevFlow)' == 'true'">
  <PackageReference Include="Microsoft.Maui.DevFlow.Agent" Version="0.1.0-preview.4" />
  <PackageReference Include="Microsoft.Maui.DevFlow.Blazor" Version="0.1.0-preview.4" />
</ItemGroup>
<!-- </DevFlow> -->
```

For CPM, omit the `Version=` attributes and add matching `PackageVersion`
entries to `Directory.Packages.props`:

```xml
<PackageVersion Include="Microsoft.Maui.DevFlow.Agent" Version="0.1.0-preview.4" />
<PackageVersion Include="Microsoft.Maui.DevFlow.Blazor" Version="0.1.0-preview.4" />
```

Rules:

- Keep `Label="DevFlow"` on every group you add. The setup skill and the
  hooks use it to recognize DevFlow's footprint.
- Do not touch anything outside the labeled groups.
- Preserve the existing csproj formatting (indent, attribute order) as
  closely as your editor allows.

## Step 6 — Edit `MauiProgram.cs`

Find the `CreateMauiApp` (or `CreateMauiAppBuilder`) method. Add `using`
statements and the extension method calls inside a `#if DEBUG && DEVFLOW`
guard:

```csharp
#if DEBUG && DEVFLOW
using Microsoft.Maui.DevFlow;                 // AddMauiDevFlowAgent
using Microsoft.Maui.DevFlow.Blazor;          // AddMauiBlazorDevFlowTools (blazor flavors only)
#endif
```

Then, after `.UseMauiApp<App>()` (or wherever the builder is configured):

```csharp
#if DEBUG && DEVFLOW
// <DevFlow>
builder.AddMauiDevFlowAgent();
builder.AddMauiBlazorDevFlowTools();          // blazor / blazor-gtk only
// </DevFlow>
#endif
```

Flavor picks:

| Flavor | Call |
|---|---|
| standard | `builder.AddMauiDevFlowAgent();` |
| blazor | `builder.AddMauiDevFlowAgent(); builder.AddMauiBlazorDevFlowTools();` |
| gtk | `builder.AddMauiDevFlowAgent();` |
| blazor-gtk | `builder.AddMauiDevFlowAgent(); builder.AddMauiBlazorDevFlowTools();` |

Note: the Gtk packages ship the *same* `AddMauiDevFlowAgent` /
`AddMauiBlazorDevFlowTools` extension method names — the package reference
selects the GTK implementation at build time. Your `MauiProgram.cs` code
looks the same whether the flavor is `blazor` or `blazor-gtk`.

`DEVFLOW` is defined automatically by the agent package `.targets` when
`EnableDevFlow=true`. `#if DEBUG && DEVFLOW` keeps the call out of Release
even if someone flips `EnableDevFlow=true` in a Release build by mistake.

## Step 7 — Optional `.devflow` config

If the user has a preferred port (rare — the CLI auto-discovers), write
`.devflow` next to the csproj:

```json
{ "port": 19223 }
```

Otherwise skip this step. The CLI broker picks a free port automatically.

Add `.devflow/` (the hook state directory) to the project's `.gitignore`:

```
# DevFlow session state
.devflow/
```

If a legacy `.mauidevflow` file exists, migrate it to `.devflow` and delete
the old one. The `.targets` prefers `.devflow` and warns on the legacy name.

## Step 8 — Verify

1. Build the project once in Debug: `dotnet build -c Debug`. It should
   succeed. Confirm the `DEVFLOW` symbol is present with
   `dotnet build -c Debug /getProperty:DefineConstants` — the output should
   contain `DEVFLOW`.
2. Run `maui devflow diagnose`. It reports broker status, connected
   agents, and which projects reference a `Microsoft.Maui.DevFlow.*`
   package. Read the output and check:
   - Your project is listed under "DevFlow-enabled projects".
   - The broker is running (or you know how to start it).
   - There are no unexpected agent processes from another session.
3. Tell the user what to do next:
   - Launch the app in Debug.
   - Run `maui devflow wait` to confirm the agent registers with the broker.
   - Once connected, the MCP tools (`maui_tree`, `maui_tap`, etc.) are
     available to this session.

## Diagnosing an existing setup

If the user asks *"why isn't DevFlow working?"* or *"is my project wired
correctly?"*, **do the inspection yourself** — do not rely on a CLI to
flag mismatches. The CLI can't always see through `Directory.Build.props`,
transitive NuGet `.targets`, or custom SDKs. You can.

Check all of the following and call out anything that doesn't match:

1. **Package present?** Is `Microsoft.Maui.DevFlow.Agent` (or `.Agent.Gtk`)
   referenced via `PackageReference` — either in the csproj, a
   `Directory.Packages.props`, a `Directory.Build.props`, or a shared
   `.props`/`.targets` file imported by the project? Use `grep -r` across
   the solution if you're not sure. For Blazor hybrid projects, also check
   for `Microsoft.Maui.DevFlow.Blazor` (or `.Blazor.Gtk`).
2. **`EnableDevFlow` declared?** Is the `<EnableDevFlow>` property set
   somewhere MSBuild will evaluate for this project? It only needs to end
   up `true` for Debug. Watch for:
   - Unconditional `<EnableDevFlow>true</EnableDevFlow>` (will leak into
     Release — flag this).
   - `<EnableDevFlow>` conditioned on `'$(Configuration)' == 'Release'`
     (almost always wrong — flag this).
   - No `<EnableDevFlow>` at all (the `.targets` from the Agent package
     will never define `DEVFLOW` — flag this and offer to add it).
3. **`DEVFLOW` symbol actually defined?** Run
   `dotnet build -c Debug /getProperty:DefineConstants` and look for
   `DEVFLOW` in the output. If it's missing, the `#if DEBUG && DEVFLOW`
   block in `MauiProgram.cs` is being skipped. If it's *also* defined in
   Release, something is hard-coding it into `<DefineConstants>` — find
   and remove that override.
4. **Call site guarded?** Grep for `AddMauiDevFlowAgent()` and verify
   every call is inside `#if DEBUG && DEVFLOW`. An unguarded call ships
   DevFlow into Release builds.
5. **Labeled block intact?** Is the `Label="DevFlow"` still on the
   PropertyGroup and ItemGroup? If a merge or refactor stripped the
   labels, the setup skill won't treat the block as owned on re-run.
6. **Runtime connection?** Ask the user what they see from
   `maui devflow list` or `maui devflow wait`. If the agent never
   registers, route to the `devflow-connect` skill (broker lifecycle,
   port forwarding on Android emulators, etc.).

Report findings as a short bullet list with concrete suggested fixes.
Prefer to *propose* edits over making them silently — diagnosis time is
not setup time.

## Guardrails

- **Never remove** an existing un-labeled DevFlow reference. Ask the user.
- **Never edit** `eng/common/` (Arcade-owned), `Directory.Build.props`
  outside of CPM additions, or anything platform-specific under `Platforms/`.
- **Never commit** secrets to `.devflow`. It only holds a port number.
- **Do not skip** the `#if DEBUG && DEVFLOW` guard. A release build with the
  agent linked in is a hard no-ship.
- **Do not change** the assembly ID, package ID, or root namespace of the
  user's project while wiring DevFlow.
- **Do not** add DevFlow to a library or test project. It belongs on the
  single head MAUI app project.
