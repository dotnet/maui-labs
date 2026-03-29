# Troubleshooting

## Table of Contents
- [Connection Refused](#connection-refused--cannot-connect)
- [Build Failures](#build-failures)
- [CDP Not Connecting](#cdp-not-connecting-blazor-hybrid)
- [Mac Catalyst Permission Dialogs](#mac-catalyst-repeated-permission-dialogs-on-rebuild)

## Connection Refused / Cannot Connect

If `maui-devflow MAUI status` fails with connection refused:

1. **App not running?** Verify the app launched: check the build output for errors.
2. **Check the broker:** Run `maui-devflow list` to see if the agent registered. If the list
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
6. **Mac Catalyst?** Check entitlements include `network.server` (see setup.md step 5).
7. **macOS (AppKit)?** Ensure `AddMacOSEssentials()` is called and the app window appeared.
   See [references/macos.md](macos.md) for troubleshooting.
8. **Linux/GTK?** No special network setup needed — runs directly on localhost. Check if the app started successfully.
9. **Broker issues?** `maui-devflow broker status` to check. `maui-devflow broker stop` then
   retry (CLI will auto-restart it).

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

If `maui-devflow cdp status` fails but `MAUI status` works:

1. **Chobitsu not loading?** Check logs for `[BlazorDevFlow]` messages. If auto-injection failed, add `<script src="chobitsu.js"></script>` manually to `wwwroot/index.html`
2. **Blazor not initialized?** Navigate to a Blazor page first, then retry
3. Check app logs: `maui-devflow MAUI logs --limit 20` — look for `[BlazorDevFlow]` errors

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
