# Apple XCTest/XCUITest flow QA handoff

> **Experimental proof of architecture — not Apple platform support.** This document describes
> the user-run macOS evidence required before iOS Simulator, Mac Catalyst, or the separately
> labeled AppKit fixture can be described as
> runtime-proven. Windows compilation, package builds, and script dry runs are not runtime proof.

## Architecture decision

The initial plan proposed loading `Microsoft.Maui.DevFlow.Testing.MauiFlowRunner` inside a managed
XHarness/XCTest process. That is not viable with the toolchain currently checked into this
repository:

- The only XHarness use is the conditional `DeviceRunners.XHarness.*` device-test runner in
  `tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/`. It runs its own managed test application;
  it does not expose a managed XCUITest API that drives a separately foregrounded target app.
- Apple's XCUITest model does provide a separate native test runner and target process:
  [`XCUIApplication`](https://developer.apple.com/documentation/xctest/xcuiapplication) launches,
  activates, and observes the target application while the XCTest bundle remains separate.
- Loading the managed runner into the native XCTest bundle would require an unsupported custom
  .NET runtime host and would still not provide the repository's required target-foreground
  ownership through the managed XHarness lane.

The safer architecture is therefore used for this spike:

```text
macOS host
  Microsoft.Maui.DevFlow.Testing.MauiFlowRunner   <-- the only semantic engine
      AppleTestAgentMauiFlowDriver
          authenticated loopback command transport
              native XCTest/XCUITest operation agent
                  XCUIApplication(target bundle ID) owns target foreground
```

`Microsoft.Maui.DevFlow.TestAgent.Core` contains only provider-neutral identity, authenticated
operation command, receipt, cancellation, capability, error, and bounded artifact contracts.
The native agent maps direct operations to XCTest accessibility APIs and, only when explicitly
configured, forwards route/theme/property operations to the in-app DevFlow endpoint. It has no
flow parser, selector/actionability policy, repair, source, plan, replay, or `MauiFlowRunner`.

## What the proof checks

The macOS host starts `Microsoft.Maui.DevFlow.TestAgent.Host`, which generates an ephemeral HMAC
session secret in memory. The shell script does not print or persist it. The XCTest bundle:

1. launches the exact approved bundle ID with `XCUIApplication`;
2. asserts the target is foreground;
3. authenticates to the loopback host with timestamped, nonce-protected HMAC requests;
4. services a tree, query, and one explicitly approved safe accessibility action;
5. returns a command receipt, performs a cancellation probe, and returns a bounded screenshot in
   authenticated chunks;
6. lets the macOS host run the exact public `MauiFlowRunner` canonical assertion flow; and
7. reads the test-build-only, value-free seed/backend/route checkpoint before replay; and
8. records a normalized report-parity digest against a canonical fixture.

The command reports `proved` only when every one of those checks succeeds. Any missing Xcode
tool, runtime, built target, failed foreground assertion, failed authentication, incomplete
receipt/cancellation, parity mismatch, or absent artifact returns nonzero and a
`pending-spike`/`proof-incomplete` result.

## Prerequisites on the separate macOS QA host

Use the [single Apple prerequisite matrix](flow-qa.md#macos-prerequisite-matrix) for the pinned
SDK/workload/Xcode combination. Provision these first; the repository scripts never install them:

- Xcode with an installed iOS Simulator runtime and command-line tools selected;
- the .NET SDK and MAUI Apple workloads already required by this checkout;
- `python3` for simulator/device metadata selection and `openssl` for the ephemeral authenticated
  transport secret (both are required and fail closed when absent);
- an available iPhone Simulator runtime (the script selects and boots one when no UDID is passed);
- a known safe accessibility action. The checked-in sample exposes `AutomationId="AddButton"`.

Physical iOS is unavailable/pending until a signed-device install/launch/reset/Test Agent harness
exists. Simulator proof never certifies physical iOS. Experimental AppKit uses its own
`com.companyname.mauitodo.appkit` bundle, fixture, corpus, and `backend=appkit` artifacts; it is
never a substitute for Mac Catalyst. When `--target-app` is omitted, the script builds the Debug
`DevFlowIntegrationTest` sample target itself before building/running the native XCTest bundle.

## Exact iOS Simulator command

Do not put a secret in any command. The script generates the ephemeral session secret itself,
proves the XCTest capability first, and then runs every Tier-1 flow three clean times through the
host-side public runner.

```bash
REPO=/absolute/path/to/maui-labs

bash "$REPO/eng/devflow/Run-DevFlowFlowQa.sh" \
  --platform ios \
  --apple-spike \
  --target-bundle-id com.companyname.mauitodo \
  --safe-action-id AddButton \
  --apple-spike-timeout 180 \
  --repeat 3 \
  --qualification \
  --results-root "$REPO/artifacts/TestResults/devflow-flow/ios"
```

For Mac Catalyst:

```bash
REPO=/absolute/path/to/maui-labs

bash "$REPO/eng/devflow/Run-DevFlowFlowQa.sh" \
  --platform maccatalyst \
  --apple-spike \
  --target-bundle-id com.companyname.mauitodo \
  --safe-action-id AddButton \
  --apple-spike-timeout 180 \
  --repeat 3 \
  --qualification \
  --results-root "$REPO/artifacts/TestResults/devflow-flow/maccatalyst"
```

For the experimental native AppKit fixture:

```bash
REPO=/absolute/path/to/maui-labs

bash "$REPO/eng/devflow/Run-DevFlowFlowQa.sh" \
  --platform macos \
  --experimental \
  --apple-spike \
  --target-bundle-id com.companyname.mauitodo.appkit \
  --safe-action-id AddButton \
  --apple-spike-timeout 180 \
  --repeat 3 \
  --results-root "$REPO/artifacts/TestResults/devflow-flow/macos"
```

The AppKit lane also runs the fixture-owned lifecycle test after the XCTest operation-agent
corpus. It verifies only the process it launched and emits `appkit-tier1-manifest.json` and
`appkit-capabilities.json`; unavailable WKWebView/CDP, NSAlert, or multi-window support remains
an explicit capability result, not a parity claim.

Use `--dry-run` to inspect the host command without launching Xcode. A dry run is not evidence.

## Optional macOS-gated xUnit wrapper

The script is the preferred handoff. On a provisioned Mac, the environment-gated wrapper runs the
same iOS command and verifies the returned proof/QA JSON. It is intentionally a no-op on Windows:

```bash
export DEVFLOW_RUN_APPLE_XCTEST_SPIKE=1
export DEVFLOW_APPLE_SPIKE_TARGET_APP=/absolute/path/to/DevFlow.Sample.app
export DEVFLOW_APPLE_SPIKE_TARGET_BUNDLE_ID=com.companyname.mauitodo
export DEVFLOW_APPLE_SPIKE_SIMULATOR_ID=<booted-simulator-udid>
export DEVFLOW_APPLE_SPIKE_SAFE_ACTION_ID=AddButton

dotnet test "$REPO/src/DevFlow/Microsoft.Maui.DevFlow.Tests/Microsoft.Maui.DevFlow.Tests.csproj" \
  --filter FullyQualifiedName~AppleXCTestSpikeEnvironmentTests
```

## Expected evidence and return package

The script prints a run ID. For a proved iOS or Mac Catalyst run, preserve:

```text
artifacts/devflow/<run-id>/ios/apple-xctest-spike.json
artifacts/devflow/<run-id>/ios/apple-xctest-host-ready.json
artifacts/devflow/<run-id>/ios/apple-flow-qa.json
artifacts/devflow/<run-id>/ios/apple-flow-runs/
artifacts/devflow/<run-id>/ios/host-diagnostics/
artifacts/devflow/<run-id>/ios/manifest.json
artifacts/devflow/<run-id>/ios/flow-run.json
artifacts/TestResults/devflow-flow/ios/
```

`apple-xctest-spike.json` contains foreground ownership, authenticated transport facts, the
macOS-host runner type/version, metadata-only command receipt, cancellation certainty, parity
and report digests, and artifact hashes. `apple-flow-qa.json` retains the Tier-1 clean attempts
and capability-honest WebView, repair/source, security/privacy, and report-parity outcomes. The
manifest records Xcode/runtime/device data, redacted signing references when applicable, Testing
and Test Agent versions, and proof outcomes. None contain the HMAC secret, authorization values,
raw UI values, or raw screenshot content.

Return the matching directories together:

```bash
cd "$REPO"
zip -r "devflow-apple-flow-qa-<run-id>-ios.zip" \
  "artifacts/TestResults/devflow-flow/ios" \
  "artifacts/devflow/<run-id>/ios"
```

For Mac Catalyst, replace both `ios` path segments with `maccatalyst`. For AppKit, use `macos`
and include `appkit-tier1-manifest.json` and `appkit-capabilities.json`. Every AppKit manifest
must state `experimental: true`, `backend: "appkit"`, `officialCoverage: false`, and
`macCatalystEquivalent: false`.

Do not report iOS Simulator, Mac Catalyst, or experimental AppKit runtime success until the
returned spike report has `status: "proved"` and its artifacts are reviewed. An AppKit result
remains advisory and does not qualify Mac Catalyst.

Verify the returned ZIP directly rather than extracting or executing it:

```bash
dotnet run --project "$REPO/src/Cli/Microsoft.Maui.Cli/Microsoft.Maui.Cli.csproj" \
  -f net10.0 --configuration Debug -- \
  devflow evidence verify-apple-qa \
  "$REPO/devflow-apple-flow-qa-<run-id>-ios.zip" \
  --import-diagnostics \
  --json
```

The command accepts only the documented return tree and manifest-hashed metadata. It imports
compatible per-flow reports/traces as isolated `untrusted` diagnostics only; it does not extract,
execute, replay, retain raw input, or authorize a repair/source proposal. A new local reproduction
remains required before a proposal can be considered.
