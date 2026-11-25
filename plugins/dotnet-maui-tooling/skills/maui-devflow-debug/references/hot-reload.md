# .NET 11 Hot Reload with DevFlow

Load this when iterating on a .NET 11 MAUI app with `dotnet watch`, when an edit
did not show up in the running app, or when a watcher behaves unexpectedly.
`SKILL.md` has the launch commands and project prerequisites.

## Why it matters

Measured on `DevFlow.Sample` (one existing-label XAML edit, verified through a
DevFlow element query, median of 4 edits):

| Target | `dotnet watch` per edit | Rebuild + relaunch per edit |
|---|---|---|
| Mac Catalyst | ~1 s | ~8 s |
| iOS Simulator | ~0.7 s | ~20 s |
| Android (Pixel 8 Pro, USB) | ~0.6 s | ~35 s |

The watched app keeps its process, navigation, and input state, so one
verification query replaces a relaunch, a `maui devflow wait`, and re-navigating
to the screen under test.

## Edit loop with `maui devflow watch`

`maui devflow watch` owns the `dotnet watch` process for you: it runs in the
background, keeps its output in a log, and turns that output into one short
result per edit, so you never need to read raw watcher output.

```bash
# Once: build, launch, and wait until the app is ready (reuses a running session)
maui devflow watch start --project <path/to/App.csproj> -f net11.0-android --device <device-id> --json

# Each edit: save the file, then
maui devflow watch wait --json && maui devflow ui query --automationId <id>

# Unsupported edit, or the app exited
maui devflow watch restart --json

# Done
maui devflow watch stop
```

- `start` runs from the project directory (or `--project <path>`), forwards
  `--property Name=Value` to `dotnet watch`, and blocks until the app is ready.
  Run it in the foreground; it detaches the watcher itself.
- `wait` reports the result of your latest save: `applied` (with `elapsedMs`),
  `no-changes`, `compile-error` (with `diagnostics`), `restart-required`,
  `process-exited`, `failed`, `watch-exited`, or `timeout`. It exits non-zero
  for anything except `applied` and `no-changes`, so `wait && <verify>` only
  verifies a live update. Call it right after saving; it allows for the
  watcher noticing the save late. Use `--since <cursor>` to wait for the next
  result after a known event.
- `status` and `list` show the session state without waiting; `stop` ends the
  watcher and the app it launched.
- MCP agents use the same flow through `maui_watch_start`, `maui_watch_wait`,
  `maui_watch_status`, `maui_watch_restart`, and `maui_watch_stop`.

`applied` means the update reached the app, not that the UI shows what you
intended. Still query the changed element (see below).

## Edit loop with raw `dotnet watch`

Use this when `maui devflow watch` is not available in the installed CLI.

1. Start the watcher once, in the background, from the app project directory.
2. `maui devflow wait`, then record the app process id from
   `maui devflow agent status --json` (`app.processId`).
3. Edit source. Read the watcher output for the result of that edit.
4. Query the affected element or property, for example
   `maui devflow ui query --automationId <id>`, and compare with the edit.
5. Check `app.processId` again. Same id means the edit was hot-reloaded; a new
   id means the watcher restarted the app.

## Reading raw watcher output

`maui devflow watch wait` already classifies these lines; this table is for raw
`dotnet watch` output.

| Watcher says | Meaning | Next step |
|---|---|---|
| `File updated: …` then `C# and Razor changes applied in <n>ms.` or `[<project> (<tfm>)] Updates applied.` | Update applied in-process | Verify through DevFlow |
| `Restart is needed to apply the changes.`, rude edit, or an `ENC*` diagnostic | Not hot-reloadable | Let it restart (or press Ctrl+R / restart it), `maui devflow wait`, verify |
| Compilation error | Nothing applied | Fix the code; the app still runs the old code |
| `[<project> (<tfm>)] Exited …` | App crashed or was stopped | Read the watcher log; on Mac Catalyst check `~/Library/Logs/DiagnosticReports/` |

`Updates applied` is a detail-level message (🕵️) and may not appear in every
output mode; when it is absent, the DevFlow query in step 4 is the authority.
Add `--verbose` to see the `ENC*` reason behind an unexpected restart.

A C# edit changes the code, not state that the old code already produced. If
the edited method ran before the edit (a constructor, `OnAppearing`, a one-time
`UpdateX()`), trigger it again (tap, navigate, or change data) before querying.
Drive multi-step triggers through one `maui devflow batch` call so the steps
share a mutation lease.

Usually in-place: method bodies, existing XAML properties/elements on
source-generated XAML pages. Usually restart: new types or members in some
cases, signatures, project/TFM/package/manifest/resource changes.

## Lifecycle gotchas

- **Only one watcher per project.** A leftover `dotnet watch` rebuilds and
  relaunches the app on every edit, competing with the new one.
  `maui devflow watch start` reuses a matching session; with raw watchers,
  check `ps -axo pid,command | grep dotnet-watch` before starting.
- **Stopping a raw watcher:** with redirected input, `dotnet watch` ignores
  SIGINT and SIGTERM. `maui devflow watch stop` handles this (it stops the app
  launchers first, then the watcher); otherwise kill the watcher by PID and
  stop the app separately.
- **Mac Catalyst outlives the launcher.** `dotnet run` and the watcher launch
  the app with `open`; stopping the watcher may leave the app running. Quit it
  explicitly before the next session.
- **Stale broker entries.** `maui devflow list` can briefly show agents from
  apps that already exited. Trust `agent status` / `app.processId`, not list
  order.
- **Two adb transports for one phone** (USB plus wireless debugging) make the
  CLI skip Android port forwarding. Set `ANDROID_SERIAL=<serial>`; see
  `connectivity.md`.

## Do not

- Do not rebuild and relaunch after every edit while a working watcher runs.
- Do not count a successful build, a connected agent, or a screenshot as proof
  that the edit applied; query the changed element.
- Do not use `maui devflow ui set-property` to "test" a source change; it
  changes the live object, not the source.
