# Microsoft.Maui.DevFlow.Agent.Native

In-app automation agent for **plain .NET Android, iOS, Mac Catalyst and macOS apps** — apps with no
reference to .NET MAUI at all.

It embeds a small HTTP server in your app and exposes the running UI over a JSON API: visual tree,
CSS-style queries, hit-testing, tap/fill/scroll/gesture, property get/set, screenshots, logs,
network capture and profiling. The `maui devflow` CLI and its MCP server drive that API, so AI
agents and test harnesses can see and operate your app.

> ⚠️ **Experimental** — APIs may change between releases. Not covered by the Microsoft Support Policy.

## Install

```xml
<PackageReference Include="Microsoft.Maui.DevFlow.Agent.Native" />
```

```bash
dotnet tool install -g Microsoft.Maui.Cli --prerelease
```

## Quick start

There is no host builder in a plain .NET app, so the agent is started explicitly. Nothing starts by
itself.

**Android** — `MainActivity.OnCreate`. The activity has to be bound, since it is the root the tree
is walked from:

```csharp
using Microsoft.Maui.DevFlow.Agent.Native;

protected override void OnCreate(Bundle? savedInstanceState)
{
    base.OnCreate(savedInstanceState);
    SetContentView(Resource.Layout.activity_main);
#if DEBUG
    this.StartDevFlowAgent();
#endif
}
```

In a multi-activity app, call `this.BindDevFlowAgent()` from `OnResume` so the tree follows the
foreground activity.

**iOS / Mac Catalyst** — `AppDelegate.FinishedLaunching`.
**macOS** — `AppDelegate.DidFinishLaunching`:

```csharp
using Microsoft.Maui.DevFlow.Agent.Native;

#if DEBUG
DevFlowAgent.Start();
#endif
```

Then drive it:

```bash
maui devflow tree
maui devflow screenshot --output shot.png
maui devflow tap --id submit-button
maui devflow mcp        # MCP server for AI agents
```

## Platform support

| Platform | UI framework | Roots walked |
|---|---|---|
| `net10.0-android` | `Android.Views` | activity decor view + dialogs |
| `net10.0-ios` | UIKit | connected scenes → `UIWindow` |
| `net10.0-maccatalyst` | UIKit | connected scenes → `UIWindow` |
| `net10.0-macos` | AppKit | application windows |

Elements are identified by whatever the platform already offers: `View.Tag` then the resource-entry
name on Android, `AccessibilityIdentifier` on UIKit, `NSView.Identifier` on AppKit. Give your views
those and the same automation ids work across all four.

## What is and is not supported

Supported everywhere: visual tree, query and CSS selectors, hit-testing, tap, fill, clear, focus,
key, gesture, scroll, batch, property get/set, screenshots (page, element, full screen), logs,
network capture, profiling, DevFlow actions, and extensions.

Endpoints with no meaning outside MAUI return `501` with
`{ "error": "not_supported", "capability": …, "reason": … }` instead of failing opaquely, and
`/api/v1/agent/capabilities` reports them as `supported: false` before you call them:

| Capability | Native | Notes |
|---|---|---|
| App theme | ❌ | MAUI reads `Application.RequestedTheme`; there is no equivalent outside Controls |
| Shell navigation | ❌ | Shell is a MAUI Controls concept |
| Preferences, secure storage | ⚙️ | add `Microsoft.Maui.DevFlow.Agent.Native.Essentials` |
| Device, display, battery, connectivity | ⚙️ | add `Microsoft.Maui.DevFlow.Agent.Native.Essentials` |
| Permissions, geolocation, sensors | ⚙️ | add `Microsoft.Maui.DevFlow.Agent.Native.Essentials` |

Clients can branch on `/api/v1/agent/status`, which reports `framework` (`maui` | `native`) and
`uiFramework` (`android-views` | `uikit` | `appkit` | `maui-controls` | `gtk` | `wpf`).

## Related packages

| Package | Description |
|---|---|
| `Microsoft.Maui.DevFlow.Agent.Native.Essentials` | Optional add-on for device, storage and sensor endpoints |
| `Microsoft.Maui.DevFlow.Agent` | The equivalent agent for .NET MAUI apps |
| `Microsoft.Maui.DevFlow.Driver` | Client driver library for tests |
| `Microsoft.Maui.Cli` | The `maui devflow` CLI and MCP server |

## Links

- [Source and docs](https://github.com/dotnet/maui-labs/tree/main/src/DevFlow)
- [Native samples](https://github.com/dotnet/maui-labs/tree/main/samples/DevFlow.Sample.Native)
- [HTTP/WebSocket protocol spec](https://github.com/dotnet/maui-labs/tree/main/docs/DevFlow/spec)
