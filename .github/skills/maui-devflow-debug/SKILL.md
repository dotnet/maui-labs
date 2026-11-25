---
name: maui-devflow-debug
description: >-
  Run DevFlow inspect-and-fix loops for MAUI apps. USE FOR: launch, .NET 11
  `dotnet watch` and Hot Reload, device selection, agent recovery, broker/port/adb,
  tree/screenshot, Blazor CDP, and iterative UI debugging. DO NOT USE FOR:
  first-time setup or non-MAUI automation. INVOKES: DevFlow `maui_*` MCP tools
  when available; `maui devflow` CLI as fallback; `dotnet`, `adb`, and `simctl`
  for build, deployment, and connectivity work.
---

# DevFlow Debug

Use this skill for the active debugging loop after a MAUI app has DevFlow
packages and `builder.AddMauiDevFlowAgent()` registered.

## When to Use

- Build and run a MAUI app on Android, iOS, Mac Catalyst, macOS, Windows, or GTK.
- Choose or create a simulator/emulator for a project.
- Wait for a DevFlow agent, inspect the visual tree, tap/fill UI, or capture screenshots.
- Recover from DevFlow connection failures after the app is integrated, including broker, port, and Android adb forwarding issues.
- Debug Blazor Hybrid content through DevFlow WebView/CDP commands.
- Read app logs, network captures, preferences, device info, or recordings through DevFlow.
- Iterate on an app bug with a build -> deploy -> inspect -> fix loop.

## Route Elsewhere

- If DevFlow packages or `MauiProgram.cs` registration are missing, use `maui-devflow-onboard`.
- If the failure is a generic build or SDK issue with no DevFlow angle, use normal .NET/MAUI diagnostics.

## Optional Session Feedback Nudge

If you have retried the same MAUI DevFlow workflow several times, tried multiple
workarounds, or are ending a long DevFlow-assisted debugging session, ask
whether the user wants to run `maui-devflow-session-review` to summarize friction
for MAUI DevFlow product feedback. Do not run it automatically.

## Core Loop

1. Confirm the app is already integrated:

   ```bash
   grep -rl "Microsoft.Maui.DevFlow" --include="*.csproj" .
   ```

   If no project has DevFlow package references, stop and switch to `maui-devflow-onboard`.

2. Pick the target framework and launch target. Do not assume `net10.0`; inspect the project first.

   ```bash
   grep -i "TargetFramework" *.csproj Directory.Build.props 2>/dev/null
   ```

3. Start or select the device/emulator. For Android and iOS, avoid reusing a simulator/emulator that is already running another app under investigation.

4. Launch the app and keep the launch process alive when required.

   - **.NET 11 Debug on Android, iOS, or Mac Catalyst: launch under watch from
     the start** whenever you may edit source after launching (reproduce-then-fix,
     iterative UI tuning, or any bug fix you will verify in the running app). Use
     `maui devflow watch start` (see the fast path below) instead of
     `dotnet build -t:Run`; a later edit then applies in about a second via
     `maui devflow watch wait` instead of a full rebuild and redeploy.
   - Otherwise, iOS, Android, and Mac Catalyst `dotnet build -t:Run` usually block for the app lifetime.
   - GTK `dotnet run` blocks for the app lifetime.
   - macOS AppKit builds can exit after compiling; launch the `.app` separately.

5. Wait for the DevFlow agent before inspecting UI:

   ```bash
   maui devflow wait
   maui devflow agent status
   maui devflow ui tree --depth 3 --fields "id,type,text,automationId"
   ```

   Treat `agent status` as the runtime truth for reachability and app identity.
   `diagnose` is broader environment state; it can report broker/project details
   without proving the current app is reachable.

   If `wait`, `list`, or `ui tree` cannot connect after the app is running, load `references/connectivity.md` and recover the broker/agent connection before continuing.

6. Prefer AutomationId-first validation for UI flows:

   ```bash
   maui devflow ui query --automationId save-button
   maui devflow ui tap <element-id-from-query>
   ```

   If important controls do not have stable `AutomationId`s, add them before
   relying on text, coordinates, screenshots, or brittle tree positions.

7. Inspect and interact. Before relaunching after a source edit, use the .NET 11 `dotnet watch` fast path below when its prerequisites are met; verify the changed behavior through DevFlow.

## .NET 11 `dotnet watch` Fast Path

For .NET 11 MAUI Debug builds targeting Android, iOS, or Mac Catalyst, start with
`dotnet watch` before rebuilding and redeploying. Keep the watcher running while
you make edits. It can apply supported C# Hot Reload and XAML updates to the
running app; it may restart for unsupported edits.

Prefer `maui devflow watch start` when the installed CLI has it: it runs the
watcher in the background from the project directory and reports one result per
edit through `maui devflow watch wait`. Otherwise run `dotnet watch` yourself,
from the app project directory. This matters in repositories
that place build outputs under a separate `artifacts` directory, because the
platform launcher can resolve its app bundle relative to the current directory.
Choose an explicit device when more than one target is available:

```bash
# Preferred: managed session (also: maui_watch_* MCP tools)
maui devflow watch start --project <path/to/App.csproj> -f net11.0-android --device <device-id> --json
maui devflow watch wait --json && maui devflow ui query --automationId <id>

# iOS Simulator
dotnet watch --framework net11.0-ios --device <simulator-udid> --no-launch-profile --non-interactive run

# Android
dotnet watch --framework net11.0-android --device <device-id> --no-launch-profile --non-interactive run

# Mac Catalyst
dotnet watch --framework net11.0-maccatalyst --no-launch-profile --non-interactive run
```

For this repository's multi-targeted `samples/DevFlow.Sample`, also pass
`--property:DevFlowSampleWatchTarget=<net11 TFM>` (or `--property DevFlowSampleWatchTarget=<net11 TFM>` with `maui devflow watch start`) matching the selected platform.
Without that sample-specific selector, an Android C# update failed because the
watcher's update compilation included sources from other platforms. Do not copy
this property to unrelated app projects.

For XAML updates, ensure the .NET 11 project uses the source-generated XAML
inflator and has not disabled incremental Hot Reload. The incremental engine is
enabled by default in Debug builds; the sample project sets:

```xml
<PropertyGroup Condition="'$(TargetFramework)' == 'net11.0-android' or '$(TargetFramework)' == 'net11.0-ios' or '$(TargetFramework)' == 'net11.0-maccatalyst'">
  <MauiXamlInflator>SourceGen</MauiXamlInflator>
</PropertyGroup>
```

These settings establish eligibility, not proof that an edit will be applied
in-place. In this repository's .NET 11 Android, iOS, and Mac Catalyst `DevFlow.Sample`,
repeated existing-label XAML edits were verified in the same process, with
input state preserved and DevFlow source maps refreshed. Before the DevFlow
source-map generator fix, the same edit requested a restart: `dotnet watch --verbose` exposed `ENC0118`
in `DevFlowXamlSourceMaps.g.cs`. If a restart is unexpected, inspect verbose
diagnostics and the DevFlow version rather than assuming XAML is unsupported.
`dotnet watch --list` omitting a XAML file does not establish that it is
unwatched: it may be tracked as a compiler additional document. This result
does not qualify every edit.

Mac Catalyst Debug builds need extra project settings for `dotnet watch`:

```xml
<PropertyGroup Condition="'$(TargetFramework)' == 'net11.0-maccatalyst' and '$(Configuration)' == 'Debug'">
  <MtouchLink>None</MtouchLink>
  <Registrar>partial-static</Registrar>
  <SupportedOSPlatformVersion>17.0</SupportedOSPlatformVersion>
</PropertyGroup>
```

Mac Catalyst arm64 Debug builds trim SDK assemblies by default, which removes
APIs the watch startup hook needs. The app then aborts at startup and the
watcher reports that the process "exited with exit code 0". Disabling linking
also selects `trimmable-static`, which Hot Reload rejects, so set the
`partial-static` registrar explicitly. After changing `MtouchLink`, delete the
Mac Catalyst `obj` and `bin` output: an incremental build can keep packaging
previously trimmed assemblies. On Mac Catalyst the app's console output is not
shown in a non-interactive watcher; check `~/Library/Logs/DiagnosticReports/`
for a startup crash.

After the initial launch, use `maui devflow wait` and `maui devflow agent status`
to verify the running app. For each edit, check the watcher output, then query
the affected control or property through DevFlow to prove the app actually
changed. Load `references/hot-reload.md` for the edit loop and watcher messages. Do not treat a successful build, a live agent, a screenshot alone, or
`maui devflow ui set-property` as proof that source Hot Reload applied.

Prefer method-body edits and supported changes to existing XAML-backed pages or
controls. Project/TFM/package/manifest changes, new structural members, and
other unsupported edits can require a restart; follow the watcher's restart or
rude-edit signal instead of claiming a live update. A watcher message that a
restart is needed means the edit was not hot-reloaded; verify the new app
instance through DevFlow after it restarts.
Treat a Hot Reload compilation error as a failed update: inspect the watcher's
diagnostics and verify the source behavior through DevFlow. A still-connected
app or agent does not prove that the edit was applied.

## Critical Anti-patterns

- Do not treat an empty `maui devflow list` as proof the project is not integrated. `list` is runtime state; project files are source of truth.
- Do not use arbitrary sleeps after launch. Use `maui devflow wait` to gate on the actual agent connection.
- Do not kill an async `dotnet build -t:Run` or `dotnet run` shell while you still need the app; that often kills the app.
- Do not reuse a busy simulator/emulator when multiple MAUI apps or agents may be running.
- Do not debug Blazor WebView DOM issues through the native visual tree alone; use the WebView/CDP commands.
- Do not drive key app flows by coordinates when AutomationIds are available or can be added.
- Do not stop a working .NET 11 `dotnet watch` process after each supported edit; verify the update in the running app before choosing a rebuild/relaunch.
- Do not split a multi-step interaction (for example fill then tap) across separate `maui devflow` processes. Each CLI process holds its own mutation lease for about 10 seconds, so the next process is rejected with "Another DevFlow session is driving this app". Send the steps through one `maui devflow batch` call.
- Run DevFlow CLI commands from the app project directory, or pass `--agent-port` explicitly. From another directory, port discovery can select a different agent or the default port.

## Stop Signals

- Stop and switch to `maui-devflow-onboard` when package references or `AddMauiDevFlowAgent()` are absent.
- Stop and ask which project, device, or agent to target when multiple candidates match.
- Stop rebuilding after two identical failures until you inspect the first meaningful build/runtime error.
- Stop using screenshots for exact property values; query the visual tree or properties instead.

## Reference Map

Load these only when needed:

- `references/setup.md` - detailed integration, package, entitlement, and update notes.
- `references/hot-reload.md` - .NET 11 `dotnet watch` edit loop, reading watcher output, measured speedups, and watcher lifecycle gotchas.
- `references/connectivity.md` - broker, agent, port, Android forwarding, and "no agents connected" recovery.
- `references/android.md` - Android SDK, emulator, adb, build, deploy, and port forwarding details.
- `references/ios-and-mac.md` - iOS simulator, Mac Catalyst, permissions, entitlements, and Apple tooling.
- `references/macos.md` - macOS AppKit project shape, launch model, and troubleshooting.
- `references/linux.md` - GTK/Linux launch, packages, and WebKitGTK notes.
- `references/batch.md` - batching multiple DevFlow UI/WebView operations.
- `references/troubleshooting.md` - build, connection, CDP, and platform-specific failure recovery.
