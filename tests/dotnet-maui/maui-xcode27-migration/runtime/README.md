# Xcode 27 lifecycle runtime qualification

This non-shipping **real MAUI iOS / Mac Catalyst app** supplements the adjacent
native `eval.yaml`; it is not an agent evaluator or a substitute for that eval.
No mock UIKit assembly, host-only callback compilation, agent package, test SDK,
auth credentials, or extra npm dependency is used. Only MAUI Controls 10.0.110 is
referenced. The local `Directory.Packages.props` imports repository central
packages and aligns Controls/Core/Essentials to 10.0.110; this is necessary
because the repository enables transitive pinning to its older MAUI release.

**Status: supplemental ordinary-iPhone startup verified; full runtime and
released-toolchain qualification remain blocked.** Public
release27.0.10722 needs SDK 10.0.401, workload 10.0.401.1, and Xcode 27.0.
The approved feeds currently lack the required released SDK package. A
10.0.402/custom-27.1 SDK with Xcode 27.1 beta can provide supplemental prototype
evidence only; a successful run on it cannot close the released qualification gate.
The fixture has compiled into a real ARM64 simulator `.app` using that existing
custom toolchain and cached MAUI 10.0.110 dependencies. This validates compilation
and native packaging. A subsequent dedicated iPhone 17 / iOS 27 run verified
window creation and both original activation effects. It exposed a real startup
crash: `UrlContexts` was null on a launch without cold payloads, despite the
binding annotation. Both cold collections are now guarded in the control and
observer. The corrected run recorded empty URL/activity payloads and reached
activation. This was a runtime-discovered defect, not just a formatting change.
Warm delivery stopped at the simulator's **Open in Lifecycle qualification?**
confirmation; the desktop safety engine could not arm its Escape handler, so
no automation action or workaround was attempted. Populated cold options,
injected contracts and the complete runtime subset have **not** passed.
Catalyst remains unbuilt (the provided supplemental SDK
has no Catalyst pack).
The compiled candidate is the **bundled reference control**, not either of the
earlier agent-generated migration artifacts. No result for this control
establishes that those artifacts compile or preserve their original behavior.

## Ownership and candidate boundary

`Candidate/MigrationCallbacks.cs` is the reference control, not a substitute for
qualifying generated migrations. The replaceable source contains the scene
overrides, per-scene queue, and the two custom lifecycle registrations.
`Harness/` contains immutable original side effects, assertions, app startup,
and real MAUI lifecycle observers. Do not let an evaluated agent edit it.
`OriginalEffects` retains the two distinct activation logs, false-returning
warm URL/activity observers, cold native title assignment, shortcut observer,
and `orders.open` decision from the adjacent custom-lifecycle scenario.

For a future migration task, give the agent a separate candidate directory and
the fixed public contract:

* `MyApp.ExistingSceneDelegate : MauiUISceneDelegate`, registered as
  `ExistingSceneDelegate`, with the actual scene callbacks.
* `CaptureCold(UIScene, IEnumerable<UIOpenUrlContext>, IEnumerable<NSUserActivity>)`
  is the extraction seam used **by the actual `WillConnect` override** and by
  the injected multi-item test. Keep its signature; its body is candidate-owned.
* `MyApp.MigrationRegistration.Configure(MauiAppBuilder)` installs the two
  custom shortcut registrations.
* Preserve calls to the matching immutable `OriginalEffects` bodies; do not
  replace them with empty methods or report their expected outcomes yourself.

The runner copies the selected candidate **byte-for-byte**, hashes it and the
test-owned files, and the app embeds that manifest. Runtime assertions exercise
that compiled candidate, not the control file left in this repository.
Preparation refuses to overwrite an earlier snapshot; a pre-compile
`verify-build` step and the runner reject source changes since preparation.
The pre-compile step also rejects compiling a different directory's bytes under
the selected manifest. Keep build logs alongside each snapshot.
Before any terminate, install, or launch command, the runner checks the app's
bundle identifier and compares its published `candidate-manifest.json`
(`BundleResource`, also read by the recorder) with that verified snapshot.
A wrong app or stale manifest is rejected before device mutation. Output paths
are checked against the canonical runtime directory and reject existing symlink
ancestors, including a linked `results/` directory.

This is a fixture-specific migration input contract, **not a transparent adapter
for arbitrary generated projects**. The current `native-audit-helper-*` artifacts
do not implement it and are intentionally untouched. Do not rewrite a failed
generated candidate to fit the harness and then report the original as passing.
A future native eval scenario can provide these fixed effect calls and seam in
its starting input; its resulting callback files can be compiled unchanged here.
Retain the adjacent eval's native assertions/rubric as the authoritative
assessment of the complete migration and the `WillConnect` wiring.

After that future native eval has produced a candidate against the fixed input
contract, the operator can bind its unchanged callback files to a runtime run:

```sh
node runner.mjs prepare --candidate /path/to/that/evals/callback-files \
  --candidate-kind agent-output --evaluation-id ACTUAL-NATIVE-EVAL-RUN-ID \
  --output results/agent-candidate
```

The manifest records this as an **operator provenance claim**, not proof that
the native evaluator passed. The bundled `Candidate/` defaults to
`reference-control`; other directories default to `unclassified`. Agent-output
claims require an evaluation ID. Preserve the native eval's original artifacts
and report separately; do not substitute the reference control or silently adapt
failed output. Immutable assertions can then check the compiled candidate's
effects and callback contracts, while native eval assertions/review must still
check the complete migration and actual connection-collection extraction.

## What each evidence class proves

| Evidence | Delivery / coverage |
| --- | --- |
| Initial launch, both activation effects, MAUI scene/window creation | OS callbacks in the real app |
| Two separate warm custom-scheme URLs | `simctl openurl` (iOS) / Launch Services `open` (Catalyst); observed in real scene callbacks |
| Cold custom-scheme URL after terminating the process | OS launch; must appear in **connection options** and perform title work in a new process, not merely arrive in a warm callback |
| Warm true/false results, URL and activity observers, base forwarding | **Injected callback contract** on a real MAUI scene; a test lifecycle registration supplies the base true/false result, not an auth provider |
| Multiple cold URLs/activities, queue retention with no window, separate drains, exactly once, window isolation | **Injected extraction contract** on two real MAUI window scenes, replaying the two captured OS URL objects plus real public `NSUserActivity` instances; windows are deliberately withheld/restored |
| Individual custom shortcut acknowledgements and aggregate completion | **Injected callback contract** using real `UIApplicationShortcutItem` objects and MAUI's real lifecycle registrations / `PerformAction`; each original synchronous handler must acknowledge once, with a bounded observation period |

There is no supported public constructor for `UIOpenUrlContext` or
`UISceneConnectionOptions`. The fixture deliberately does **not** fabricate
UIKit objects, use private KVC setters, or subclass fake connection options.
The multi-item test therefore calls the candidate's extraction seam rather
than pretending to be an OS `WillConnect` invocation. It proves queue behavior,
not native multi-context delivery or enumeration wiring. The OS cold single-URL
test does exercise actual `WillConnect`.

Remaining external gates are explicit in every report: released toolchain,
native multiple-connection contexts, actual associated-domain links/Handoff,
authentication returns, and OS-selected quick actions. Injected activity objects
are **not** authentication, universal-link, or Handoff evidence.

## Prepare and build (parent/operator owns provisioning)

Run from this `runtime/` directory, using an explicitly selected SDK and
`DEVELOPER_DIR` environment variable. Never change global Xcode selection.
Provision/restore dependencies separately with approval; the runner neither
installs SDKs/packages nor builds. Inspect the resolved Controls version and
toolchain in the retained build log.

```sh
set -o pipefail
node runner.mjs prepare --candidate Candidate --output results/control
# prepare prints absolute paths; substitute those exact values below.

# Use an already provisioned SDK and workload, approved feeds, and selected Xcode.
# Restore is a separate, explicitly approved step. Example ARM64 simulator build:
"$DOTNET" build LifecycleFixture.csproj -f net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64 \
  -p:CandidateDirectory="$PWD/results/control/candidate" \
  -p:CandidateManifest="$PWD/results/control/candidate-manifest.json" \
  2>&1 | tee results/control/build-ios.log

# Catalyst: use net10.0-maccatalyst and maccatalyst-arm64 on Apple Silicon.
# Use separate prepared snapshots per platform/candidate/toolchain.
```

For **supplemental custom 27.1 builds only**, the evaluation-only native API
marker requires `-p:NoWarn=XCODE_27_1_PREVIEW`. This does not disable
`ValidateXcodeVersion`, and must not be represented as released qualification.
The local supplemental check generated restore assets exclusively from the
existing package cache using a new empty local `--source` directory and
`-p:NuGetAudit=false`, then built with `--no-restore`. No packages were downloaded
or installed, and repository `NuGet.config` was not changed. That offline
compilation check is not a dependency-security audit.

Normal repository props/Arcade conventions and central packages are inherited.
Generated output is redirected beneath `runtime/artifacts/`; unrelated
`eng/Common.targets` DevFlow injection is intentionally not imported.
`ValidateXcodeVersion=true` overrides the repository's local-development opt-out.
Nothing is shipping or added to a product solution. Build output is ignored.

## Run on iOS

Use an **already booted, explicitly named** iPad simulator for the complete
two-scene contract. An ordinary iPhone can check startup/links but cannot be
claimed to pass the two-window gate. The runner fails rather than substitutes
two fake windows. Do not point it at a production app/device.

```sh
node runner.mjs run-ios \
  --udid EXACT-BOOTED-SIMULATOR-UDID \
  --app /absolute/path/to/LifecycleFixture.app \
  --prepared results/control \
  --build-log results/control/build-ios.log \
  --toolchain supplemental
```

The runner installs only this fixture app, launches it, sends warm URLs and
the contract trigger, terminates it, and sends the cold URL. It does not boot
simulators or change any toolchain. It reads the app's Documents JSONL via
`simctl get_app_container`. Each invocation writes a new result directory.
The app opens a second MAUI window itself for the isolation contract.
If the simulator asks to confirm opening a custom-scheme link, the operator must
approve that dialog through an authorized UI path while the runner waits.
`simctl openurl` succeeding does not prove delivery; a pending confirmation
causes the event wait to fail. Do not disable simulator safeguards to avoid it.

For a read-only bundle preflight, without a simulator or device commands:

```sh
node runner.mjs verify-app --prepared results/control \
  --app /absolute/path/to/LifecycleFixture.app --platform ios
```

Use `--platform maccatalyst` for the existing Catalyst bundle layout. Preflight
records the bundle identifier/version, supported platforms, SDK metadata,
executable hash, published manifest hash and a bundle file-inventory hash.
These checks detect accidental mismatches; **they do not establish cryptographic
authenticity of arbitrary operator-supplied code**. Keep bundles and prepared
directories unchanged between preflight and use.

## Run on Mac Catalyst

Build the Catalyst head into a separate snapshot. Close any existing fixture
instance. Locate its Documents directory from the app's
`QUALIFICATION_EVENTS=...` console line during a preliminary launch, then close
that preliminary instance. For sandboxed apps this is inside the app container;
do not assume it is the user's normal Documents directory.

```sh
node runner.mjs run-catalyst \
  --app /absolute/path/to/LifecycleFixture.app \
  --events /absolute/path/to/app/Documents/lifecycle-events.jsonl \
  --prepared results/catalyst \
  --build-log results/catalyst/build.log \
  --toolchain supplemental
```

The runner uses Launch Services for launch and URL dispatch, and terminates only
the PID recorded by the newly launched fixture. Signing/entitlements and window
permissions remain the parent's responsibility. Manual physical-device runs can
export the same app JSONL, but are not automated by this simulator runner.

## Results and safe local checks

`events.jsonl` preserves actual observations with process, sequence, scene,
window and `delivery` (`os` or `injected-contract`). `commands.json`, `build.log`,
and `report.json` preserve the external commands, supplied build evidence,
candidate hash and individual pass/fail checks. Missing/duplicate assertions,
timeouts, exceptions and changed build identities fail closed.
`app-preflight.json` and the runtime report retain the pre-mutation bundle
identity/hash evidence. OS URL checks correlate base delivery and original
effects by both process and scene, counting matching OS effects across the
whole process so duplicate delivery into another window cannot pass.
Reports explicitly retain candidate provenance, `nativeEvaluator: not-assessed`,
the extraction-method-only scope of multi-item injection, and the fact that
prior generated artifacts were not qualified by this run. A reference-control
subset pass must never be presented as an agent migration pass.

* Exit **1**: execution or runtime subset failed.
* Exit **2**: runtime subset passed, **full qualification remains blocked**.
* Exit **0** from `prepare` or unit tests does **not** mean runtime qualification.

`--toolchain` is an operator claim retained as such, not automatic verification.
No report closes external gates simply because that flag says `released`.

```sh
node --check runner.mjs
node --test assertions.test.mjs runner.test.mjs
```

These are host tests of the report validator, not UIKit/device tests. Actual
device results must come from executing the app. To test a failing mutation,
prepare a new candidate snapshot (for example, remove the application activation
call, drop a queued item, or complete an observer twice) and require a failed
runtime report. Never patch an existing snapshot after seeing its result.

Signature references: tagged MAUI
[`10.0.110/MauiUISceneDelegate.cs`](https://github.com/dotnet/maui/blob/10.0.110/src/Core/src/Platform/iOS/MauiUISceneDelegate.cs)
and macios
[`dotnet-10.0.1xx-xcode27.0-10722/src/uikit.cs`](https://github.com/dotnet/macios/blob/dotnet-10.0.1xx-xcode27.0-10722/src/uikit.cs).
