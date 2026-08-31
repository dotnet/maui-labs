# DevFlow platform flow-QA handoff

`eng/devflow/Run-DevFlowFlowQa.ps1` and `Run-DevFlowFlowQa.sh` are repeatable host
entry points for collecting DevFlow flow-QA artifacts. They use the SDK selected by
the repository `global.json`; they do **not** install workloads, Xcode, Appium, SDKs,
or device tools.

The PowerShell entry point requires PowerShell 7.3 or later and is always invoked as
`pwsh ./eng/devflow/Run-DevFlowFlowQa.ps1`; the shell entry point requires `bash`. The same
minimum applies to the other checked-in DevFlow engineering scripts:
`Finalize-DevFlowFlowPilotManifest.ps1`, `Measure-ReplayStability.ps1`, and
`Publish-DevFlowFailureIssue.ps1`.

Each invocation requires an exact, repository-local results path:

```text
artifacts/TestResults/devflow-flow/<platform>
```

It writes the corresponding run root under
`artifacts/devflow/<run-id>/<platform>/`, including `manifest.json`, `flow-run.json`,
bounded redacted host diagnostics, and failure evidence when the platform runner can
produce it. Existing run roots are never cleaned or overwritten.

> A dry run validates arguments and prints a planned command only. It is not a
> platform execution or a passing result.

## macOS prerequisite matrix

This is the single checked-in prerequisite matrix for the Apple handoff. It is aligned with
`global.json`, `.github/workflows/_build.yml`, and
`.github/workflows/devflow-integration.yml`; it is a static setup check, **not** a macOS runtime
claim.

| Lane | Repository SDK | Pinned workload manifest | Xcode/runtime | Required host tools |
|---|---|---|---|---|
| iOS Simulator | `10.0.301` | `10.0.203` | Xcode `26.3`; an available iOS Simulator runtime | `xcodebuild`, `xcrun`, `python3`, `openssl`, `zip` |
| Mac Catalyst | `10.0.301` | `10.0.203` | Xcode `26.3` | `xcodebuild`, `xcrun`, `python3`, `openssl`, `zip` |
| Experimental AppKit | `10.0.301` | `10.0.203` | Xcode `26.3` / macOS SDK | `xcodebuild`, `xcrun`, `python3`, `openssl`, `zip` |

The `10.0.301` SDK and `10.0.203` workload manifest are intentionally different. The SDK is
selected by the repository's `global.json`; `10.0.203` is the separately pinned MAUI workload
version used by CI to prevent workload drift. Do **not** replace the workload pin with the SDK
version.

On the macOS QA host, from the repository root:

```bash
dotnet --version                    # must resolve to 10.0.301
dotnet workload install maui ios macos maccatalyst tvos android wasm-tools --version 10.0.203
dotnet workload list
xcodebuild -version                 # Xcode 26.3
xcrun --sdk macosx --show-sdk-path
command -v python3 openssl zip
python3 --version
openssl version
```

The scripts never install these prerequisites. Missing `python3` prevents simulator metadata
selection; missing `openssl` prevents generation of the ephemeral authenticated-transport secret
and fails closed.

## Android and Windows local commands

From the repository root on Windows:

```powershell
pwsh .\eng\devflow\Run-DevFlowFlowQa.ps1 `
  --platform android `
  --repeat 3 `
  --results-root .\artifacts\TestResults\devflow-flow\android

pwsh .\eng\devflow\Run-DevFlowFlowQa.ps1 `
  --platform windows `
  --repeat 3 `
  --results-root .\artifacts\TestResults\devflow-flow\windows
```

Android uses the existing `Category=FlowPilot` fixture and lifecycle host. Windows uses the
dedicated `Category=WindowsFlowQa` host, not the broad `Category=Device` integration suite and
not the experimental WPF backend. The Windows host runs the shared Tier-1 corpus three clean
times per flow by default, preserves each flow's immutable first attempt in
`windows-tier1-manifest.json`, and invokes the exact public `MauiFlowRunner`; the script does not
multiply those three fixture-owned resets into nine replays.

The Windows fixture builds the Debug `net10.0-windows10.0.19041.0` sample with
`DevFlowIntegrationTest=true`, terminates only the exact process it started, launches with a
known DevFlow port and seed, and uses the Debug-only sample extension for reset/seed fingerprints.
It never clears an arbitrary user profile, package-data directory, or app storage location.
Before replay it verifies the app/package/build/process agent, route/window/modal checkpoint,
locale/theme/orientation/display profile, and seed/backend fingerprints. A missing native-dialog
or WebView capability is reported as `capability-missing`, never hidden by a weaker selector
fallback.

Windows prerequisites are a Windows host with the repository-selected .NET SDK, MAUI Windows
workload, Windows App SDK/WinUI runtime, and WebView2 when the WebView contract is enabled.
The current QA process must run in an **active, unlocked desktop session**. The Windows fixture
checks the current process session ID, WTS connection state, and lock state; it does not treat
`Environment.UserInteractive` as sufficient. A disconnected RDP session, locked desktop, or
unavailable WTS result fails closed as an `infrastructure-failure` (PowerShell exit code `3`)
before the WinUI process or any flow replay starts. The lane never migrates or reconnects a
session and never substitutes WPF or a headless backend for the official Windows MAUI contract.

After signing in to and unlocking the active desktop, rerun exactly:

```powershell
pwsh .\eng\devflow\Run-DevFlowFlowQa.ps1 `
  --platform windows `
  --repeat 3 `
  --results-root .\artifacts\TestResults\devflow-flow\windows
```

`--no-build` only passes `--no-build` to the test host; lifecycle fixtures may still build the
app they own. Use `--flow-filter <VSTest-filter>` to further narrow a platform's fixed filter.

### Android ADB fixture-initialization failures

If Android fixture initialization stops before a flow can replay, inspect:

```text
artifacts/devflow/<run-id>/android/host-diagnostics/fixture-initialization.json
```

The bounded redacted record identifies the lifecycle phase, failed ADB action/category, exit
code, timeout/cancellation state, and a capped safe error chain. It is declared and hashed in
`manifest.json`; qualification records it as a fixture-initialization infrastructure exclusion.
It deliberately excludes the device serial, full environment, command line, and uncapped command
output.

For an ADB transport error such as `adb protocol fault (couldn't read status length)`, repair the
host or emulator first. On the QA host, an operator may run:

```powershell
adb kill-server
adb start-server
adb devices
```

Then rerun the Android command above with a new run ID. The flow-QA scripts never run
`adb kill-server` or `adb start-server` automatically, and they do not retry or launch a second
flow attempt after fixture initialization fails.

For safe command inspection:

```powershell
pwsh .\eng\devflow\Run-DevFlowFlowQa.ps1 `
  --platform android `
  --repeat 3 `
  --results-root .\artifacts\TestResults\devflow-flow\android `
  --no-build `
  --dry-run
```

## macOS handoff

Run these commands on the macOS QA host, after workloads and Xcode have been
provisioned by the host owner:

```bash
REPO=/absolute/path/to/maui-labs

bash "$REPO/eng/devflow/Run-DevFlowFlowQa.sh" \
  --platform ios \
  --apple-spike \
  --ios-runtime 18.x \
  --repeat 3 \
  --qualification \
  --results-root "$REPO/artifacts/TestResults/devflow-flow/ios"

bash "$REPO/eng/devflow/Run-DevFlowFlowQa.sh" \
  --platform maccatalyst \
  --apple-spike \
  --repeat 3 \
  --qualification \
  --results-root "$REPO/artifacts/TestResults/devflow-flow/maccatalyst"

bash "$REPO/eng/devflow/Run-DevFlowFlowQa.sh" \
  --platform macos \
  --experimental \
  --repeat 3 \
  --results-root "$REPO/artifacts/TestResults/devflow-flow/macos"
```

The `macos` command is an **experimental AppKit** lane. It targets
`samples/DevFlow.Sample.MacOS/DevFlow.Sample.MacOS.csproj` and its dedicated
`maui-tests/` corpus. It is not Mac Catalyst coverage and never substitutes for an official Mac
Catalyst result.

For iOS Simulator and Mac Catalyst, `--qualification` invokes the local, read-only
`maui devflow flow qualify` adapter after the manifest exists. Its Apple projection records
foreground/auth/receipt/cancellation/parity, Xcode/runtime/device/seed facts, digests, attempts,
artifacts, and omissions. The current Android preview policy still reports Apple evidence as
advisory rather than turning it into an Android or physical-device qualification.

The experimental AppKit script deliberately rejects `--qualification`; run its separate,
read-only evidence mapping after the script instead:

```bash
dotnet run --project "$REPO/src/Cli/Microsoft.Maui.Cli/Microsoft.Maui.Cli.csproj" \
  -f net10.0 --configuration Debug -- \
  devflow flow qualify \
  --platform macos \
  --corpus "$REPO/tests/DevFlow/InspectorCorpus" \
  --artifact-manifest "$REPO/artifacts/devflow/<run-id>/macos/manifest.json" \
  --output "$REPO/artifacts/devflow/<run-id>/macos/qualification.json" \
  --json
```

The native AppKit fixture builds that Debug-only test app, terminates only the exact process it
started, launches it with a known DevFlow port, uses the test-build-only in-memory reset/seed
extension, rejects a stale agent instance, and verifies the bundle/build fingerprint, route,
window, locale, theme, display profile, and seed/backend fingerprints before the canonical
`MauiFlowRunner` can replay a flow. It captures bounded redacted process logs, matching AppKit
crash reports when available, host diagnostics, and cleanup facts. It never deletes a user
container, Keychain item, or arbitrary macOS state.

The small AppKit Tier-1 corpus covers stable managed/native identities, Shell navigation, the
in-app modal equivalent, and native Button/Entry handlers. WKWebView/CDP runs only if the running
agent advertises `agent.webview`; native system dialogs and multi-window automation are explicitly
reported as unsupported rather than treated as Mac Catalyst parity.

## Apple XCTest proof-of-architecture

The checked-in Apple agent is a guarded **XCTest/XCUITest QA lane**, not a claim of iOS or
Mac Catalyst support. It keeps the target foreground through `XCUIApplication`, while the exact
public `Microsoft.Maui.DevFlow.Testing.MauiFlowRunner` remains on the macOS host. Each non-dry-run
official Apple invocation establishes the capability proof first and then runs the Tier-1 corpus.
`--apple-spike` makes that prerequisite explicit. The script builds the instrumented sample when
`--target-app` is omitted; a supplied app must match the exact target bundle ID.

Run the exact commands, prerequisites, and artifact-return procedure in
[Apple XCTest flow proof](apple-xctest-spike.md). The command produces
`artifacts/devflow/<run-id>/<platform>/apple-xctest-spike.json`; it is nonzero/pending unless
foreground ownership, authenticated transport, receipt/cancellation, parity, and bounded
artifact return are all proven on the macOS QA host.

## Physical iOS is unavailable pending a signed-device harness

Physical iOS is **not available for qualification yet**. The repository does not currently have
a signed-device install/launch/reset/Test Agent harness. The guarded
`--physical-device` path only validates the protected input shape and reports
`pending-spike`/`capability-missing`; it does not install, drive, or certify a physical device.
Do not return its output as physical-iOS evidence.

When a signed-device harness is implemented, it will require a trusted device identifier,
signing identity, provisioning profile, and keychain reference from secured host configuration.
Passwords, tokens, certificates, profile contents, and raw identifiers must never be committed
or included in return artifacts. **Simulator evidence never certifies physical iOS.**

## Returning artifacts

The script prints its `<run-id>`. Return both the TRX directory and its matching
run root. On macOS:

```bash
cd "$REPO"
zip -r "devflow-flow-qa-<run-id>-ios.zip" \
  artifacts/TestResults/devflow-flow/ios \
  artifacts/devflow/<run-id>/ios

zip -r "devflow-flow-qa-<run-id>-maccatalyst.zip" \
  artifacts/TestResults/devflow-flow/maccatalyst \
  artifacts/devflow/<run-id>/maccatalyst

zip -r "devflow-flow-qa-<run-id>-macos-appkit-experimental.zip" \
  artifacts/TestResults/devflow-flow/macos \
  artifacts/devflow/<run-id>/macos
```

The AppKit ZIP remains separately labeled and advisory; it does not certify Mac Catalyst.

Do not make the ZIP executable or unpack it into a source/workspace directory. Verify it directly
on the receiving host; this reads only the bounded allowlist, validates manifest-declared hashes,
and does not extract, replay, persist raw bytes, or grant proposal authority:

```bash
dotnet run --project "$REPO/src/Cli/Microsoft.Maui.Cli/Microsoft.Maui.Cli.csproj" \
  -f net10.0 --configuration Debug -- \
  devflow evidence verify-apple-qa \
  "$REPO/devflow-flow-qa-<run-id>-ios.zip" \
  --json
```

To create only fresh, memory-only **untrusted** diagnostic projections for compatible per-flow
`flow-run.json` and `.mauitrace` entries, add `--import-diagnostics`:

```bash
dotnet run --project "$REPO/src/Cli/Microsoft.Maui.Cli/Microsoft.Maui.Cli.csproj" \
  -f net10.0 --configuration Debug -- \
  devflow evidence verify-apple-qa \
  "$REPO/devflow-flow-qa-<run-id>-ios.zip" \
  --import-diagnostics \
  --json
```

The verifier rejects traversal, duplicate, symbolic-link/reparse-point, oversized, excessive,
and decompression-bomb entries; it requires every non-manifest returned file to have a matching
manifest hash. An extracted return directory is supported only when it preserves the ZIP's
top-level `artifacts/TestResults/...` and `artifacts/devflow/...` layout. Imported IDs use the
isolated `imported-artifact` namespace and remain `untrusted`; a new matching local reproduction
is required before any future repair or source proposal policy can consider them.

On Windows:

```powershell
Compress-Archive `
  -Path .\artifacts\TestResults\devflow-flow\windows, .\artifacts\devflow\<run-id>\windows `
  -DestinationPath .\devflow-flow-qa-<run-id>-windows.zip
```

Keep the manifest, `flow-run.json`, `apple-xctest-spike.json`, `apple-flow-qa.json`,
`qualification.json` when requested, `.trx`
files, `.mauitrace` failure evidence if available, and `host-diagnostics/` together. The manifest records artifact hashes
and explicit omissions so an absent trace is not mistaken for a passing flow.

Both entry points write those facts once per pass and publish them identically in
`manifest.json` and `flow-run.json`: the same `omissions` array, and an `artifactSummary` of
`maxArtifacts`, `recordedArtifacts`, `omittedArtifacts`, and `truncated`. Artifact references are
repository-relative, sorted, and never include the manifest itself, whose bytes are not final
until after the digests are taken. Every digest and the size beside it come from one read of the
file, so a file rewritten between passes is never published with a new hash beside an old length.
`omittedArtifacts` counts every reference excluded from the published inventory - the cap, anything
that could not be hashed, anything outside the repository, anything reached through a link, and one
per directory that could not be enumerated (`artifact-enumeration`, a floor rather than a
measurement) - while `truncated` stays the narrower fact it names: the cap, and only the cap, was
reached. A consumer can therefore tell a complete inventory from an incomplete one without deriving
it.

On Android the shared `manifest.json` is the flow-pilot manifest the test process writes and
`Finalize-DevFlowFlowPilotManifest.ps1` completes, so its inventory is the finalizer's own: it
publishes the same four-field `artifactSummary` over the artifacts it recorded, and its
`omissions` are the pilot manifest's, not the script's. The finalizer re-hashes every inherited
reference whose file is still present under an accepted artifact root - digest and size come from
one read of the file, so a file rewritten between passes is never published with a new hash beside
an old length - while the producer's own account of the evidence, its `kind`, `mediaType`, and
`redacted` flag, is carried through untouched. A reference it cannot see at all is carried over and
reported as `artifact-inherited` rather than presented as verified. A directory it could not
enumerate is reported as `artifact-enumeration` and counted, never treated as empty. Finalizing the
same tree again restates the finalizer's own omissions and counters - each one it writes carries
`source: "finalizer"`, so the omissions the test process recorded about references that never
reached disk are kept rather than dropped - instead of adding to the previous pass's. A `runId` is recorded only when the first directory below the artifact root
matches a run the manifest or a discovered report named and is a bounded plain identifier
(`[A-Za-z0-9][A-Za-z0-9._-]*`, at most 96 characters), so run-independent evidence such as
`host-diagnostics/summary.json` carries none; the test-process producer applies the same rule, so
the two never disagree about the same file. When finalization fails, the entry point preserves the
unfinalized pilot manifest as `manifest.unfinalized.json`, publishes it as an artifact, writes its
own generic manifest in place of the shared one, and records the `shared-manifest` omission - with
the preserved path - in both `manifest.json` and `flow-run.json`. The pilot evidence is never
destroyed to make room for the fallback.

A failing attempt is classified from structured run evidence whenever the host recorded any. The
free-text fallback is the last resort, and it matches only anchored phrases a failing host actually
prints (`emulator: ERROR`, `device ... not found`, `did not become ready`, and the like). Bare
words such as "emulator" or "timeout" are deliberately not markers: an ordinary line naming the
emulator, or a flow assertion that timed out, must not turn a product defect into an
infrastructure failure that nobody has to fix. Both entry points use the same marker list.

For Windows, also retain:

```text
artifacts/devflow/<run-id>/windows/windows-tier1-manifest.json
artifacts/devflow/<run-id>/windows/<flow>-attempt-<n>/flow-run.json
artifacts/devflow/<run-id>/windows/<flow>-attempt-<n>/windows-host-diagnostics.json
artifacts/devflow/<run-id>/windows/host-diagnostics/windows-session.json
```

The script-owned `manifest.json` and `flow-run.json` summarize the invocation; the Tier-1
manifest is the per-flow first-attempt and repetition record.
`windows-session.json` contains only the process session ID, WTS/lock states, bounded admission
result, timestamp, and reason; it does not include a username or raw `quser` output.

For the experimental AppKit lane, retain:

```text
artifacts/devflow/<run-id>/macos/manifest.json
artifacts/devflow/<run-id>/macos/flow-run.json
artifacts/devflow/<run-id>/macos/apple-flow-qa.json
artifacts/devflow/<run-id>/macos/appkit-tier1-manifest.json
artifacts/devflow/<run-id>/macos/appkit-capabilities.json
artifacts/devflow/<run-id>/macos/<flow>-attempt-<n>/flow-run.json
```

Every experimental AppKit manifest carries `experimental: true`, `backend: "appkit"`,
`officialCoverage: false`, and `macCatalystEquivalent: false`. Those labels are required even
when the lane is pending because a host capability is unavailable.

## Status interpretation

Apple source alone is not a runtime capability. Until the guarded macOS
[`--apple-spike`](apple-xctest-spike.md) report is `proved`, iOS Simulator, Mac Catalyst, and
experimental AppKit runs write a nonzero `pending-spike` / `capability-missing` or
`proof-incomplete` result. That is not a test failure and is never a pass.

Both entry points report the same `capability.state` for the same checkout, and neither treats
checked-in source as a capability report:

| `capability.state` | Meaning |
|---|---|
| `planned` | A non-Apple lane whose platform fixture is present. |
| `proof-required` | The Apple agent source is checked in, so the runtime proof is required and has not been established in this invocation. |
| `pending-spike` | No Apple agent source is present, so the spike itself is still pending. |

Once the AppKit proof and fixture capabilities are present, `--platform macos --experimental`
executes the separate AppKit corpus and produces experimental artifacts. A successful AppKit run
remains advisory and does not qualify Mac Catalyst or the official all-platform MAUI gate;
`--qualification` is intentionally rejected for this lane.

`flow-failure` means a launched supported host returned a failing test result.
`infrastructure-failure` and `prerequisite-missing` cover missing host tools,
workloads, emulator/simulator/device readiness, or fixture setup. With
`--qualification`, Android/Windows `not-qualified` evidence returns nonzero; the Apple adapter
is intentionally advisory and does not relabel a completed Apple runtime attempt. An emulator or
simulator pilot does not claim real-device or physical-device qualification.

`capability-missing` returns the pending exit code and is an explicit incomplete Windows platform
contract result. It is not converted into a pass or into WPF coverage.
