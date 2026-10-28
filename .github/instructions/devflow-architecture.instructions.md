---
applyTo: "src/DevFlow/**"
---

# DevFlow Architecture

## Communication Model

DevFlow uses a three-tier architecture:

```
┌──────────────┐                ┌──────────────┐                ┌──────────────────────────┐
│  CLI / MCP   │  HTTP (direct) │    Broker     │  WebSocket     │  Agent (in-app)          │
│  (maui devflow)│ ◄──────────► │  (port 19223) │ ◄───────────── │  (dynamic port)          │
└──────────────┘  (after port   └──────────────┘  (registration) └──────────────────────────┘
      │            discovery)          │                                │
      │ HTTP (direct, after discovery) │                                │ Platform APIs
      └────────────────────────────────┼────────────────────────────────┘
      ▲                                                               
      │ MCP (stdio)                                                   
      ▼                                                               
┌──────────────┐                                              
│  AI Agent    │                                              
│  (Copilot,   │                                              
│   Claude,    │                                              
│   etc.)      │                                              
└──────────────┘
```

1. **Agent** runs inside the app process (added via NuGet package — MAUI or plain .NET). Exposes HTTP API on a dynamic port. Registers with the Broker over WebSocket. Has direct access to the visual tree, pages, platform views.
2. **Broker** runs on the developer machine (port 19223). Agents register with it via WebSocket. CLI discovers agent ports through the broker's HTTP API.
3. **CLI** (`maui devflow`) discovers agents via the broker, then communicates **directly** with agents over HTTP. Also hosts the MCP server for AI agent integration.

## Package Dependency Graph

```
Microsoft.Maui.DevFlow.CLI (global tool)
├── Microsoft.Maui.DevFlow.Driver (platform drivers)
│   └── Microsoft.Maui.DevFlow.Client (AgentClient — public protocol API)
├── ModelContextProtocol (MCP server)
├── System.CommandLine (CLI framework)
├── Spectre.Console (terminal UI)
└── Websocket.Client (broker transport)

Microsoft.Maui.DevFlow.Client (netstandard2.0 + modern .NET)
├── AgentClient, ElementInfo, protocol DTOs, HTTP/JSON operations
└── System.Text.Json

Microsoft.Maui.DevFlow.Driver (modern .NET, platform-specific)
├── Microsoft.Maui.DevFlow.Client
├── App process management, UI Automation, Mac accessibility
└── SkiaSharp (screenshot processing)

Microsoft.Maui.DevFlow.Agent.Abstractions (no MAUI dependency)
├── AgentHttpServer, AgentOptions, routing, DevFlowAgentService
├── ElementInfo + DTOs, Css/, Network/, Profiling/, extensions
├── Fizzler (CSS selector parsing)
└── SkiaSharp (screenshot capture/resize)

Microsoft.Maui.DevFlow.Agent.Core (MAUI UI backend)
└── Microsoft.Maui.DevFlow.Agent.Abstractions

Microsoft.Maui.DevFlow.Agent (NuGet package for MAUI app developers)
├── Microsoft.Maui.DevFlow.Agent.Core
└── Microsoft.Maui.DevFlow.Blazor (optional — CDP bridge for Blazor WebViews)

Microsoft.Maui.DevFlow.Agent.Gtk (NuGet package for GTK/Linux apps)
├── Microsoft.Maui.DevFlow.Agent.Core
└── Microsoft.Maui.DevFlow.Blazor.Gtk (optional — WebKitGTK CDP)

Microsoft.Maui.DevFlow.Agent.WPF (NuGet package for WPF apps)
└── Microsoft.Maui.DevFlow.Agent.Core

Microsoft.Maui.DevFlow.Agent.Native (plain .NET android/ios/maccatalyst/macos — no MAUI)
├── Microsoft.Maui.DevFlow.Agent.Abstractions
└── NativeUi.{Android,UIKit,AppKit}, NativeDevFlowAgentService, DevFlowAgent.Start()

Microsoft.Maui.DevFlow.Agent.Native.Essentials (optional add-on)
└── Microsoft.Maui.DevFlow.Agent.Native + Microsoft.Maui.Essentials

Microsoft.Maui.DevFlow.Logging (standalone — no MAUI dependency)
```

## The Abstractions / backend split

`DevFlowAgentService` lives in **`Agent.Abstractions`** and owns routing, orchestration policy
(retries, waits, batch sequencing, error shaping, CSS filtering over `ElementInfo`) and every
handler that needs no UI framework. Handlers that do need one are `protected virtual` and return a
uniform `501` `{ error: "not_supported", capability, reason }` envelope by default. Backends
override them:

- `MauiDevFlowAgentService` (in `Agent.Core`) — MAUI Controls
- `NativeDevFlowAgentService` (in `Agent.Native`) — Android views, UIKit, AppKit
- `EssentialsNativeDevFlowAgentService` (in `Agent.Native.Essentials`) — adds Essentials endpoints

This is a virtual-method seam rather than an `IUiBackend` interface because partial classes cannot
span assemblies, and the existing `protected virtual` hooks that `Agent`, `Agent.Gtk` and
`Agent.WPF` override had to keep working verbatim.

**Rules when adding to this area:**

- Anything framework-neutral belongs in `Agent.Abstractions`. Do not add a `Microsoft.Maui.*`
  reference to it or to `Agent.Native` — `MauiFreeAssemblyTests` reads the PE metadata and fails
  the build if you do.
- Essentials-backed handlers go in `src/DevFlow/Shared.Essentials/EssentialsAgentSupport.cs`, which
  is compiled by **both** `Agent.Core` and `Agent.Native.Essentials`. Never fork them; the whole
  point is that the two agents cannot drift.
- Report support honestly. Add the capability to `HandleCapabilities` with `supported: false` and a
  reason rather than letting an endpoint fail obscurely.
- `agent.framework` (`maui` | `native`) and `agent.uiFramework`
  (`maui-controls` | `android-views` | `uikit` | `appkit` | `gtk` | `wpf`) are how clients branch.

Assembly moves out of `Agent.Core` are **binary breaking**. Namespaces were deliberately left alone
so source stays compatible. Label such PRs `breaking-change` and say so in the description.

## Key Extension Points

### Adding a New HTTP Endpoint

1. Add route in `Agent.Abstractions/DevFlowAgentService.Handlers.cs` → `RegisterRoutes()`:
   ```csharp
   _server.MapGet("/api/v1/myfeature", HandleMyFeature);
   ```
2. Implement the handler in `Agent.Abstractions` if it is framework-neutral. If it needs a UI
   framework, declare it virtual there returning `NotSupported(...)`, then override it in
   `Agent.Core/MauiDevFlowAgentService.cs` and `Agent.Native/NativeDevFlowAgentService.cs`:
   ```csharp
   protected virtual async Task<HttpResponse> HandleMyFeature(HttpRequest request) { ... }
   ```
3. Add DTO class at the bottom of the owning file if needed.
4. Add client method in `Client/AgentClient.cs`.
5. Update the canonical protocol contract in `docs/DevFlow/spec/openapi.yaml`,
   including exact query parameters, request/response schemas, status codes, and
   examples. Update `asyncapi.yaml` for any `/ws/v1/*` channel. Route and
   protocol changes must be made in the same PR.
6. Run `ProtocolSpecTests`; registered routes must be represented in OpenAPI or
   AsyncAPI.
7. Optionally expose as MCP tool and/or CLI command

### Adding a New MCP Tool

See `mcp-tools.instructions.md`.

### Adding Platform-Specific Behavior

Override virtual methods from `Agent.Abstractions/DevFlowAgentService.cs`:

- In `Agent/DevFlowAgentService.cs` with `#if` directives for iOS/Android/macOS/Windows
- In `Agent.Gtk/GtkAgentService.cs` for Linux/GTK
- In `Agent.WPF/WpfAgentService.cs` for WPF
- In `Agent.Native/NativeUi.{Android,UIKit,AppKit}.cs` for plain .NET apps
- Always call `await DispatchAsync(() => ...)` to run on the UI thread

## Visual Tree and Element Resolution

- `VisualTreeWalker` (MAUI) recursively walks MAUI's `IVisualTreeElement` hierarchy;
  `NativeElementRegistry` + `NativeUi` do the same over `ViewGroup`/`UIView`/`NSView`
- Each element gets a unique ID (ephemeral, regenerated on tree changes)
- Elements are resolved by: element ID, AutomationId, type, text content, CSS selector
- Native automation ids come from `View.Tag` then the resource-entry name (Android),
  `AccessibilityIdentifier` (UIKit), `NSView.Identifier` (AppKit)
- MAUI normalises types to `Button`/`Label`/`Entry`; native reports the real platform type
  (`UIButton`, `NSButton`, …), which is why integration tests use
  `IntegrationTestBase.ButtonTypeName`
- `ElementInfo` captures: Id, Type, AutomationId, Text, IsVisible, IsEnabled, Bounds, WindowBounds
- CSS selectors (Fizzler) work in Blazor WebViews via CDP

## Gesture Injection

`POST /api/v1/ui/actions/gesture` supports `tap`, `doubletap`, `longpress`, `swipe`, `pan`,
`pinch` and `rotate`. Resolution is two-tier, and the response reports which tier ran via
`handledBy` (`recognizer` | `native` | `action` | `scroll` | `none`) plus a `detail` string:

1. **Managed recognizer** (`Agent.Core`, all platforms) — walks the target element and its
   ancestors for a matching MAUI gesture recognizer and drives it through MAUI's public
   controller interfaces: `IPinchGestureController`, `IPanGestureController`,
   `ISwipeGestureController`. Only `TapGestureRecognizer.SendTapped` needs reflection
   (`TryInvokeTapped`), because it is `internal`. Continuous gestures are emitted as N
   interpolated steps — `SendPinch` in particular takes a *delta* scale per event.
2. **Native injection** (`Agent/PlatformAgentService.Gestures.cs`) — the fallback when no
   recognizer exists, which is the case for Map, WebView and other controls that handle
   gestures internally. Override the `TryNative{Pinch,Rotate,Pan,Swipe,LongPress,DoubleTap}`
   virtuals; return a description string when handled, `null` when not.
   - **Android** — real multi-pointer `MotionEvent`s dispatched to the named element's native
     view (or its MAUI container), with start points constrained to its visible bounds. The
     activity's window hit-test and ancestor interception are intentionally bypassed so another
     view cannot receive a gesture reported against the target.
   - **iOS / Mac Catalyst** — MKMapView camera distance/centre → UIScrollView zoom/offset →
     driving an attached `UIGestureRecognizer` via `setState:`. When `AgentOptions.EnableSyntheticTouch`
     is enabled, MAUI's `GraphicsView` and explicitly registered raw-touch view types can also
     receive synthesised `UITouch` sequences. This opt-in uses private UIKit ivars; ordinary
     controls, scroll views, and recognizer-backed views are excluded. The status capability
     `syntheticTouch` reports whether the tier is enabled on that backend. `SKCanvasView` is not
     eligible for these gestures because its touch handling is recognizer-backed; a tap at a
     point reaches it (below).
     Native swipes try target/descendant swipe recognizers, then target-local pan/raw-touch
     handling, and only then ancestor swipe and pan recognizers.
   - **Windows** — `ScrollViewer.ChangeView`. Input injection needs the restricted
     `inputInjectionBrokered` capability and is unusable from a normal app package.
   - **macOS AppKit** — `NSScrollView` magnification and content offset.
   - **GTK** — tier 1 only.

**Tap at a point.** `POST /api/v1/ui/actions/tap` with `x`/`y` (device-independent units from the
element's top-left) delivers a real touch at that point instead of invoking the element. It exists
for content a canvas draws and hit-tests itself — SkiaSharp's `SKCanvasView`, `GraphicsView`,
Syncfusion's charts and other Microsoft.Maui.Graphics controls — which has no element ID. Core
(`HandlePointTapAsync`) validates the point against the element's bounds and never falls back to
the element tap; platforms override `TryNativePointTapAsync`.
   - **Android** — a `MotionEvent` down/up at the point, sent to the element's view like the
     gestures above; the point must be in the view's visible rect, and not covered
     (`FindTouchBlocker`, shared with point drags). Android offers a down to the topmost view
     first, and whether a view keeps it cannot be read back in general — an `onTouchEvent`
     override or an `OnTouchListener` (SkiaSharp's canvas, MAUI's gesture recognizers, one the app
     attached natively) can keep it — so the check fails closed: a view drawn over the target lets
     the touch through only when it is not clickable, its exact type is one of MAUI's plain
     platform views (layouts, `ContentViewGroup`, `WrapperView`, Label, Image, BoxView) or the
     framework base they use, no MAUI gesture recognizer is attached to it, and it has no touch
     listener (read from the private `View.mListenerInfo`; unreadable counts as having one).
     Anything else over the point refuses the touch.
   - **iOS / Mac Catalyst** — requires `EnableSyntheticTouch`. The element is scrolled into view
     through its enclosing scroll views, then the window hit-tests the point,
     which must land on the element or inside it (controls and scroll views are refused — tap them
     by ID). The synthesised `UITouch` is then fed straight to the gesture recognizers on the hit
     view and its ancestors up to the element (`AppleTouchInjector.DriveRecognizersAsync`), after
     asking each delegate's `gestureRecognizer:shouldReceiveTouch:`; a recognizer-less view the
     raw-touch policy accepts gets it through `touchesBegan:` as for gestures. Recognizers a lone
     one-finger tap cannot complete (multi-tap, multi-touch, pinch, rotation) are skipped: UIKit
     fails them on a timer its gesture environment only runs for real touches, so fed this tap
     they would hold it and turn the next tap into a stale double tap. `setState:` and `reset`
     do not clear that state.

**Drag from a point.** `POST /api/v1/ui/actions/gesture` with type `drag` (or `pan`) and `x`/`y`
is a real touch rather than a recognizer call: pressed at the point, held `holdMs`, moved to
`toX`/`toY` (on `toElementId` when given, else on the element, where it may run past the edge) or by
`deltaX`/`deltaY`, and released. `toElementId` alone ends at the target's centre; it must be in the
same capture as `elementId` (batch and single requests both validate it). Core
(`PerformPointDragAsync`, planning in `SyntheticTouchGeometry.TryPlanPointDrag`) checks the start
against the element's bounds and the end against a drop target's; it never falls back to
`IPanGestureController`, which would pass for input no finger produced. Platforms override
`TryNativePointDragAsync`.
   - **Android** — `MotionEvent`s sent to the window's root view at screen positions, not to the
     element's view: the moves cross other views, and an enclosing scroller intercepting the drag
     (cancelling the element's pan) is exactly what a finger would see. The start must be in the
     element's visible rect, a drop target's end in the target's.
   - **iOS / Mac Catalyst** — requires `EnableSyntheticTouch`; the element is first scrolled into
     view (as for point taps). The hit view's recognizers up to the element are driven with
     `DriveRecognizersAsync` along the path. UIKit applies a recognizer's state changes after the
     first through its gesture environment, which runs only for real touches, so the injector does
     that work between moves (`Arbitrate`): a begun pan is moved to Changed (sending its action),
     a tap fails once the touch travels past its allowable movement (45pt, read from its private
     getter), and once a gesture begins every waiting recognizer fails unless a delegate allows
     simultaneous recognition. A losing recognizer gets `touchesCancelled:` and nothing more —
     `setState:Failed` mid-touch is deferred by UIKit, and the tap would still fire at touch end.
     The touch's `_preciseLocationInWindow` is moved with `_locationInWindow`. MAUI's pan
     translation starts after UIKit's 10pt slop, as it does for a finger.
     Only the element's own recognizers are driven, so a drag a recognizer outside them would
     contest is refused before any touch is sent (`FindCompetingRecognizer`): an enclosing scroll
     view that scrolls along an axis the drag travels more than 10pt on, or — above the element —
     a pan, a swipe in a direction the drag travels, a long press the hold outlasts, or an unknown
     recognizer, after its delegate's `shouldReceiveTouch:` (Shell's flyout pan declines touches
     away from the leading edge). Taps, hover, screen-edge and multi-finger recognizers, and a
     scroll view's private machinery, are not contenders. Modelling UIKit's arbitration with those
     recognizers is future work; until then the refusal says which recognizer was in the way.
   - MAUI's `DragGestureRecognizer`/`DropGestureRecognizer` are out of scope: the platforms run
     them as system drag sessions (`UIDragInteraction`, `View.startDragAndDrop`) that this tier
     does not feed, and MAUI exposes only `DropGestureRecognizer.SendDragOver` publicly.
     Pan-based drag and drop, which is what control libraries ship, is covered.

Gesture directions describe finger travel, not content travel. An `up` swipe moves the pointer
upward; a scroll fallback inverts that vector so scrollable content moves downward consistently
with native touch injection. Named gestures target MAUI `VisualElement` IDs (or the current page
for non-tap gestures; tap requires an element ID);
registered native-only element IDs remain supported by the dedicated tap action, but are not
targets for multi-pointer gesture synthesis.

Gestures against `samples/DevFlow.Sample` → `GestureTestPage` (`//gestures`) write what they
received to `AutomationId`'d status labels, so tests assert the gesture actually reached the app.

## Screenshot Capture Flow

1. **Default** (no params): captures `window.Page` via `VisualDiagnostics.CaptureAsPngAsync` — page content only
2. **Element** (`--id` or `--selector`): captures specific element bounds
3. **Fullscreen** (`--fullscreen`): platform-specific composited capture including status bar and safe areas
4. **iOS CLI fallback**: `simctl io screenshot` for full simulator display
5. All screenshots auto-scale to 1x logical resolution (configurable via `--scale native`)
