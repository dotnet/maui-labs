---
name: maui-devflow-onboard
description: >-
  Add MAUI DevFlow to a .NET MAUI project with agent package references,
  MauiProgram.cs registration, Blazor WebView support, GTK variants, Central
  Package Management guidance, and verification commands. USE FOR: first-time
  DevFlow setup, reviewing what files to edit, choosing DevFlow packages, or
  continuing after `maui devflow init` installs skills. DO NOT USE FOR:
  troubleshooting an already-integrated app that cannot connect, iterative app
  debugging, UI inspection, or generic MAUI build failures (use
  maui-devflow-debug). INVOKES: maui devflow CLI and dotnet CLI.
---

# DevFlow Onboard

Use this skill to add MAUI DevFlow to a project after `maui devflow init` has installed the DevFlow skills.

## When to Use

- The project has no `Microsoft.Maui.DevFlow.*` package references yet.
- The user asks how to onboard, initialize, install, integrate, or set up DevFlow.
- The agent needs to choose between standard, Blazor, GTK, or GTK + Blazor DevFlow packages.
- The repo uses Central Package Management and package edits need to be split correctly.
- `maui devflow init` has installed skills and the user asks for the next project edits.

## Route Elsewhere

- If package references and `AddMauiDevFlowAgent()` are already present but the CLI cannot connect, use `maui-devflow-debug`.
- If an agent is reachable and the user wants to inspect, tap, screenshot, or debug UI, use `maui-devflow-debug`.

## Workflow

1. Optionally run `maui devflow skills check` and update bundled skills before editing if it reports `update-available-from-current-cli`.
2. Find MAUI app projects in the workspace. Prefer app projects with `UseMaui`, platform TFMs such as `net*-android`/`net*-ios`/`net*-maccatalyst`/`net*-windows`, or GTK MAUI package references.
3. Determine whether each target project is standard MAUI, MAUI + Blazor WebView, GTK, or GTK + Blazor.
4. Add the correct DevFlow package references. Respect Central Package Management if `Directory.Packages.props` is present.
5. Register DevFlow in `MauiProgram.cs` inside `#if DEBUG`.
6. Build and run the app.
7. Verify with:

   ```bash
   maui devflow diagnose
   maui devflow wait
   maui devflow ui tree --depth 1
   ```

If verification fails after integration, switch to `maui-devflow-debug` for connectivity recovery.

## Stop Signals

- Stop before editing if multiple MAUI app projects exist and the user has not indicated which one to onboard.
- Stop and ask before enabling DevFlow outside Debug builds.
- Stop after package/registration edits and verify with a build before doing runtime debugging.

## Critical Anti-patterns

- Do not add versions to project `PackageReference` items when `Directory.Packages.props` is managing package versions.
- Do not add Blazor DevFlow packages unless the app uses Blazor WebView.
- Do not register DevFlow outside `#if DEBUG` unless the user explicitly asks.
- Do not use the old `builder.Services.AddMauiDevFlowAgent()` pattern; use `builder.AddMauiDevFlowAgent()`.

## Package Selection

| Project flavor | Required packages |
| --- | --- |
| Standard MAUI | `Microsoft.Maui.DevFlow.Agent` |
| MAUI + Blazor WebView | `Microsoft.Maui.DevFlow.Agent`, `Microsoft.Maui.DevFlow.Blazor` |
| GTK MAUI | `Microsoft.Maui.DevFlow.Agent.Gtk` |
| GTK MAUI + Blazor WebView | `Microsoft.Maui.DevFlow.Agent.Gtk`, `Microsoft.Maui.DevFlow.Blazor.Gtk` |

Blazor WebView indicators include a `Microsoft.AspNetCore.Components.WebView.Maui` package reference or `AddMauiBlazorWebView()` in `MauiProgram.cs`.

GTK indicators include package references such as `Maui.Gtk`, `Platform.Maui.Linux.Gtk4`, `GirCore.Gtk-4.0`, or `Platform.Maui.Linux.Gtk4.BlazorWebView`.

## Central Package Management

If the repo uses `Directory.Packages.props`, put versions there and leave project `PackageReference` entries versionless.

```xml
<!-- Directory.Packages.props -->
<PackageVersion Include="Microsoft.Maui.DevFlow.Agent" Version="<current-version>" />
```

```xml
<!-- App.csproj -->
<PackageReference Include="Microsoft.Maui.DevFlow.Agent" />
```

If the repo does not use Central Package Management, put the version on the `PackageReference`.

## MauiProgram.cs Registration

For standard MAUI:

```csharp
using Microsoft.Maui.DevFlow.Agent;

// inside CreateMauiApp(), before return builder.Build();
#if DEBUG
builder.AddMauiDevFlowAgent();
#endif
```

For MAUI + Blazor WebView:

```csharp
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.DevFlow.Blazor;

// inside CreateMauiApp(), before return builder.Build();
#if DEBUG
builder.AddMauiDevFlowAgent();
builder.AddMauiBlazorDevFlowTools();
#endif
```

For GTK, use the `.Gtk` namespaces and packages. GTK apps also need to start the agent after app activation, for example `app.StartDevFlowAgent()` in the platform app activation flow.

For Mac Catalyst, ensure the Debug entitlements allow the in-app HTTP server:

```xml
<key>com.apple.security.network.server</key>
<true/>
```

## Validation Checklist

- `MauiProgram.cs` registers DevFlow only in Debug builds.
- The app project references the package flavor that matches the target platform.
- Blazor DevFlow tools are added only when the app uses Blazor WebView.
- GTK apps start the DevFlow agent after app activation.
- Mac Catalyst Debug entitlements include `com.apple.security.network.server`.
- `dotnet build` succeeds.
- Important controls in the flows you plan to automate have stable `AutomationId`s.
- A running app appears in `maui devflow list`.
- `maui devflow agent status` returns the expected app name and platform.
- `maui devflow ui tree --depth 1` returns a visual tree.

## References

- See `references/package-selection.md` for package/flavor details.
- See `references/mauiprogram-registration.md` for registration patterns.
