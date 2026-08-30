# The device layer

> **Experimental.** The device layer, the Mobile Canvas companion, and the standalone Mobile Device
> Canvas are preview surfaces. They are versioned and pinned, but their contracts may change, and
> nothing else in DevFlow depends on them.

DevFlow observes a running app from **inside** it: the in-app agent walks the MAUI visual tree,
reads properties, and drives semantic input. That vantage point is precise but bounded — everything
it knows arrives through a process living inside the app, so it goes blind whenever the app is not
both running and frontmost:

- the app **crashed**, and the agent died with it
- a **system dialog** (permissions, share sheet, photo picker) is on top and is not in the MAUI tree
- the **soft keyboard** is covering a control, invisibly to layout diagnostics
- the app **has not launched yet**, so there is nothing to attach to

The device layer is a second, independent vantage point that survives all four. It observes and
controls the device *around* the app: lifecycle, live frames, physical input, and environment.

> The device layer is **optional**. On a machine with no device host, and for every desktop MAUI
> app, it reports every capability as unavailable and DevFlow behaves exactly as it did before.
> That is a guarantee, not a best effort — see [Degradation](#degradation).

### Layout system evidence

`POST /api/layout-diagnostics/composite` on the broker pairs one agent layout report with a Mobile
Canvas UI snapshot: bounded system/accessibility element identity, role, package, interaction state,
device-point bounds, foreground owner, keyboard visibility, orientation and scale metadata, and an
optional screenshot digest. Raw screenshot pixels are never embedded in a layout report.

The correlation is only claimed when it can be defended. Evidence is `complete` only when an
immediate second app scan reports the same tree revision, the connected agent instance is unchanged,
the hierarchy came from the paired device in the orientation and scale the pairing recorded, and the
two captures are within the allowed skew. Anything else is `incomplete`, carries no elements, and
names the exact drift in `limitations`. With no device host, no exact pairing, or no hierarchy the
status is `unavailable` and the report is otherwise untouched.

The route reports; it never rewrites. It adds no finding, changes no summary counter, and does not
recompute the agent's diagnostics revision — the agent holds the reviewed suppression policy for the
scan, so a finding invented here could not be suppressed by it. Accessibility bounds are device-level
geometry rather than compositor paint regions, so visual occlusion remains unproven either way.

## Contracts

| Contract | Document |
|---|---|
| Device surface | [`schemas/device-surface-v1.json`](spec/schemas/device-surface-v1.json) |

> **Not to be confused with [`schemas/device.json`](spec/schemas/device.json).** That contract is
> the device as the *app* sees it — `DeviceInfo`, battery, connectivity, sensors, permissions — read
> through the in-app agent and surfaced in the Inspector's Device data tab. This contract is the
> device as the *host* sees it: lifecycle, display geometry, and control. They describe the same
> physical device from the two vantage points, and are deliberately separate documents because
> they have different owners, different availability, and different trust.

## Pairing an app to its device

Without pairing, a user picks an app and a device separately and nothing connects them — the device
layer would be a second tool rather than part of DevFlow. Pairing is what makes one selection mean
both.

A running app reports what it can observe about its own host in the broker registration's
`deviceId` field, in a compact wire form:

```
platform=ios;udid=1E2D3C4B-5A69
platform=android;serial=emulator-5554;avd=Pixel_8_API_35
```

| Platform | Signal | Strength |
|---|---|---|
| iOS / Mac Catalyst simulator | `SIMULATOR_UDID`, injected into the process environment by the simulator runtime | **Exact** — it is the UDID `simctl` uses |
| Android emulator | `ro.boot.qemu.avd_name` | **Primary** — the name `avdmanager` knows |
| Android emulator | `ro.serialno` | **Secondary** — *not* the adb serial; adb addresses an emulator as `emulator-<consolePort>`, derived from the console port, so these agree only on some configurations |
| Windows / macOS desktop | none | No virtual device exists; nothing pairs |

The broker joins that identity against the devices the device layer discovered. Two rules keep a
wrong pairing from being worse than no pairing:

1. **Exact beats weak.** A serial or UDID match always wins over a name match.
2. **Ambiguity refuses.** If two devices match equally well, the result is *no pairing*. Silently
   choosing the wrong device would apply every subsequent coordinate to the wrong screen — a
   failure that looks plausible rather than raising anything.

Resolution is best-effort throughout: a platform API that throws, or a host we do not recognise,
degrades to no pairing and never prevents the app from registering.

## Coordinate spaces

Drawing the MAUI tree overlay on a device frame crosses four coordinate spaces. Every boundary is a
place to be silently and plausibly wrong, so the whole chain lives in one type,
`DeviceCoordinateSpace`, rather than being recomputed by each caller.

```
frame pixels          what the client decoded from the video stream or screenshot
  ÷ streamScale       the client asks the encoder to scale down to its panel size
device pixels         DisplayGeometry.pixelWidth × pixelHeight
  ÷ displayScale      DisplayGeometry.scale — reported, never assumed
device points         DisplayGeometry.pointWidth × pointHeight
  − app window origin the app window's rectangle within the screen
app logical units     the space MAUI element bounds live in
```

The transform deliberately **stops at app logical units**. Subtracting the visual tree's root
offset is the existing renderer's job and is not duplicated.

### Why each factor is carried rather than assumed

- **`streamScale`** — a client rendering into a narrow side panel asks the encoder not to spend
  bandwidth on a full 3× framebuffer. Frame pixels are therefore not device pixels.
- **`displayScale`** — reported by the device rather than derived from a device-model lookup, so a
  backend can correct it.
- **app window origin** — the app does not own the whole screen. On a phone it is inset by the
  status and navigation bars. **The app reports this itself**: only it can observe where its window
  sits, so `DevFlowAgentService.GetWindowScreenOrigin` is overridden per platform (the `UIWindow`
  frame on iOS, the content view's on-screen location on Android) and surfaced as
  `windowScreenX`/`windowScreenY` on the agent status. When a platform cannot say, it reports
  `null` and consumers assume the window fills the screen rather than guessing an inset — a
  fabricated origin taps the wrong place while looking entirely plausible.
- **app logical size, separately from window point size** — normally equal, but carried apart so a
  platform whose logical units differ from display points does not skew the overlay.

## Arbitration

Device input goes through the **same mutation lease** as app input. Both the app's self-reported
identity and the companion host's device record derive one hashed, stable device key. That key does
not change when an app launches, reconnects, or exits.

Sharing the stable key is the point: a device-level tap, `maui_tap`, workflow run, Inspector action,
and proxied `mobile_device_*` mutation all contend for the same screen. Independent locks would let
two sessions drive one device while each believed it had exclusive control.

### Where the lease is taken

Exactly one place: the broker. `/api/device/tap` and `/api/device/control` in the Inspector forward
the caller's lease identity and take **no** lease of their own, which is deliberate and not a gap.
`AgentClient.UseMutationLease` only tags requests that reach a connected agent, and a device
operation never does — it targets the emulator or simulator around the app. More fundamentally,
device control exists precisely for the moments when there is no agent to key a lease on: before
launch, and after a crash. The broker resolves the stable device key and takes it with
`ClaimAndBeginExclusive` for the duration of the operation, so an app tap and a device tap still
contend for one lease. A second claim in the Inspector would be a weaker, parallel authority over
the same hardware, and would deadlock against the broker's own exclusive transaction.

### Recovering a lease when an agent disappears

An app-keyed lease is private to one agent id, so a disconnect drops the whole entry. A device-keyed
lease is not: the same key serializes the app, the Inspector's device controls, and the companion
MCP, and it outlives whatever app happened to be running inside the device. Clearing it on any
disconnect would let one crashed agent revoke a live session's hold on the same hardware.

So the device-keyed lease is released on disconnect only when both hold:

- the lease is still owned by that exact registration — agent id **and** instance, because a
  relaunched app re-registers under the same id — so a holder taken by the Inspector's device
  controls or the companion MCP, which record no owner at all, is never touched; and
- no identical registration has replaced it, which is what a socket flap looks like.

Otherwise the lease is left to expire on its own bounded TTL. Recovery advances the authority epoch,
so a client caching one can tell its hold is gone.

### Rotation

`frameQuarterTurns` records the clockwise quarter-turns applied to the device screen to produce the
frame. **Zero is the default and the common case**: both simulator screenshots and the emulator
video stream already arrive in the device's current orientation, so the reported geometry is
already correct.

It is modelled explicitly anyway, because a backend that delivers frames in the display's *natural*
orientation would otherwise produce an overlay that is correct in portrait and transposed in
landscape — which ships, because nobody tests landscape first.

Note that `DisplayGeometry.orientation` and `frameQuarterTurns` are **different things**: the first
describes what the display is presenting, the second describes what was done to the frame. They are
kept apart so a backend change cannot silently redefine either.

### Falling outside the app window

`DeviceToApp` returns `null` when a point lies outside the app window. That is not an error — it is
the signal that an interaction must fall through to the device layer instead of being mis-sent to
the app agent. A tap on a permission dialog, the soft keyboard, or the navigation bar is a device
tap; a tap on a MAUI element is a semantic tap that records a durable selector.

## Layer fallthrough in the Inspector

The Inspector's Interact mode is **layer-aware**, and deliberately not a third mode the user has to
manage. Making the human choose "app tap" or "device tap" would be the moment the abstraction
failed, so the highest-fidelity layer that can service the click is chosen automatically:

| Where the click lands | What happens | Recorded as |
|---|---|---|
| A MAUI element, app running | Semantic tap through the agent — **today's exact path** | `tap` with a durable selector |
| Outside the app window | Device tap | not recorded; the user is told why |
| Anywhere, app crashed or not launched | Device tap | not recorded |

The last row is what turns the disconnected overlay from a dead end into something usable: the
agent dies with the app, but the device does not, so a click can still dismiss a crash dialog.

> **Status.** The crash-survival row works today. The *outside the app window* row works when the
> device reports its screen size: the Inspector then renders the device screen as the substrate
> with the app window inset at its real origin, so there are finally pixels outside the app to
> click. Where a device does not report a display, the Inspector falls back to rendering the app
> window alone and only the crash-survival row applies.

Everything about the second coordinate space lives beside `toAppCoords` in `devflow.js` rather than
being recomputed per handler — six handlers each doing their own arithmetic is exactly how an
overlay ends up correct in portrait and subtly wrong in landscape.

## The substrate

`#df-stage` was always a scaling wrapper around `#app-viewport`. With a device layer it becomes the
**device screen**, and the app window is inset within it at the origin the app reported:

```
#df-stage        ← the device screen (clicks here go to the device)
  └ #app-viewport ← the app window, inset at (originX, originY)
      └ elements  ← the MAUI overlay, unchanged
```

`HtmlRenderer` is untouched. Elements are still positioned relative to `#app-viewport`, which still
means the app window, so `Fit`, `Bounds`, hover, selection, and hit candidates all keep working.

## Hosts

There is nothing per-host to wire. The browser, the VS Code webview, and the Copilot Canvas all
embed the **same** broker-hosted inspector (`/inspector/{agentId}/`), so the substrate, the
fallthrough, and the Device tab reach all three at once. That is the shared-inspector architecture
paying for itself: a device picker or a capability flag implemented three times would be three
chances to disagree.

## Inspector Device workspace

The Inspector's **Data → Device** tab is the integrated device-management surface. It keeps the
app and device views together instead of embedding the standalone Mobile Device Canvas:

- the app-reported model, display, battery, and connectivity remain visible;
- the paired device is selected by default, while a local selector can inspect or manage another
  known emulator or simulator without retargeting the Inspector's app connection;
- lifecycle controls cover create, boot, restart, reveal, shutdown, erase, and delete;
- direct input covers tap, long press, swipe, text, USB HID keys, hardware/system buttons, and
  explicit orientation;
- media controls capture a device screenshot and start, inspect, or stop a bounded recording;
- installed runtimes, device types, and scrubbed host diagnostics remain available in the same
  workspace.

Controls are capability-gated from the selected device record. Unsupported operations stay visible
but disabled, and absent, incompatible, unauthorized, and non-responsive hosts retain distinct
messages. Semantic app actions remain the preferred path: device controls are explicitly described
as the fallback for system UI and content outside the MAUI visual tree.

Erase and delete require two independent checks: the Inspector asks the human to type
`erase <device-id>` or `delete <device-id>`, and the broker verifies both that exact text and the
selected device id before dispatch. Every mutation runs inside a broker transaction on the same
stable device lease used by app actions, so another Inspector, Canvas, MCP client, or workflow
cannot interleave input.

When H.264 is unavailable but screenshots are supported, the shared Inspector polls a bounded PNG
device frame behind the MAUI overlay. A standalone browser can download an explicitly captured PNG;
embedded hosts show the captured preview without pretending their sandbox completed a download.

## Degradation

The device layer is optional at every level. Absence is an ordinary state — but it is carefully
distinguished from *failure*, because collapsing the two is actively harmful: an incompatible or
unauthenticated host reporting as "absent" looks exactly like a machine with no device layer, so a
real and fixable break silently presents as a missing feature that nobody investigates.

| `DeviceHostAvailability` | Meaning | Typical cause |
|---|---|---|
| `Absent` | No host running | Not installed, or the daemon has idled out |
| `NotResponding` | State file exists, host silent | Stale file after a crash |
| `Unauthorized` | Host rejected our control token | Host restarted and reissued one |
| `Incompatible` | Protocol or host version outside the validated contract | Host older than the pinned companion or on another protocol |
| `Available` | Usable | — |

| Situation | Behaviour |
|---|---|
| Device exists but lacks a capability | Refused with a reason naming the capability |
| Desktop MAUI app | No device identity, no pairing, no change in behaviour |

Discovery is **file-based and never launches anything as a side effect of looking**. A DevFlow
session that only wants to know whether device control is possible must not start a background
daemon to find out.

> Note that the host's state file exists only while the host is **running** — it is removed on
> shutdown. Its absence therefore means "no host running", which is not the same as "not
> installed". Distinguishing those is the job of the managed-install path, not of discovery.

## Talking to the host

The wire boundary is pinned by contract tests against a stub speaking the real protocol, because
the adapter's failure mode is silence — a wrong field name or a missing credential fails in a way
indistinguishable from "not installed". Degradation tests cannot catch that; only a successful
authenticated round trip proves the binding.

- The current compatibility baseline is Mobile Canvas `0.1.16`, protocol `1.0`, upstream revision
  `0f0d7806a08d41b3b0b932c05b313686486f75ca`. DevFlow requires that exact protocol and host
  `0.1.16` or newer; changes to the baseline require refreshing the successful route and payload
  fixtures.
- State file: `~/.mobile-canvas/hosts/v1.0/host.json`, fields `schemaVersion`, `port`,
  `processId`, `controlToken`, `version`. The unversioned `~/.mobile-canvas/host.json` remains a
  discovery fallback so an older installation can be diagnosed and replaced, not driven.
- Health: `GET /api/v1/status`.
- Every request carries `Authorization: Bearer {controlToken}`. This is a trusted local control
  credential, distinct from the single-use bootstrap secret the host issues to canvas panels.
- The protocol and minimum product version are checked before any request is made. An unstamped,
  older, or different-protocol host is refused up front rather than optimistically driven, because
  guessing risks silently wrong device control instead of a clean error.

## Using it

The broker is the single front door: the CLI, the Inspector, and the MCP server all read the same
device list and share one idea of which app is on which device.

### CLI

```bash
maui devflow devices setup             # what the device layer needs on this machine
maui devflow devices host install      # explicitly download and verify the pinned companion
maui devflow devices host status       # read-only; never downloads or starts a process
maui devflow devices host start        # re-verify, then start the installed companion
maui devflow devices host stop         # stop only when a host is already registered
maui devflow devices host update       # install the version pinned by this MAUI CLI
maui devflow devices host mcp          # run the companion's separate mobile_device_* MCP server
maui devflow devices list              # devices, each paired with the app inside it
maui devflow devices list --json
maui devflow devices boot <device-id>
maui devflow devices shutdown <device-id>
```

`setup` and `host status` diagnose rather than download — silently fetching and executing a binary
from a read path is not allowed. `host install` and `host update` are the explicit acquisition
boundary: they accept no arbitrary version or URL, download only the release pinned into the
shipping CLI, enforce compressed and decompressed SHA-256 hashes and exact sizes, and stage the
runtime before an atomic install. `host start` hashes every installed file again and never falls
back to `PATH` or an environment override. `host stop` talks directly to an already registered
host, so asking to stop an absent host cannot accidentally start one.

The diagnostic commands give each state a *different* instruction, because
"not installed" and "installed but unusable" look identical to a user and need opposite responses.

The companion MCP process remains a separately named server with its upstream
`mobile_device_*` inventory. Its 61 tools are not copied into the existing `maui_*` server. The
MAUI DevFlow VS Code extension offers this verified-runtime fallback only when the dedicated
`redth.mobile-canvas` extension is absent, avoiding two providers for the same device service.
The `dotnet-maui` plugin publishes the same command through its `.mcp.json`, so Copilot plugin
installs discover the server without duplicating its schemas in the DevFlow MCP host.
The stdio proxy forwards schemas and results unchanged, but wraps mutating calls in a broker
transaction on the same stable device key used by app operations. It claims before forwarding,
heartbeats long calls, releases only after the matching JSON-RPC response, rejects mutating batches
and targetless unknown calls, and conservatively leaves unknown-completion transactions to expire
under the broker's bounded transaction TTL. Every forwarded mutation and every Canvas action uses
an operation-scoped lease identity, so concurrent calls from one MCP process or one Canvas panel
cannot be mistaken for re-entrant access by the same writer.

The pinned binary and its real MCP inventory are exercised by:

```bash
node eng/smoke-tests/mobile-canvas-companion-smoke-test.mjs --maui <path-to-maui>
```

Passing `--platform android|ios --require-device` additionally exercises a real booted target,
idempotent orientation input, and PNG capture. The integration workflow runs that device-backed
mode on Android and iOS, performs a clean host restart on both, and adds abrupt host recovery to
the scheduled Android run. The script installs only the manifest-pinned runtime and never stops or
kills a host that was already running when it began.

`list` distinguishes its two empty states, for the same reason:

```
No device host is installed or running.            # install/start the host
No emulators or simulators were found. Create one. # host is fine, nothing to drive
```

### MCP

**Off by default.** These four tools are the only opt-in part of the full MCP profile. The full
profile registers them only when `DEVFLOW_PREVIEW_MOBILE_CANVAS=true` (kill switch:
`DEVFLOW_PREVIEW_KILL_SWITCHES=mobile-canvas`); without it the profile serves 79 tools and no device
layer. The VS Code extension makes the same call for the companion's own separate MCP server behind
`mauiDevflow.registerMobileCanvasMcpServer`, also off by default. One decision, two surfaces: the
companion is a separately installed experimental binary that neither surface ships, so advertising
its tools unasked offers a capability that is usually absent — and every advertised tool costs the
model attention on the tools that do work. The restricted `maui-test-agent` profile is unaffected by
the gate and still serves exactly its 14 tools.

| Tool | Purpose |
|---|---|
| `maui_device_list` | Devices, each paired with the app agent inside it |
| `maui_device_boot` | Boot and wait until driveable |
| `maui_device_shutdown` | Power off without erasing |
| `maui_device_tap` | Tap a physical point |

The CLI equivalents under `maui devflow devices` are not gated: they are typed by a human who has
already decided to use the device layer, so there is no attention budget to protect.

This set is deliberately small. Every tool added competes for an agent's attention against the
~79 existing `maui_*` tools, and a bloated surface measurably degrades tool selection, so only
operations the in-app agent structurally cannot perform are exposed.

`maui_device_tap` exists for UI the visual tree cannot reach — permission dialogs, the soft
keyboard, OS navigation, or anything before launch or after a crash. Its description steers callers
back to `maui_tap` for anything inside the app, because a selector survives a layout change and a
coordinate does not.

## Executable device preconditions

`DevicePreconditions` and `DevicePreconditionApplier` define fail-closed permission, network,
battery, and orientation setup. The broker execution extension resolves the app's exact paired
device after acquiring the workflow lease, binds permission changes to the app package/bundle ID,
and requires read-back-capable operations to confirm their result.

Inspector preflight and confirmation display the exact device changes and bind them to a digest
submitted with the start request. Test-agent run approvals include the same bounded effect list and
remain bound to the committed flow digest. Repair-validation dispatches remain blocked because a
selector-repair grant does not authorize replaying unrelated device mutations.

Simulated location is refused before any mutation because Mobile Canvas cannot read it
back; iOS network simulation is likewise refused because that backend changes only the status-bar
indicator, not the app's connection.

### Broker HTTP

| Route | Purpose |
|---|---|
| `GET /api/devices` | Devices plus host availability |
| `GET /api/devices/catalog` | Devices, runtimes, device types, and host diagnostics |
| `GET /api/devices/{id}` | Complete selected-device details |
| `GET /api/devices/{id}/screenshot` | Device PNG |
| `GET /api/devices/{id}/recording` | Recording status and bounded artifact metadata |
| `POST /api/devices/control` | Typed, leased lifecycle/input/media operation |
| `POST /api/devices/{id}/boot` | Boot |
| `POST /api/devices/{id}/shutdown` | Shut down |
| `POST /api/devices/{id}/tap?x=&y=` | Tap at a device point |

The consolidated control route is used by the Inspector and accepts only the documented action
allowlist. Create operations use the catalog lease; target operations use the selected device's
stable lease. Device erasure and deletion additionally require the exact confirmation fields.
The device list carries the host's availability alongside the devices so a caller can tell
"no devices" from "no device layer" without a second request.

## Recording out-of-app interactions

Flows record semantic steps against MAUI elements, which is what makes them durable. A first-run
permission prompt, a share sheet, or the soft keyboard is not in the visual tree at all, so a flow
that touched one could not be authored — the recording dead-ended.

A **device step** closes that gap without weakening the rest. It rides as an additive
`deviceSteps` extension field and carries both a coordinate and a description of the native view
under it, so replay can match by text or id and fall back to coordinates only when it must. A step
with neither is reported as `IsFragile`, reusing the vocabulary the flow recorder already applies to
a selector without an `AutomationId`: it blocks nothing, it tells a reviewer which steps will break
first when the UI moves.

> **Status.** Successful paired device taps append a durable `deviceSteps` item to the active
> broker recording. Append, move, and delete editing preserves or remaps the numeric app-step
> relationship. The execution extension is implemented and tests native accessibility ID/text
> before coordinate fallback. Inspector and test-agent runs execute it only after the exact
> digest-bound device changes are displayed and approved; repair validation remains blocked.

Prefer a **precondition** over a device step wherever one exists. A flow that grants the permission
up front never sees the prompt, and a test that sets up its own environment is deterministic in a
way that one tapping through a dialog is not.

## Live video

Where the device reports `liveStream`, the Inspector replaces the polled screenshot with decoded
H.264 painted onto the stage backdrop. Three properties matter more than the picture:

- **It never touches the agent.** Frames come from the device host through the broker's proxy, so
  video costs the app nothing — and keeps arriving after the app has died, which is what makes the
  crash-survival view live rather than frozen.
- **It never drives the tree.** Frame cadence and tree cadence are separate. 50 frames a second
  must not mean 50 visual-tree pulls a second, so the video path schedules no refreshes at all.
- **It always degrades.** No WebCodecs, no `liveStream` capability, a refused upstream, a decoder
  error — every one falls back to the screenshot path the Inspector was already using.

The first WebSocket text message is the protocol `StreamDescriptor`. The Inspector validates its
encoding, cadence and scale, records the capture source, and derives the actual AVC profile from
the stream's SPS instead of assuming one encoder profile.

### Why it is proxied

The browser never connects to the device host. That host authenticates control clients with a
bearer token, and a browser cannot attach headers to a WebSocket — nor should that token ever be
placed in a page, a URL, or a DOM a framed document could read. The broker holds it server-side and
the page presents only the embed token, which also keeps the broker the single front door.

**A WebSocket is not subject to the same-origin policy**, so the proxy enforces two gates before
upgrading, and a page that fails either gets a 403:

1. **Loopback origin** — proves the caller is on this machine.
2. **The embed token** — proves it is an Inspector session, not merely some other local page. A dev
   server or a docs site on localhost passes the first gate and must still be refused.

Without both, any page a user visited could open a live feed of their device screen and the broker
would helpfully authenticate it on their behalf.

The proxy is **one-directional** on purpose: the video channel carries frames out and nothing in.
Input travels the HTTP control path where the mutation lease arbitrates it. A socket that also
accepted commands would be a second, unarbitrated way to drive the device.

### Cadence

While video is live the screenshot fetch is skipped entirely, because it is a stale duplicate of
pixels the stream is already painting. Video therefore makes the Inspector *cheaper*: the frame
half of every poll disappears and the tree half is unchanged.

### Stream framing

H.264 arrives as an Annex-B byte stream that aligns to neither WebSocket messages nor frames, so
`inspector-video.js` reassembles it:

- NAL units are split on 3- and 4-byte start codes, and the trailing bytes are returned as a **raw
  remainder including its start code** so the caller can prepend it to the next chunk. Returning
  the payload alone would make the reassembled buffer begin mid-unit and silently drop exactly the
  frames that straddle a message boundary.
- Parameter sets are emitted with the picture that follows them, not alone — feeding an SPS as if
  it were a frame yields a decoder that configures and never outputs anything.
- A picture is closed by what **follows** it, not by the slice itself. A single picture may be
  coded as several slice NALs — common on hardware encoders under a bandwidth cap — and submitting
  each as its own picture feeds the decoder partial frames, which it rejects and never recovers
  from. A new picture is detected by the slice header's `first_mb_in_slice` being zero. The cost is
  one frame of latency, which is the right trade for a stream that works on every encoder.
- Frames before the first keyframe are discarded: a decoder started mid-stream has no reference
  frames, so they would decode to garbage. This is why a stream takes a moment to appear.

## Contributing to a replay

The replay engine lives in `Microsoft.Maui.DevFlow.Testing`, a shipped package that deliberately
does **not** reference the device layer — a project reference the other way would force a
non-shipping assembly into a public package. Instead it declares a seam,
`IFlowReplayEvidenceCapture`, and the CLI, which owns both sides, implements it via
`DeviceFlowObserver`.

> **Status.** The canonical runner now invokes whole-run observation and failure explanation, and
> broker runs compose the device decorator without hiding the existing detailed evidence capture.
> Device-video recording is opt-in in the Inspector run check and may also be requested explicitly
> as `device-recording` expected evidence. The stopped MP4 is digest-bound into the run report.
> Foreground-cause enrichment remains empty on a backend that cannot identify what is covering the
> app.

Two things the engine cannot obtain on its own, because both require seeing outside the app:

**A recording of the run.** `BeginRunAsync` returns a handle disposed when the run ends — whether
it passed, failed, or threw — so a recording can never outlive the run that started it. A failed
test that ships with video of its own failure is the most useful artifact a replay can produce,
because the interesting moment has already passed by the time anyone reads the report. A device
that cannot record is not a reason to refuse the run.

**A cause for a failed step.** The engine can report that an element was "not visible"; only
something outside the app can report that a permission dialog was covering it, that the soft
keyboard was over it, or that the app was backgrounded — none of which are in the visual tree.
`ExplainFailureAsync` fills `FlowStepResult.FailureCause`, additively, leaving the original `Error`
exactly as the step reported it.

The restraint matters as much as the capability. An explanation is offered **only** for failure
kinds a foreign window could plausibly account for — not visible, not actionable, timeout, stale.
A selector that matched nothing is an authoring problem, and blaming the environment for it would
send the reader to investigate something that was fine while the real cause goes unexamined. When
the app is frontmost, nothing is said at all.

Both members are defaulted, so an implementer written before them keeps compiling and behaves
identically.

If starting the device recording throws, the inner capture's handle is disposed before the failure
propagates. The caller never receives a handle in that case, so nothing else would ever close it.

## Recording files: containment and retention

Recordings are the one place the device host hands DevFlow a filesystem path that DevFlow then
reads, copies, hashes into evidence, and serves over HTTP. Two rules govern them.

**Containment is resolved, not lexical.** `DeviceRecordingPathGuard` refuses any path whose *final*
target lies outside the DevFlow-owned root. It resolves each path segment through symlinks,
junctions, and every other reparse point before comparing, because a link planted inside the root
passes a string prefix check while the bytes read come from somewhere else. Windows alternate data
streams (`run.mp4:hidden`) are refused outright: no recording DevFlow names ever contains one, and
the stream read would not be the file the metadata describes. The check is re-run at every step
that touches bytes — copy, hash, and open — rather than once at the start, so a link swapped in
between the check and the read is caught at the read.

Two roots exist, and each is checked against its own consumer:

| Root | Owner | Checked when |
|---|---|---|
| `%TEMP%/maui-devflow/device-recordings` | the device surface | a host reports a stopped recording; a flow observer hashes it into evidence; the Inspector copies it |
| `%TEMP%/maui-devflow/workbench-device-recordings/inspector_<pid>_<start>_<guid>` | one Inspector process | a retained run recording is looked up and streamed |

**Retention is bounded by age and by process liveness.** The Inspector's per-process directory is
deleted at shutdown, and directories belonging to processes that are provably gone are swept at
startup — a Win32 error that leaves ownership unproven preserves the directory rather than guessing.
The surface's shared root cannot use process liveness, because several DevFlow processes write into
it, so it is swept by age: `StartRecordingAsync` deletes ordinary `.mp4` files whose last write is
older than 24 hours. A recording is bounded to one hour by `timeoutSeconds`, so nothing inside that
window can be finished, and nothing younger than the retention window is ever touched — an
in-progress recording belonging to another process is never deleted out from under its writer. The
sweep skips reparse points instead of following them, so a link planted in the root cannot turn
cleanup into arbitrary deletion.

## Implementation

| Type | Responsibility |
|---|---|
| `IDeviceSurface` | The device layer's contract. Never throws for an unsupported operation |
| `NullDeviceSurface` | The honest no-op used when there is no device layer |
| `MobileCanvasDeviceSurface` | Adapter over a locally installed device host |
| `MobileCanvasHost` | File-based discovery of that host |
| `DeviceRecordingPathGuard` | Link-resolving containment for every recording path DevFlow reads |
| `DeviceCoordinateSpace` | The full coordinate chain, with round-trip guarantees |
| `inspector-video.js` | Annex-B reassembly and WebCodecs decoding for the live stream |
| `BrokerServer.Video.cs` | Authenticated one-directional video proxy |
| `DeviceIdentity` / `DeviceIdentityMatcher` | Pairing an app agent to its device |
| `DeviceFlowObserver` | Run recording and failure causes for a flow replay |
| `DeviceIdentityProvider` | Agent-side self-reporting (Agent.Core, with platform overrides) |

## Testing

```bash
dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Tests/ --filter "FullyQualifiedName~Device"
```

The coordinate tests pin the round trip across every orientation, density and stream scale, and
additionally pin specific corners. A round-trip test alone is not sufficient: an inverse-consistent
but *mirrored* transform round-trips perfectly and is still wrong.
