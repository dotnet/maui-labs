# Foldable emulator and simulator controls

`maui devflow device fold` (alias `device hinge`) controls an explicitly selected
virtual device. These are host operations: neither a running app nor a DevFlow
agent is required. They affect the entire device, including other running apps.

| Backend | Requirements | Support |
| --- | --- | --- |
| Android | Connected foldable AVD with enabled `hinge-angleN` sensors; `adb` on PATH | Read angles, selected-hinge presets, exact angles, animated transitions |
| iOS | Booted foldable simulator, Apple Silicon Mac, Xcode/iOS Simulator SDK 27.1+, `--experimental` | Private simulator hinge read/write; integrated display metadata and endpoint handover verification |
| Physical devices | Not supported by these host commands | No mechanical hinge control or logical device-state overrides |

The supported control range is **0..180 degrees for inward-folding hinges**.
This is the command's supported range, not a claim about every foldable's
hardware range. Multi-hinge Android AVDs expose their enabled indices; a preset
changes **only the selected hinge**, not every hinge. Outward folding and
device-specific tent/flipped postures are not implemented.

## Android

```sh
maui devflow device fold capabilities --device emulator-5554
maui devflow device fold get --device emulator-5554 --json
maui devflow device fold set folded --device emulator-5554
maui devflow device fold set unfolded --device emulator-5554
maui devflow device fold set partially-folded --angle 110 --device emulator-5554
maui devflow device fold set partially-folded --angle 90 --duration 2 --hinge-index 0 --device emulator-5554
```

Presets are 0 degrees (`folded`), 180 degrees (`unfolded`), and 90 degrees
(`partially-folded`). `--angle` overrides the partial preset and must be strictly
between 0 and 180. NaN, infinity, invalid durations, and unavailable hinges are
errors; values are never silently clamped.
`set --dry-run` validates and previews the requested values without contacting
the device, compiling a helper, or running post-actions.

The backend uses the emulator console's enabled hinge sensors and reads their
values back. It does **not** hardcode Android `cmd device_state` IDs or force a
logical OS state. The reported mode is derived from hinge readings, not from a
WindowManager folding feature or app layout. An emulator with no enabled hinge
sensor reports unsupported. Android display/app geometry is not returned by this
initial host backend.

## iOS Simulator (experimental)

```sh
xcrun simctl list devices booted
maui devflow device fold capabilities --udid SIMULATOR_UDID
maui devflow device fold capabilities --udid SIMULATOR_UDID --experimental
maui devflow device fold get --udid SIMULATOR_UDID --experimental --json
maui devflow device fold set unfolded --udid SIMULATOR_UDID --experimental
maui devflow device fold set partially-folded --angle 110 --duration 2 --udid SIMULATOR_UDID --experimental
maui devflow device fold set folded --udid SIMULATOR_UDID --experimental --timeout 60
```

No supported fold setter was found in the inspected Xcode 27.1/27.2 CLI
surfaces. Explicit opt-in permits a small, simulator-only helper to use the
private HID hinge protocol researched by
[hinge](https://github.com/artemnovichkov/hinge) and
[duoctl](https://github.com/skarol/duoctl). The helper reads CoreMotion directly;
it does not depend on `devicectl device motion hinge-angle`, which is unavailable
in the inspected shared CoreDevice 651.31.

The Driver embeds the helper source and compiles/ad-hoc signs it locally with
the selected Xcode on first use. No download, Homebrew, Python, additional NuGet
dependency, or app instrumentation is needed. The helper is **not** an app
library and must never be linked into a shipping app. Xcode's command-line
components must already be initialized. `DEVELOPER_DIR` is respected. Cache keys
include source, actual Xcode build, SDK path/version, CoreDevice version, and
architecture, so replacing Xcode at the same path does not reuse a stale helper.

These private interfaces can break with future Xcode or simulator runtimes.
The Device Hub slider may not reflect an externally changed angle. Installing
a new Xcode/SDK does not install its matching simulator runtime or managed .NET
bindings. Record all versions separately when reporting failures.

State includes each integrated display's ID, UUID, active/primary flags, native
pixel dimensions, scale, and rotation. **Primary is not the same as active.**
Use the reported active display ID when capturing directly:

```sh
xcrun simctl io SIMULATOR_UDID screenshot --display=DISPLAY_ID capture.png
```

This feature does not replace the existing Driver's host touch/capture backends
or add display routing to the app agent. In particular, legacy host input can
reach the cover display while the inner display is active; verify the actual
target when automating system UI.

## Completion, ownership, and errors

`set` reads the starting angle instead of inventing one, sends the change, and
polls for the requested angle within one degree. It verifies the **final** angle
for animated transitions too. iOS folded/unfolded presets additionally require
the corresponding primary/non-primary display to become active and its reported
geometry to remain stable over two observations. Intermediate angles do not
assume a universal display-handover threshold, because the active screen can
depend on the path taken through the hinge range.

Success reports both the requested values and observed state. On multi-hinge
devices the overall observed mode can differ from the selected-hinge preset.
This verifies device-side readings, **not app readiness or UI state retention**.
The timeout covers preparation, animation, and confirmation; it must exceed the
duration. Cancellation and timeouts can leave a partially changed device. Read
state before deciding whether to retry; there is no automatic rollback.

A cross-process, per-device lock prevents concurrent DevFlow fold mutations.
It is distinct from the app-agent mutation lease and does not prevent another
tool or person from moving the hinge. Explicit `--device` or `--udid` targeting
is mandatory; these commands never select the first booted device or an agent's
unrelated emulator.

`set` also supports `--and-screenshot`, `--and-tree`, and `--and-tree-depth`.
Only those optional post-actions require an app agent; select it with the normal
`--agent-host`/`--agent-port` options. Capture failure can occur after a successful
hinge change. Refresh element IDs after transitions.

Apple's Xcode 27.2 release notes document black Duo captures shortly after boot,
Device Hub accessibility limitations, and modifier-key issues after display
handover. A booted device or a matching angle is not proof that captures, external
accessibility, or app input are ready.

## MCP and Driver

The same operations are exposed as `maui_fold_capabilities`,
`maui_get_fold_state`, and `maui_set_fold_state`. Specify exactly one of
`androidDevice` or `simulatorUdid`; iOS requires `allowExperimentalIos: true`.
Set accepts `mode`, optional `angleDegrees`, `hingeIndex`, `durationSeconds`,
and `timeoutSeconds`. Tool failures are errors, not success-shaped fallback data.

```csharp
var device = new FoldableDevice("android", "emulator-5554");
var capabilities = await device.GetCapabilitiesAsync();
var state = await device.GetStateAsync();
var changed = await device.SetAsync(
    FoldMode.PartiallyFolded, angleDegrees: 110, durationSeconds: 2,
    cancellationToken: cancellationToken);
```

Models live in portable Client and are re-exported by Driver; native execution
stays in Driver. There are no new agent HTTP routes or `AgentClient` setters:
an in-process app cannot invoke host emulator tools. Public app-side
`UIHingeInteraction`/Jetpack WindowManager observation, fold-region layout
diagnostics, orientation control, and display-targeted host touch injection
remain separate work tracked in [#746](https://github.com/dotnet/maui-labs/issues/746).
