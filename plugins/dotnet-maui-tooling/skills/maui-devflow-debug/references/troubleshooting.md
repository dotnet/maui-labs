# Troubleshooting

## Table of Contents
- [Machine-readable output and error envelope](#machine-readable-output-and-error-envelope)
- [Connection Refused](#connection-refused--cannot-connect)
- [Android UI Thread Exceptions](#android-ui-thread-exceptions)
- [Build Failures](#build-failures)
- [CDP Not Connecting](#cdp-not-connecting-blazor-hybrid)
- [Mac Catalyst Permission Dialogs](#mac-catalyst-repeated-permission-dialogs-on-rebuild)

## Machine-readable output and error envelope <a name="machine-readable-output-and-error-envelope"></a>

Always pass `--json` to any `maui` command an agent will parse, and `--ci` for
non-interactive failure-fast runs.

The full `--json` error envelope contract (schema, code categories, worked examples, PowerShell and Bash consumers) is documented in
[`src/Cli/README.md` — Error envelope](../../../../../src/Cli/README.md#error-envelope).

**Quick reference** — when a non-DevFlow command fails with `--json`, stdout is a top-level JSON object (no `"error"` wrapper). Note: `maui devflow ...` uses a different JSON error shape and writes errors to stderr.

```json
{
  "code": "E2103",
  "category": "platform",
  "severity": "error",
  "message": "Android SDK licenses have not been accepted.",
  "remediation": {
    "type": "autofixable",
    "command": "maui android sdk accept-licenses"
  }
}
```

Optional fields (`remediation`, `context`, `native_error`, `docs_url`, `correlation_id`) are **omitted entirely** when null.
When `remediation.type` is `autofixable`, run `remediation.command` then retry the original command.
When `remediation` is absent, surface `message` and stop retrying.

## Connection Refused / Cannot Connect

If `maui devflow ui status` fails with connection refused:

1. **App not running?** Verify the app launched: check the build output for errors.
2. **Check the broker:** Run `maui devflow list` to see if the agent registered. If the list
   is empty, the app may not have connected to the broker yet (wait a few seconds and retry).
3. **Wrong port?** If using `.mauidevflow`, ensure the port matches between build and CLI.
   Run CLI from the project directory so it auto-detects the config file.
4. **Port already in use?** Another process may hold the port. Check with:
   ```bash
   lsof -i :<port>       # macOS/Linux
   ```
   With the broker, this is less common since ports are auto-assigned.
5. **Android?** Did you run `adb reverse tcp:19223 tcp:19223` (for broker) and
   `adb forward tcp:<port> tcp:<port>` (for agent)? Re-run after each deploy.
   If broker/list is empty, still try direct status:
   ```bash
   adb devices
   adb forward tcp:9223 tcp:9223
   maui devflow agent status --agent-host localhost --agent-port 9223
   ```
6. **Mac Catalyst?** Check entitlements include `network.server` (see setup.md step 5).
7. **macOS (AppKit)?** Ensure `AddMacOSEssentials()` is called and the app window appeared.
   See [references/macos.md](macos.md) for troubleshooting.
8. **Linux/GTK?** No special network setup needed — runs directly on localhost. Check if the app started successfully.
9. **Broker issues?** `maui devflow broker status` to check. `maui devflow broker stop` then
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

**SDK version mismatch:**
```
error : The current .NET SDK does not support targeting .NET 10.0
```
Fix: Install the required .NET SDK version, or check `global.json` for version pins.

**Android SDK not found:**
```
error XA0000: Could not find Android SDK
```
Fix: Install Android SDK via `android sdk install` or set `$ANDROID_HOME`.

**iOS provisioning / signing errors:**
Fix: For simulators, ensure no signing is configured (default). For devices, set up provisioning
profiles via `apple appstoreconnect profiles list`.

**General build failure recovery:**
1. `dotnet clean` then retry the build
2. Delete `bin/` and `obj/` directories: `rm -rf bin obj` then rebuild
3. Check the full build output (not just the last error) — earlier warnings often reveal the root cause

## CDP Not Connecting (Blazor Hybrid)

If `maui devflow webview status` fails but `ui status` works:

1. **Chobitsu not loading?** Check logs for `[BlazorDevFlow]` messages. If auto-injection failed, add `<script src="chobitsu.js"></script>` manually to `wwwroot/index.html`
2. **Blazor not initialized?** Navigate to a Blazor page first, then retry
3. Check app logs: `maui devflow ui logs --limit 20` — look for `[BlazorDevFlow]` errors

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
