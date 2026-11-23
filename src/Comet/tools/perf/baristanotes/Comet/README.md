# Comet fixture adapter

This is opt-in, unmeasured comparison support. It is not a benchmark runner.
The shared contract in `../Shared` is compiled once by the small Contract
project. This directory does not define another fixture format. No NuGet
package or app dependency version is changed.

`CometFixtureAdapter` creates records through the existing Barista services.
It checks operation results and keeps native IDs as invariant strings.
Readback opens a fresh SQLite store and uses the existing list, by-ID and
history APIs without replacing the active app service locator. List and by-ID
values are compared. Unknown IDs fail. No SQL insertion, row deletion,
fixture seed removal, or cache replacement is used.

Supporting-entity checks compare each list row with the result fetched for
that exact native ID, before canonical sorting. They check every common
fixture field, including null versus empty notes. Sorting the final arrays
must not hide swapped by-ID responses.

Every unmeasured readback constructs a new SQLite store and new domain,
preference, range and theme readers. It does not use the writer's entity
snapshot, range cache, app theme signal or service locator. Normal app
caching is unchanged.

Readback refuses a non-null `GetSettings().LoadWarning` before it reads the
fixture entities. Malformed or unsupported persisted range settings must not
pass as valid Auto modes. Normal Settings fallback and caching are unchanged.
Refusing completed validation does not repair the bad settings or rewrite
the complete receipt and readback files.

Selection and adapter identity checks use the same `BaristaAppStorage`
path resolver, including symbolic-link ancestors. The adapter opens that
resolved database and compares it with the resolved writer path. A valid
non-default alias is accepted; matching file names in different directories
are not. Default-database alias rejection and the exclusive live-store guard
remain in effect.

Timestamps are UTC, roast dates are calendar dates, and the shared
`FixtureUtc` rule is used for persisted timestamps. Null preinfusion, rating
zero and empty accessories stay distinct. Preferences use the selected
database, including Light theme, Celsius, Auto ranges and recent selections.
Activity All is checked from the new source page, not written as a preference.

## Build

Add this argument to the existing comparison publish command:

```sh
-p:BaristaFixtureInputs=/absolute/path/to/fixtures-v2
```

Both approved files must be present. The app verifies their exact shared
SHA-256 values before parsing or provisioning. Both profiles include the same
input assets and adapter. Default probe builds and other samples do not
include the contract, assets or registered fixture receiver. The JNI receiver
type remains visible to every configuration so Android wrapper generation
does not depend on a Debug-only type.

## Android control

Only comparison manifests export
`com.comet.baristacomparison.FixtureControlReceiver`, in the separate
`:fixturecontrol` process. It requires the Android `DUMP` permission.
The selector checks namespace, count, mode and existing evidence before it
writes selection metadata. It refuses a running main process, failed or
incomplete namespaces, orphaned external preferences, and missing completed
databases/readbacks. It cannot reset or repair a namespace.

Comet comparison theme preferences are in SQLite, not an Android XML
preferences file. The database, WAL, SHM and rollback journal are inside
the namespace directory. An existing directory without a complete receipt
is refused before opening SQLite or reserving a receipt, even if it contains
only a sidecar or backup file. This also preserves recoverable orphaned
data; the selector does not attempt recovery.

Use only an allocated device, the verified APK and an approved app stop/launch
path. Replace `SERIAL` explicitly; these examples do not grant device access.
Stop only the owned comparison package before changing selection.

```sh
PACKAGE=com.comet.sample.perf.diag.baristanotes
adb -s "$SERIAL" shell am broadcast --user 0 -f 0x20 \
  -n "$PACKAGE/com.comet.baristacomparison.FixtureControlReceiver" \
  -a com.comet.baristacomparison.SELECT \
  --es namespace fixture-v2-100-comet01 --ei count 100 --es mode provision
```

Require broadcast result `-1` and `Selection saved`. Stop the owned control
process with the package's approved stop command. Launch the already installed
APK, without a build. Selection is read before voice, photo, theme or Barista
services resolve. Missing selection, or explicit `namespace=review, count=0,
mode=run`, preserves normal review storage and its normal first-run sample.

New provisioning starts once, after the normal app/database/page setup.
Domain mutations run on the activity thread; parsing and independent readback
do not. An existing complete namespace is validated without create/preference
calls or changes to its receipt/readback. On activity replacement, the host
waits for the operation and asynchronous voice/service teardown before creating
a replacement root against the same process selection.

```sh
adb -s "$SERIAL" shell am broadcast --user 0 -f 0x20 \
  -n "$PACKAGE/com.comet.baristacomparison.FixtureControlReceiver" \
  -a com.comet.baristacomparison.STATUS
```

Require `status.runState=RanToCompletion`, no error, and a `complete` shared receipt
before using a provisioned or validated namespace. `Faulted`, an unresolved
pending operation or an incomplete receipt means stop. Do not retry it.
Status is durable, not proof that the main process or UI is currently running.
Check app identity and UI separately with DevFlow in the diagnostic profile.

Select the same namespace with `mode=validate` and launch a fresh process to
check persistence. `mode=run` does **not** load input definitions, import data,
or run full readback during startup; its status is `RunSelected`, not a
correctness result. Normal selected database/service/theme/page loads remain.

Explicit unmeasured readback is also available while a completed namespace is
selected:

```sh
adb -s "$SERIAL" shell am broadcast --user 0 -f 0x20 \
  -n "$PACKAGE/com.comet.baristacomparison.FixtureControlReceiver" \
  -a com.comet.baristacomparison.EXPORT
```

Require result `-1` and `Exported <token>`. This opens a fresh domain scope,
checks actual data against the exact input and completion receipt, and writes
a new canonical export under `files/barista-comparison-control/exports`.
It does not return cached fixture input or modify the namespace evidence.
Files remain app-private. Retrieve bounded chunks, including on Release APKs:

```sh
adb -s "$SERIAL" shell am broadcast --user 0 -f 0x20 \
  -n "$PACKAGE/com.comet.baristacomparison.FixtureControlReceiver" \
  -a com.comet.baristacomparison.READ_EXPORT \
  --es token TOKEN --ei offset 0 --ei length 16384
```

Require result `-1`. Decode `base64` and append chunks in `offset` order until
`totalBytes` is reached. Every chunk has the whole-file `sha256`; it must remain
constant and match the assembled bytes and receipt semantic hash. A successful
startup operation also supplies `status.readbackToken`, so another export is
optional. Tokens are fixed lowercase GUIDs scoped to the selected namespace;
arbitrary paths and ranges are refused. No public-storage or default-data
export is provided.

These controls are app-owned Android operations. They are not original-app
extension routes, shared DevFlow endpoints, or timing markers.

### Diagnostic activity recreation

Only the diagnostic comparison manifest registers `BaristaLifecycleReceiver`.
It runs in the main app process and requires Android's shell `DUMP` permission.
Timing and normal profiles have no registered lifecycle receiver or active
control. The JNI receiver class remains visible in all configurations so
Android can generate its Java wrapper.

After the diagnostic app is active and fixture work has finished, read its live
identity. This does not load fixture definitions or read the database:

```sh
adb -s "$SERIAL" shell am broadcast --user 0 -f 0x20 \
  -n "$PACKAGE/com.comet.baristacomparison.BaristaLifecycleReceiver" \
  -a com.comet.baristacomparison.LIFECYCLE_STATUS
```

Require result `-1`, the expected `processId` and selected `databasePath`, and
`canRecreate=true`. Copy the returned `activityId` and logical Comet `rootId`
into one request:

```sh
adb -s "$SERIAL" shell am broadcast --user 0 -f 0x20 \
  -n "$PACKAGE/com.comet.baristacomparison.BaristaLifecycleReceiver" \
  -a com.comet.baristacomparison.RECREATE_ACTIVITY \
  --ei pid PID --es activityId ACTIVITY_ID --es rootId ROOT_ID
```

The receiver calls Android `Activity.Recreate()` on the main thread. It refuses
missing or stale identities, inactive windows, pending or failed fixture work,
and repeated requests to the same activity. Exceptions are logged and returned
as result `0` with `Rejected`; no automatic retry occurs.

The returned `recreateRequested=true` means a request was made, **not** that
recreation completed. Read status again and require the same PID/database,
different activity/root IDs and `canRecreate=true`. Correlate the PID-scoped
`CometLifecycleControl` logs with DevFlow tree, PixelCopy and real navigation.
Check selected data, theme and recent preferences through the existing controls
and UI. A process restart does not satisfy this gate. The existing root
ownership mechanism still awaits old services and voice teardown before it
constructs a new root; this control does not change that mechanism.

## Host validation

Rebuild the normal Comet host DLL before tests because the host suite uses a
HintPath:

```sh
cd src/Comet
dotnet build src/Comet/Comet.csproj -f net11.0-maccatalyst
dotnet test tests/Comet.Tests/Comet.Tests.csproj
dotnet build tools/perf/baristanotes/Comet/HostProof/CometFixture.HostProof.csproj
```

Run the proof executable in a **new process for each command**:

```sh
dotnet tools/perf/baristanotes/Comet/HostProof/bin/Debug/net11.0/CometFixture.HostProof.dll \
  /absolute/fixtures-v2 /absolute/new-host-evidence fixture-v2-100-comet01 provision
```

Repeat for `validate`, `run`, and completed `provision` (read-only validation),
then use a separate `fixture-v2-1000-comet01` namespace. This checks actual
persisted fields, relationships, IDs, counts, rating-zero/null semantics,
settings, history/latest100 and unchanged default database bytes. A default
theme adapter that throws proves that fixture operations never use that key.
Run mode asserts zero input loads. Validation also checks unchanged completed
database, receipt and readback bytes.

`CometFixtureReadbackTests` also rejects swapped entity responses and each
common-field mismatch. A real SQLite test changes persisted data/settings
through a second set of domain services while the writer retains its old
entity and range caches. Readback must see the new data without refreshing
those caches and must work after the writer is disposed. Refusal tests
preserve a live SQLite WAL with a stored theme, plus isolated sidecar files.

The host proof executable also has three correction checks. Each provisions
a real completed fixture from the exact approved 100-record input and checks
validation without rewriting the receipt, readback or default database:

```sh
dotnet tools/perf/baristanotes/Comet/HostProof/bin/Debug/net11.0/CometFixture.HostProof.dll \
  /absolute/fixtures-v2 /absolute/new-malformed-proof fixture-v2-100-malformed check-malformed-ranges
dotnet tools/perf/baristanotes/Comet/HostProof/bin/Debug/net11.0/CometFixture.HostProof.dll \
  /absolute/fixtures-v2 /absolute/new-schema-proof fixture-v2-100-unsupported check-unsupported-ranges
dotnet tools/perf/baristanotes/Comet/HostProof/bin/Debug/net11.0/CometFixture.HostProof.dll \
  /absolute/fixtures-v2 /absolute/new-alias-proof fixture-v2-100-alias check-aliased-namespace
```

Use a new proof directory for each invocation. The first two retain the
deliberately bad JSON and require explicit validation refusal. The third
provisions through a non-default symbolic-link ancestor and validates through
both the alias and the physical path. These are host test commands, not
additional app selection modes.

Host proof and compilation do not establish Android fixture-data or UI
acceptance. Device tests must use the published APK after ownership is granted.
