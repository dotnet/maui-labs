# dotnet-maui plugin

The `dotnet-maui` plugin packages everything an AI coding agent needs to help a developer automate, inspect, and debug a .NET MAUI app with DevFlow.

## What's in the box

| Component | What it does |
|---|---|
| **MCP server** — `maui devflow mcp` (stdio) | Exposes the 49 `maui_*` tools (visual tree, tap, fill, screenshot, CDP, network monitor, storage, sensors, …) to the AI host. |
| **Skill: `maui-devflow-setup`** | Agent-driven recipe that reads a MAUI project, picks the right DevFlow packages (standard / Blazor / GTK), and wires `<EnableDevFlow>` + `#if DEBUG && DEVFLOW` + `AddMauiDevFlowAgent()` into `MauiProgram.cs`. |
| **Skill: `devflow-connect`** | Troubleshooting recipe for broker/agent connectivity (port forwarding, conflicts, platform-specific quirks). |
| **Subagent: `maui-devflow-specialist`** | Optional entry point that auto-routes "set up DevFlow" vs. "DevFlow isn't connecting" work to the right skill and leans on the MCP tools for runtime inspection. |
| **Hooks: `SessionStart` + `PostToolUse`** | Detects a MAUI project that isn't wired for DevFlow and nudges the agent to run the setup skill. Silent if the CLI is missing, the project isn't MAUI, or DevFlow is already wired. |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- MAUI workload: `dotnet workload install maui`
- `maui` CLI: `dotnet tool install -g Microsoft.Maui.Cli --prerelease`

If the CLI is missing, the plugin's `SessionStart` hook prints a one-shot install hint instead of starting the MCP server. MCP tools remain unavailable until you install the CLI and reload the plugin.

## Install

```bash
/plugin marketplace add dotnet/maui-labs
/plugin install dotnet-maui@dotnet-maui-labs
```

Host-specific notes:

- **Copilot CLI / Claude Code** — auto-discovers MCP from `plugin.json`; no `.vscode/mcp.json` edits needed.
- **VS Code MCP** — works via the same mechanism when the plugin is installed into a compatible host.

## Usage

Open any .NET MAUI project in your AI host. The `SessionStart` hook runs one of:

| Project state | Nudge |
|---|---|
| Not a MAUI project | (silent) |
| MAUI, not wired | *"🔧 MAUI project detected (standard) but DevFlow is not wired. Say 'set up DevFlow' …"* |
| MAUI, wired | (silent) |
| MAUI, `maui` CLI missing | *"🔧 MAUI project detected, but the `maui` CLI is not installed …"* |

Say **"set up DevFlow"** (or "install MAUI DevFlow"), and the specialist subagent follows the `maui-devflow-setup` skill. It:

1. Detects the flavor (standard / Blazor hybrid / GTK / Blazor GTK) from the csproj.
2. Picks the matching `Microsoft.Maui.DevFlow.*` packages.
3. Honors Central Package Management (writes `PackageVersion` entries into `Directory.Packages.props` when applicable).
4. Adds a `PropertyGroup Label="DevFlow"` with `<EnableDevFlow Condition="'$(Configuration)' == 'Debug'">true</EnableDevFlow>`.
5. Adds a labeled `ItemGroup` gated by `Condition="'$(EnableDevFlow)' == 'true'"`.
6. Inserts the guarded `#if DEBUG && DEVFLOW` call in `MauiProgram.cs`.
7. Runs `maui devflow diagnose` to verify.

## How it knows what to edit

The skill teaches the AI to inspect the project directly — there is **no** `maui devflow recommend-packages` helper. Detection uses MSBuild as the source of truth whenever possible, not regex (see `skills/maui-devflow-setup/SKILL.md`):

- **Is this MAUI?** `dotnet msbuild -getProperty:UseMaui` returns `true`, **or** the `PackageReference` items (also from MSBuild) include anything under `Microsoft.Maui.*` / `Platform.Maui.*`.
- **Blazor hybrid?** `PackageReference` items include `Microsoft.AspNetCore.Components.WebView.Maui`.
- **GTK?** `PackageReference` items include `Platform.Maui.Linux.Gtk4` (or the `Agent.Gtk` / `Blazor.Gtk` DevFlow packages).
- **Central Package Management?** `Directory.Packages.props` with `ManagePackageVersionsCentrally=true` found walking upward from the csproj.
- **Already wired?** Any `Microsoft.Maui.DevFlow.*` entry in the resolved `PackageReference` list. This works whether the reference lives in the csproj, a `Directory.Packages.props`, a custom `.targets`, or gets surfaced by an SDK — because MSBuild is doing the evaluation, not a regex over one file.

The `SessionStart` / `PostToolUse` hook applies the same MSBuild evaluation. Tests override the eval via `MAUI_DEVFLOW_HOOK_STUB=<json-file>` so CI does not need a fully restored .NET SDK.

## Hooks

The hook logic lives in the `maui` CLI itself — `maui devflow hook check <event>` does the MSBuild evaluation, classification, debounce, and JSON emission. The plugin ships two tiny platform wrappers (`hooks/check.sh` for macOS/Linux, `hooks/check.cmd` for Windows) that detect a missing `maui` CLI and forward to the subcommand otherwise. Keeping the logic in the CLI means there's no extra runtime dependency (no Node, no Python, no `jq`) — the same evaluator the rest of the tool uses handles hooks too.

The CLI asks MSBuild for the authoritative view of each csproj in the project root via `dotnet msbuild -getProperty:UseMaui,EnableDevFlow -getItem:PackageReference`. That JSON output already accounts for `Directory.Build.props`, `Directory.Packages.props`, custom SDKs, and transitive `.targets` imports — so the nudge doesn't false-fire when the wiring lives somewhere other than the csproj itself. A failed or timed-out MSBuild evaluation stays silent.

- Fires on `SessionStart` once per changed project state.
- Fires on `PostToolUse` only when the edited file is `MauiProgram.cs`, a `.csproj`, `Directory.Packages.props`, or a `Directory.Build.*` file. Unrelated edits short-circuit before MSBuild runs.
- Debounces via `${CLAUDE_PLUGIN_DATA}/hook-state/<repo-hash>.json` — **outside** the user's repo, so there's no clash with the `.devflow` config file and no `.gitignore` hygiene to worry about.
- When the `maui` CLI is not on PATH the wrapper prints a one-shot install hint (`dotnet tool install -g Microsoft.Maui.Cli --prerelease`) and exits silently otherwise.
- Never blocks the host — exits 0 with empty stdout if in doubt.

Opt out by uninstalling the plugin, or disable it globally in the host's plugin settings.

## MSBuild knobs you'll see after setup

| Property / symbol | Purpose |
|---|---|
| `<EnableDevFlow>` | User-facing switch. Set to `true` in Debug; Agent `.targets` define the `DEVFLOW` compiler symbol when true. |
| `DEVFLOW` (compile symbol) | Gates the `AddMauiDevFlowAgent()` call via `#if DEBUG && DEVFLOW`. |
| `<DevFlowConstant>` (optional) | Override the symbol name if `DEVFLOW` clashes with something in your solution. |
| `<DevFlowAgentVersion>` (optional) | Pin the Agent package version from `Directory.Packages.props`. |
| Labels: `Label="DevFlow"` | Marks ItemGroups / PropertyGroups the skill owns. Re-running the skill is idempotent; unlabeled blocks are left alone. |
| `.devflow` (optional, gitignored recommended) | Per-project config (e.g., custom agent port). Legacy `.mauidevflow` is still read with a one-time warning. |

## Troubleshooting

Run `maui devflow diagnose` — it reports broker status, connected agents, and DevFlow-enabled projects in the current directory.

Deeper *"is my project wired correctly?"* diagnosis is handled by the `maui-devflow-setup` skill, not the CLI. Static regex checks over a single csproj can't see through `Directory.Build.props`, transitive NuGet `.targets`, or custom SDKs — the AI can. Ask the agent something like *"check my DevFlow wiring"* and it will:

- Grep the solution for `Microsoft.Maui.DevFlow.*` package references (including in shared props/targets).
- Evaluate where `<EnableDevFlow>` is set and whether it's correctly gated to Debug.
- Confirm the `DEVFLOW` compile symbol actually reaches the MAUI app via `dotnet build -c Debug /getProperty:DefineConstants`.
- Verify every `AddMauiDevFlowAgent()` call site is guarded by `#if DEBUG && DEVFLOW`.
- Route runtime connection failures to the `devflow-connect` skill.

Connection failures (agent doesn't show up in `maui devflow list`) are covered by the `devflow-connect` skill — broker lifecycle, `adb reverse` for Android emulators, port conflicts, and iOS-simulator specifics.

## Manual alternative (no plugin)

If you prefer to bypass the plugin entirely:

```xml
<!-- YourApp.csproj -->
<PropertyGroup Label="DevFlow">
  <EnableDevFlow Condition="'$(EnableDevFlow)' == '' AND '$(Configuration)' == 'Debug'">true</EnableDevFlow>
</PropertyGroup>
<ItemGroup Label="DevFlow" Condition="'$(EnableDevFlow)' == 'true'">
  <PackageReference Include="Microsoft.Maui.DevFlow.Agent" Version="0.1.0-preview.4" />
</ItemGroup>
```

```csharp
// MauiProgram.cs
public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder.UseMauiApp<App>();
#if DEBUG && DEVFLOW
    // <DevFlow>
    builder.AddMauiDevFlowAgent();
    // </DevFlow>
#endif
    return builder.Build();
}
```

And add `maui devflow mcp` to your host's MCP config manually (e.g., `.vscode/mcp.json`). The skill path simply automates these edits.

## Migrating from `maui-devflow update-skill`

Earlier drafts shipped a standalone skill that the CLI installed via `maui devflow update-skill`. That flow is now in **maintenance mode**:

- **New users**: install the plugin above. It bundles the canonical skill, the MCP server, and the setup flow.
- **Existing users** on the standalone skill: no immediate action required. `maui devflow update-skill` still works. When you're ready, uninstall the standalone skill in your host and install the plugin — the plugin's `maui-devflow-setup` skill is the same content plus the setup guardrails and hooks.

See also: the top-level [`README.md`](../../README.md#quick-start-wire-devflow-into-a-maui-app) and the plugin marketplace [`plugins/README.md`](../README.md).
