# Troubleshooting

## Table of Contents
- [Reading machine-readable output and errors](#reading-machine-readable-output-and-errors)
- [Connection Refused](#connection-refused--cannot-connect)
- [Android UI Thread Exceptions](#android-ui-thread-exceptions)
- [Build Failures](#build-failures)
- [CDP Not Connecting](#cdp-not-connecting-blazor-hybrid)
- [Mac Catalyst Permission Dialogs](#mac-catalyst-repeated-permission-dialogs-on-rebuild)

## Reading machine-readable output and errors

Always pass `--json` to any `maui` command an agent will parse, and `--ci` for
non-interactive failure-fast runs.

> **Which commands this applies to.** The flat stdout envelope below covers the
> top-level command groups (`maui android`, `maui apple`, `maui device`, …).
> Two exceptions: (1) `maui devflow …` subcommands (`wait`, `diagnose`, `ui …`,
> etc.) write a `{ "error", "type", "retryable", "suggestions" }` object to
> **stderr** and keep stdout for data; (2) `maui doctor --json` emits a
> `DoctorReport` (top-level `status` plus `checks[].fix`), not this envelope.
> See [connectivity.md](connectivity.md) for the DevFlow error shape.

### Error envelope

When a `maui` command fails with `--json`, it writes a structured error object
at the **top level** of stdout (there is no enclosing `"error"` wrapper).
Property names are `snake_case`:

```json
{
  "code": "E2103",
  "category": "platform",
  "severity": "error",
  "message": "Android SDK licenses have not been accepted.",
  "remediation": {
    "type": "autofixable",
    "command": "maui android sdk accept-licenses"
  },
  "context": { "sdk_path": "/Users/me/Library/Android/sdk" },
  "native_error": "..."
}
```

Optional fields (`remediation`, `context`, `native_error`, `docs_url`,
`correlation_id`) are **omitted entirely** when null — agents must null-check
before dereferencing.

### Code categories

| Prefix | Category | Examples |
|--------|----------|----------|
| `E1xxx` | Tool error (likely an internal bug) | `E1001` InternalError, `E1004` InvalidArgument, `E1006` DeviceNotFound, `E1007` PlatformNotSupported |
| `E2xxx` | Platform / SDK | `E2001` JdkNotFound, `E2101` AndroidSdkNotFound, `E2103` AndroidLicensesNotAccepted, `E2106` AndroidEmulatorNotFound, `E2110` AndroidAdbNotFound, `E2201` AppleXcodeNotFound, `E2204` AppleSimulatorNotFound, `E2402` MauiWorkloadMissing |
| `E3xxx` | User action required | (e.g., choose a target when multiple match) |
| `E4xxx` | Network | (download / fetch failures) |
| `E5xxx` | Permission | (sandbox / elevation issues) |

### Remediation

When present, `remediation.type` is a **lowercase** string, one of:

- `autofixable` — run `remediation.command` and retry the original command.
- `useraction` — present `remediation.manual_steps` to the user.
- `terminal` — cannot be fixed (e.g., unsupported OS); abort.
- `unknown` — fall back to displaying `message`.

If `remediation` is missing from the envelope, the failure has no auto-fix
hint — surface `message` (and `native_error` if present) and stop retrying.

### Worked example

`maui android emulator start <name>` will throw `E2106` *with* an
`autofixable` remediation when the Android emulator binary is not installed:

```bash
maui android emulator start Pixel8 --json
# → { "code": "E2106",
#     "category": "platform",
#     "message": "Android emulator not installed",
#     "remediation": { "type": "autofixable",
#                      "command": "maui android sdk install emulator" } }

# Auto-fix path:
maui android sdk install emulator --json
maui android emulator start Pixel8 --json   # retry original
```

Other E2106 throw sites (e.g., "no AVD with that name") emit the same code
**without** a `remediation` block — that's the case where the agent should
stop and surface the message instead of looping.

## Connection Refused / Cannot Connect

If `maui devflow ui status` fails with connection refused:

1. **App not running?** Verify the app launched: check the build output for errors.
2. **Run the diagnostic first:** `maui devflow diagnose` separates broker
   startup, project integration, no running app, and target-device networking.
3. **Check the broker:** Run `maui devflow list` to see if the agent registered. If the list
   is empty, the app may not have connected to the broker yet (wait a few seconds and retry).
4. **Wrong port?** If using `.mauidevflow`, ensure the port matches between build and CLI.
   Run CLI from the project directory so it auto-detects the config file.
5. **Port already in use?** Another process may hold the port. Check with:
   ```bash
   # Not yet wrapped by 'maui' CLI — use raw lsof
   lsof -i :<port>       # macOS/Linux
   ```
   With the broker, this is less common since ports are auto-assigned.
6. **Android?** `maui devflow wait --wait-platform android` (or `maui devflow list`)
   sets up the broker reverse (tcp:19223) and agent forward automatically; re-run
   `list` after each deploy to repair the agent forward. If broker/list is empty,
   the direct `.mauidevflow` port is the one forward the CLI does *not* auto-set —
   do it manually and check direct status:
   ```bash
   adb devices
   adb forward tcp:9223 tcp:9223
   maui devflow agent status --agent-host localhost --agent-port 9223
   ```
7. **Mac Catalyst?** Check entitlements include `network.server` (see setup.md step 5).
8. **macOS (AppKit)?** Ensure `AddMacOSEssentials()` is called and the app window appeared.
   See [references/macos.md](macos.md) for troubleshooting.
9. **Linux/GTK?** No special network setup needed — runs directly on localhost. Check if the app started successfully.
10. **Broker issues?** `maui devflow broker status` to check. `maui devflow broker stop` then
    retry (CLI will auto-restart it).

## Android UI Thread Exceptions

If `maui devflow ui tap`, `fill`, `focus`, or other UI actions fail on Android
with `CalledFromWrongThreadException`, treat it as likely DevFlow agent action
dispatch trouble rather than an app logic bug, especially when manual input or
ADB taps work.

Capture evidence before changing app code:

```bash
maui devflow agent status --agent-host localhost --agent-port <port>
maui devflow ui query --automationId <control-id> --agent-host localhost --agent-port <port>
adb logcat -d -t 300 | grep -i "CalledFromWrongThreadException\\|DevFlow\\|DOTNET"
```

Report the DevFlow command, target platform, agent version from `agent status`,
the queried element id/AutomationId, and the logcat exception. Do not work
around this with coordinate-only automation unless you only need a temporary
validation fallback.

## Build Failures

**Missing workloads:**
```
error NETSDK1147: To build this project, the following workloads must be installed: maui-ios
```
Fix: `dotnet workload install maui` (installs all MAUI workloads).
Error code via `maui` JSON: `E2402` MauiWorkloadMissing (often `autofixable`).

**SDK version mismatch:**
```
error : The current .NET SDK does not support targeting .NET 10.0
```
Fix: Install the required .NET SDK version, or check `global.json` for version pins.

**Android SDK not found:**
```
error XA0000: Could not find Android SDK
```
Fix: Install Android SDK via `maui android sdk install "platforms;android-35"`
(or run `maui android install` for guided setup), or set `$ANDROID_HOME`.
Error code via `maui` JSON: `E2101` AndroidSdkNotFound.

**iOS provisioning / signing errors:**
Fix: For simulators, ensure no signing is configured (default). For devices, set up provisioning
profiles via your Apple Developer account.

**General build failure recovery:**
1. `dotnet clean` then retry the build
2. Delete `bin/` and `obj/` directories: `rm -rf bin obj` then rebuild
3. Check the full build output (not just the last error) — earlier warnings often reveal the root cause

## CDP Not Connecting (Blazor Hybrid)

If `maui devflow webview status` fails but `ui status` works:

1. **Chobitsu not loading?** Check logs for `[BlazorDevFlow]` messages. If auto-injection failed, add `<script src="chobitsu.js"></script>` manually to `wwwroot/index.html`
2. **Blazor not initialized?** Navigate to a Blazor page first, then retry
3. Check app logs: `maui devflow logs --limit 20` — look for `[BlazorDevFlow]` errors

## Mac Catalyst: Repeated Permission Dialogs on Rebuild

If macOS prompts "App would like to access your Documents folder" on every rebuild:

**Cause:** TCC permissions are tied to the app's code signature. Ad-hoc Debug builds produce a
different signature each rebuild → macOS forgets the grant and re-prompts. This happens even
with App Sandbox disabled.

**Fix:** Don't access TCC-protected directories (`~/Documents`, `~/Downloads`, `~/Desktop`,
or dotfiles like `~/.myapp/` in the home root) programmatically. Instead use:
- `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` → `~/Library/Application Support/` (not TCC-protected)
- `NSOpenPanel`/`NSSavePanel` for user-initiated file access (grants automatic TCC exemption)

If you can't avoid TCC paths, sign Debug builds with a stable Apple Development certificate
so the code signature stays consistent across rebuilds.

## macOS (AppKit) Issues

For detailed macOS (AppKit) troubleshooting, see [references/macos.md](macos.md#troubleshooting).

Common issues:
- **No window appears** → Missing `AddMacOSEssentials()` in builder
- **SIGKILL on launch** → Don't re-sign manually; clean rebuild instead
- **Blazor stuck on "Loading..."** → Use `MacOSBlazorWebView`, not standard `BlazorWebView`
- **No sidebar content** → Add `MacOSShell.SetUseNativeSidebar(shell, true)` + `FlyoutBehavior.Locked`
