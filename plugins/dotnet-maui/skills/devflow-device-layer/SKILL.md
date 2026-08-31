---
name: devflow-device-layer
description: >-
  Experimental and optional. Control the emulator or simulator around a MAUI app, and reach UI the app's visual tree cannot. USE FOR: booting a device before deploying, tapping system permission dialogs or the soft keyboard, diagnosing why DevFlow went blind after a crash, establishing device preconditions (permissions, location, network, battery) for a deterministic test. DO NOT USE FOR: tapping controls inside the app (use maui_tap with a selector), agent connectivity problems (use devflow-connect), desktop MAUI apps, which have no virtual device, or any machine with no Mobile Canvas host installed, where every device capability reports unavailable by design.
---

# DevFlow device layer

> **Experimental, optional, and off by default.** This layer depends on the separately installed
> Mobile Canvas companion. Without it every capability reports unavailable and the rest of DevFlow
> is unaffected.
>
> The four `maui_device_*` tools below are **not** in the default MCP inventory. The full profile
> registers them only when `DEVFLOW_PREVIEW_MOBILE_CANVAS=true`; VS Code offers the companion's own
> separate server only when `mauiDevflow.registerMobileCanvasMcpServer` is on. Both default off, for
> the same reason: advertising a tool that needs an uninstalled binary spends the model's attention
> on a capability that is usually absent. If these tools are missing from a session, the gate is off
> — that is the expected state, not a fault, and the CLI equivalents below still work.

DevFlow observes an app from **inside** it. Everything the in-app agent knows arrives through a
process living in the app, so DevFlow goes blind whenever the app is not both running and
frontmost. The device layer is a second vantage point that survives that.

## When to use

- **Nothing is running yet.** Boot a device before deploying.
- **A system dialog is on screen.** Permission prompts, share sheets, photo pickers and the soft
  keyboard are not in the MAUI visual tree, so `maui_tap` cannot reach them.
- **The app crashed.** The agent died with it; the device did not.
- **A test needs a specific environment.** Location denied, offline, low battery.

## When not to use

- **Anything inside the app.** Use `maui_tap`, `maui_fill`, `maui_scroll` with a selector. A
  selector survives a layout change; a device coordinate does not, so a recorded test built on
  coordinates is fragile by construction.
- **Agent connectivity problems.** Use `devflow-connect`.
- **Desktop MAUI apps.** Windows and macOS apps have no virtual device around them, so the device
  layer reports everything as unavailable — correctly.

## Tools

| Tool | Purpose |
|---|---|
| `maui_device_list` | Devices, each paired with the app agent running inside it |
| `maui_device_boot` | Boot and wait until driveable |
| `maui_device_shutdown` | Power off without erasing |
| `maui_device_tap` | Tap a physical point, in device-independent points from the top-left |

CLI equivalents: `maui devflow devices list | boot <id> | shutdown <id>`.

Do not confuse `maui_device_list` with `maui_device_info`. The first is the device as the **host**
sees it (lifecycle, display, capabilities). The second is the device as the running **app** sees it
(model, OS version, battery, connectivity). Both describe the same physical device from the two
vantage points.

## Reading availability

`maui_device_list` returns an `available` flag alongside the devices. Distinguish carefully:

| Result | Meaning | What to do |
|---|---|---|
| `available: false`, absent | No device host running | Say so; do not retry in a loop |
| `available: false`, not responding | Stale state, host gone | Suggest restarting the host |
| `available: false`, unauthorized | Host restarted, token stale | Suggest restarting the host |
| `available: false`, incompatible | Protocol mismatch | Suggest aligning versions |
| `available: true`, empty list | Host fine, no devices exist | Suggest creating one |

**Absence is normal.** Most machines have no device host, and a desktop MAUI app never has a
virtual device. Report it plainly and continue with the in-app agent; it is not an error to fix.

## Typical flow

```
maui_device_list                      -> find a device, note its id
maui_device_boot <id>                 -> boot and wait
(deploy the app to that device)
maui_wait                             -> the agent attaches
maui_tree / maui_tap / maui_fill      -> drive the app semantically
maui_device_tap                       -> only for UI outside the app
```

## Preconditions over interaction

If a flow keeps hitting the same permission prompt, do not record a device tap for it. Establish
the permission as a **device precondition** instead so the prompt never appears. A test that sets
up its own environment is deterministic; one that taps through a prompt depends on install state.

Preconditions **fail fast**: if one cannot be established on this platform, the run stops rather
than continuing. That is deliberate — a green test in the wrong environment reports confidence that
was never earned.

## Coordinates

`maui_device_tap` takes device-independent **points** from the top-left of the display, not pixels
and not app coordinates. The app window is usually inset below the status bar, so a point inside
the app is not the same number in both spaces. Prefer `maui_tap` whenever the target is in the
visual tree.
