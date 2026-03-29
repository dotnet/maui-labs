# Setup & Installation

Complete guide for integrating MauiDevFlow into a .NET MAUI app.

## Table of Contents
- [Install CLI Tools](#1-install-cli-tools)
- [Add NuGet Packages](#2-add-nuget-packages)
- [Register in MauiProgram.cs](#3-register-in-mauiprogramcs)
- [Port Configuration](#3b-port-configuration)
- [Blazor Hybrid Setup](#4-blazor-hybrid-chobitsu-auto-injection)
- [Mac Catalyst Entitlements](#5-mac-catalyst-entitlements)
- [Android Port Forwarding](#6-android-port-forwarding)
- [Verify Setup](#7-verify-setup)
- [Checking for Updates](#checking-for-updates)

## 1. Install CLI Tools

```bash
dotnet tool install --global Redth.MauiDevFlow.CLI    # maui-devflow
dotnet tool install --global androidsdk.tool           # android (Android only)
dotnet tool install --global appledev.tools            # apple (iOS/Mac only)
```

Verify: `maui-devflow --version`

## 2. Add NuGet Packages

First, determine whether the project is a standard MAUI app or a Linux/GTK app.
There is no `-linux` TFM — Linux/GTK apps target plain `net10.0` (or `net9.0`) and use
the community `Maui.Gtk` package. Detect this by checking the `.csproj`:

```bash
# Check for GTK indicators in the project
grep -i 'GirCore\|Maui\.Gtk\|Gtk-4\.0' *.csproj Directory.Build.props 2>/dev/null
```

If GTK/GirCore references are found, use the **Linux/GTK packages** below.
Otherwise, use the **standard MAUI packages**.

### Standard MAUI Apps (iOS, Android, Mac Catalyst, Windows, macOS)

Add to your MAUI app's `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Redth.MauiDevFlow.Agent" Version="*" />
  <!-- Blazor Hybrid apps also need: -->
  <PackageReference Include="Redth.MauiDevFlow.Blazor" Version="*" />
</ItemGroup>
```

- `Redth.MauiDevFlow.Agent` — Required for all MAUI apps (iOS, Android, Mac Catalyst, Windows, macOS AppKit). Provides the in-app agent
  for visual tree inspection, screenshots, tapping, filling text, etc.
- `Redth.MauiDevFlow.Blazor` — Required for Blazor Hybrid apps. Provides the CDP bridge
  for DOM inspection, JavaScript evaluation, and Blazor debugging.

**macOS (AppKit) apps** also need the `Platform.Maui.MacOS` packages — see [references/macos.md](macos.md)
for the full project setup including entry point, builder configuration, and BlazorWebView differences.

### Linux/GTK Apps

Linux/GTK apps (using Maui.Gtk) use separate packages:

```xml
<ItemGroup>
  <PackageReference Include="Redth.MauiDevFlow.Agent.Gtk" Version="*" />
  <!-- Blazor Hybrid apps also need: -->
  <PackageReference Include="Redth.MauiDevFlow.Blazor.Gtk" Version="*" />
</ItemGroup>
```

- `Redth.MauiDevFlow.Agent.Gtk` — Agent for Linux/GTK apps. Uses GirCore.Gtk-4.0 for native GTK integration.
- `Redth.MauiDevFlow.Blazor.Gtk` — CDP bridge for WebKitGTK-based BlazorWebView on Linux.

## 3. Register in MauiProgram.cs

```csharp
using MauiDevFlow.Agent;
using MauiDevFlow.Blazor;  // Blazor Hybrid only

var builder = MauiApp.CreateBuilder();
// ... your existing setup ...

#if DEBUG
builder.Services.AddBlazorWebViewDeveloperTools();          // Blazor Hybrid only
builder.AddMauiDevFlowAgent();
builder.AddMauiBlazorDevFlowTools(); // Blazor Hybrid only
#endif
```

### Linux/GTK Registration

For Linux/GTK apps, use the GTK-specific namespaces and add the agent startup call:

```csharp
using MauiDevFlow.Agent.Gtk;
using MauiDevFlow.Blazor.Gtk;  // Blazor Hybrid only

var builder = MauiApp.CreateBuilder();
// ... your existing setup ...

#if DEBUG
builder.AddMauiDevFlowAgent();
builder.AddMauiBlazorDevFlowTools(); // Blazor Hybrid only
#endif
```

After the MAUI app is activated (e.g., in `OnActivate` or after `Application.Current` is available):

```csharp
#if DEBUG
app.StartDevFlowAgent();
// For Blazor, wire CDP to agent:
var blazorService = app.Handler?.MauiContext?.Services.GetService<GtkBlazorWebViewDebugService>();
blazorService?.WireBlazorCdpToAgent();
#endif
```

**Agent options:**
- `Port` — HTTP port for the agent REST API (default: 9223). Also configurable via `.mauidevflow` or `-p:MauiDevFlowPort=XXXX`.
- `Enabled` — Enable/disable the agent (default: true)
- `MaxTreeDepth` — Max depth for visual tree queries, 0 = unlimited (default: 0)

## 3b. Port Configuration

**Automatic (via broker):** The CLI includes a broker daemon that automatically assigns
ports to agents. No manual port configuration is needed — the broker handles it. The CLI
auto-starts the broker on first use. See the main SKILL.md for details on the broker.

**Manual fallback (.mauidevflow):** If the broker isn't available, create a `.mauidevflow`
file in the project directory to set an explicit port:

```json
{
  "port": 9347
}
```

Both the MSBuild targets and the CLI read this file automatically:
- **Build**: `dotnet build -t:Run` — agent starts on the configured port
- **CLI**: `maui-devflow MAUI status` — connects to the configured port (when run from project dir)

**Port priority:** Explicit `--agent-port` > Broker discovery > `.mauidevflow` > default 9223.

**How port discovery works:** When you run any `MAUI` or `cdp` command, the CLI:
1. Auto-starts the broker if not running
2. Queries the broker for agents matching the current project (`.csproj` in cwd)
3. If one agent matches → uses its port automatically
4. If multiple match → prints a disambiguation table to stderr
5. Falls back to `.mauidevflow` config file → default 9223

**Multiple apps simultaneously:** The broker assigns unique ports from range 10223–10899.
Use `maui-devflow list` to see all agents, then target a specific one:
```bash
maui-devflow MAUI status --agent-port 10224    # target specific agent
```

**Blazor options:**
- `Enabled` — Enable/disable CDP support (default: true)
- `EnableWebViewInspection` — Enable WebView inspection (default: true)
- `EnableLogging` — Log debug messages (default: true in DEBUG)

## 4. Blazor Hybrid: Chobitsu Auto-Injection

**No manual setup needed for Blazor Hybrid apps.** The `Redth.MauiDevFlow.Blazor` NuGet package
automatically injects `chobitsu.js` (the CDP implementation) via a Blazor JS initializer.
Just add the NuGet package and register in `MauiProgram.cs` — that's it.

### Fallback: Manual Script Tag

If auto-injection doesn't work in your setup (e.g., older .NET versions), add this line
before `</body>` in `wwwroot/index.html`:

```html
<script src="chobitsu.js"></script>
```

The library detects both approaches — manual script tags take priority over auto-injection.

### What if it's not working?

The library checks at runtime and logs a message:
```
[BlazorDevFlow] ⚠️ No chobitsu script tag found. Auto-injection via JS initializer may not have run.
```

### How the file gets there

The `chobitsu.js` file is included in the NuGet package as a static web asset. It is
automatically available at the root of your app's `wwwroot/` — no `.targets` file copying,
no manual downloads. It works in both Debug and Release builds (though MauiDevFlow itself
should only be referenced in Debug configurations).

## 5. Mac Catalyst: Entitlements

Mac Catalyst apps need the `com.apple.security.network.server` entitlement to allow the
agent and CDP servers to bind ports. Without this, the app will crash or fail silently.

### Option A: Sandbox disabled (simpler for development)

Create or update `Platforms/MacCatalyst/Entitlements.plist` for Debug builds:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>com.apple.security.app-sandbox</key>
    <false/>
    <key>com.apple.security.network.client</key>
    <true/>
  </dict>
</plist>
```

### Option B: Sandbox enabled (required for App Store)

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>com.apple.security.app-sandbox</key>
    <true/>
    <key>com.apple.security.network.client</key>
    <true/>
    <key>com.apple.security.network.server</key>
    <true/>
  </dict>
</plist>
```

Reference in your `.csproj` (Debug only, so Release uses the default entitlements):

```xml
<PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)'))
    == 'maccatalyst' and '$(Configuration)' == 'Debug'">
  <CodeSignEntitlements>Platforms/MacCatalyst/Entitlements.Debug.plist</CodeSignEntitlements>
</PropertyGroup>
```

**Avoiding TCC permission dialogs:** Even with sandbox disabled, macOS prompts for access to
`~/Documents`, `~/Downloads`, `~/Desktop`, and dotfiles in `~/` on every rebuild (ad-hoc signing
changes the code signature each build). To avoid this, store app data in
`Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` (`~/Library/Application Support/`)
instead of the home directory root. This path is not TCC-protected.

## 6. Android: Port Forwarding

After deploying to an Android emulator, set up port forwarding for the broker and agent:

```bash
adb reverse tcp:19223 tcp:19223  # Broker (lets agent register with host broker)
adb forward tcp:<port> tcp:<port> # Agent (lets CLI reach agent — get port from `maui-devflow list`)
```

The broker reverse (`tcp:19223`) is needed so the agent inside the emulator can connect to
the host's broker daemon. Set this up once per emulator session.

The agent forward uses the port shown in `maui-devflow list` after the agent registers
(range 10223–10899).

**Fallback (no broker):** If using direct mode with a `.mauidevflow` config file:
```bash
adb reverse tcp:9223 tcp:9223    # Direct agent port (single port for Agent + CDP)
```

## 7. Verify Setup

After building and running the app:

```bash
maui-devflow list                 # Should show registered agents (via broker)
maui-devflow MAUI status          # Should show agent info, platform, app name
maui-devflow cdp status           # Should show "Connected" (Blazor Hybrid only)
```

If status commands fail:
- **Broker not running?** `maui-devflow broker status` — CLI auto-starts the broker, but check if it's healthy
- **Agent not registered?** `maui-devflow list` — wait a few seconds for the agent to register
- **Mac Catalyst:** Check entitlements (Step 5)
- **macOS (AppKit):** Ensure `AddMacOSEssentials()` is called — see [references/macos.md](macos.md)
- **Android:** Check port forwarding (Step 6) — need both `adb reverse tcp:19223` and `adb forward tcp:<port>`
- **iOS Simulator:** Should work without extra config
- **Linux/GTK:** Should work without extra config — runs directly on localhost
- **All platforms:** Ensure the app is running and the `#if DEBUG` block is active
- **Port conflict:** Check if another process holds the port: `lsof -i :9223` (or your configured port)
- **Wrong port:** Use `maui-devflow list` to find the assigned port, or ensure CLI is run from the project directory

## Quick Checklist

For an AI agent setting up MauiDevFlow in a new project:

1. [ ] `Redth.MauiDevFlow.Agent` NuGet package added (or `Redth.MauiDevFlow.Agent.Gtk` for Linux)
2. [ ] `Redth.MauiDevFlow.Blazor` NuGet package added (Blazor Hybrid only; or `Redth.MauiDevFlow.Blazor.Gtk` for Linux)
3. [ ] `builder.AddMauiDevFlowAgent(...)` in MauiProgram.cs inside `#if DEBUG`
4. [ ] `builder.AddMauiBlazorDevFlowTools(...)` in MauiProgram.cs (Blazor Hybrid only)
5. [ ] Chobitsu auto-injected via JS initializer (Blazor Hybrid — no manual step needed)
6. [ ] Mac Catalyst entitlements include `network.server` (Mac Catalyst only)
7. [ ] `adb reverse tcp:19223` for broker + `adb forward tcp:<port>` for agent (Android only)
8. [ ] Linux/GTK: `app.StartDevFlowAgent()` called after app activation
9. [ ] macOS (AppKit): `UseMauiAppMacOS()`, `AddMacOSEssentials()`, `MacOSBlazorWebView` — see [macos.md](macos.md)
10. [ ] Verify with `maui-devflow list` and `maui-devflow MAUI status`

## Checking for Updates

At the start of each session (or periodically), check whether the CLI, skill, and NuGet
packages are up to date. Outdated components can cause confusing failures or missing features.

### Check CLI version
```bash
maui-devflow --version
dotnet tool search Redth.MauiDevFlow.CLI | head -5
```

If a newer version is available:
```bash
dotnet tool update --global Redth.MauiDevFlow.CLI
```

### Update the skill
```bash
# Check if the skill is up to date (compares local commit SHA against remote)
maui-devflow skill-version

# Download the latest skill files from GitHub
maui-devflow update-skill
```

The `update-skill` command writes a `.skill-version` file tracking the installed commit SHA.
The `skill-version` command reads this file and compares against the latest remote commit.

**AI agents should check at session start:** If `.claude/skills/maui-ai-debugging/.skill-version`
exists, run `maui-devflow skill-version` to see if an update is available. If the remote SHA
differs from the installed SHA, ask the user if they'd like to update before proceeding.

### Check NuGet packages in the project
```bash
grep -i 'Redth.MauiDevFlow' *.csproj Directory.Build.props Directory.Packages.props 2>/dev/null
```

If packages are outdated:
```bash
dotnet add package Redth.MauiDevFlow.Agent
dotnet add package Redth.MauiDevFlow.Blazor    # only if Blazor Hybrid
# For Linux/GTK: use .Gtk variants instead
```

### Re-run setup verification
After any updates, walk through the checklist above to ensure everything is still properly
configured. A CLI update may introduce new setup requirements.
