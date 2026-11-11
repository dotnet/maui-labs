# Shared AppKit runtime tests

This is the renamed/generalized `LayoutRegressionTests` application, not a second
test app. Portable `MacOS.Tests` multi-targets `net10.0` and `net11.0`.
Native scenarios share one executable, project, process bootstrap and CI runner.
Every launch selects exactly one registered scenario in a fresh process.

The dialog scenarios replace the standalone `AlertRegistrationProbe` app:
`dialog-registration` checks the public typed subscription on MAUI 11 plus the
isolated AppKit compatibility proxy and singleton registration on MAUI 10.
`dialogs` runs at 10.0.70, first restoring only the legacy registration logic to
prove the missing service in a real window, then verifying fixed native action
sheets, prompts and alerts with visible-sheet captures and typed task results.
A timeout is a failure, not accepted baseline evidence.

The `shell-sections` baseline keeps `ShellHandler` and `ShellItemHandler` pinned
to `23ea45e98b84bfbf9dd00f4859fd4cf30d44120d`. Its `ShellSectionHandler` overlay
restores that revision's no-op `MapCurrentItem` with an exact replacement while
retaining the equivalent explicit parent traversal needed by MAUI 11, where the
old `FindParentOfType` extension is unavailable. The same concrete regression
sentinel and all 59 fixed assertions remain required.

| Scenario | Cases | Before/fixed behavior |
|---|---|---|
| `layout` | 6 | Exact missing native child failure / add, insert, replace, remove, clear, re-add |
| `contentview-clipping` | 19 | Missing bounds clip and transform redraw / clipping toggles, pan/zoom, translated fresh text, nested controls, Entry replacement, redraw cost, scale-only visibility and extent, native owner relayout, scaled collections, composite scroll cost and layout changes, Auto Layout toolbar geometry, autoresizing, scale/anchor resets, nonuniform scale, combined rotation/mirroring with translation, and zero/collapsed transitions (198 assertions) |
| `collection-view-grid` | 4 | Incorrect composed-card measurements and grid rows / tall cards, live Span changes, resize remeasurement, and spanning headers (8 assertions) |
| `shell-sections` | 1 | Lazy route target missing / section and content switching, dynamic insertion and handler lifecycle (59 assertions) |
| `dialog-registration` | 1 | Typed MAUI 11 subscription or isolated MAUI 10 proxy, with singleton registration |
| `dialogs` | 3 | Missing consumed subscription / native action sheets, prompts and alerts |
| `picker` | 19 | Unselected null-title Picker displays January / selection, title, items and native activation transitions |
| `tap-buttons` | 3 | Left click routed to secondary recognizer / primary, secondary and dynamic `Buttons` changes |
| `bundle-resources` | 4 | Linked image missing / processed resources, embedded fonts, view capture, and assets loaded from the app bundle |
| `media-capture` | 1 | Camera-equipped, pre-authorized runner records and verifies a non-empty native `.mov` before deleting it |
| `menu-localization` | 5 per launch | English defaults in a German app / native bundle localization, app overrides, localized app names, responder actions, custom-menu replacement and option resets |

The `picker` scenario covers the native state transitions of the AppKit Picker
handler; `bundle-resources` verifies MAUI resources in the packaged application.
`menu-localization` also consumes the actual NuGet packages. It launches fresh
processes with `-AppleLanguages` for German, French, Dutch, English and an unsupported
language, then verifies an English-only host under German preferences and a partial
host strings override, followed by default menus again to check resource isolation.
Custom English and localized Window menus must take over AppKit's native Window
role, not merely suppress the default menu's visible title; both replacements are
checked by native menu identity.
Set `RUNTIME_TEST_PACKAGES` to a directory containing the
backend and Essentials `.nupkg` files (default: `artifacts/runtime-packages`).

`media-capture` is not part of the hosted matrix because hosted macOS runners
have no camera. The conditional `camera-runtime` job requires a self-hosted
runner labeled `macOS` and `camera`, Xcode 27 selected, a connected camera, and
camera access already granted to `com.microsoft.maui.appkit.runtimetests`.
Enable it with the repository variable `MAUI_APPKIT_CAMERA_TESTS=1`. The
scenario clicks the native Stop button, verifies a non-empty movie, records
evidence, and deletes the captured file.

## Run on macOS

Use the repository .NET 11 SDK, its pinned macOS workload, and Xcode 27.0:

```sh
export NUGET_PACKAGES="$PWD/.packages"
./eng/common/dotnet.sh workload install macos --from-rollback-file eng/workloads/dotnet11-rc2.json
python3 -B platforms/MacOS/tests/MacOS.RuntimeTests/run.py \
  --scenario layout --evidence "$PWD/artifacts/layout-run"
```

The default run targets `net11.0-macos`. Passing a MAUI 10 version with
`--maui-version` selects `net10.0-macos`, keeping the release compatibility lane
without enabling any MAUI 10 private workaround in the .NET 11 build.
Select Xcode 26.0.x for that compatibility lane. Hosted CI uses `macos-26` with
Xcode 26.0.1 for MAUI 10 scenarios and `xcode-27` with Xcode 27.0 for MAUI 11
scenarios. Both use `eng/workloads/dotnet11-rc2.json`. The runner explicitly
enables Xcode validation, which matches the workload's required major/minor
version; a different major/minor is not accepted as a native pass.

The bundle-resource driver also checks real raw-asset processing separately
from synthetic processed-item metadata: explicit logical names win over source
links, default names remain filenames, and linked image paths are normalized
before resizing. These targets can be exercised without native linking, but
only the full driver verifies the packaged app, runtime fonts, screenshots,
incremental bundles and publish output.

Use a new evidence directory for each run. CI uses the `native-runtime` matrix in
`ci-macos-appkit.yml`. Python contract tests need no native SDK:

```sh
python3 -B -m unittest discover -s platforms/MacOS/tests/MacOS.RuntimeTests -p 'test_*.py'
```

The executable accepts `--list` (JSON metadata), or exactly
`--scenario <id> --evidence <directory>`. Unknown/empty selectors fail before
native startup. Duplicate registrations and non-positive expected counts fail.
Scenario ids start with a lowercase ASCII letter and contain only lowercase
ASCII letters, digits and hyphens.

## Add a scenario, not a project or job

Put a module and `scenario.json` in `Scenarios/<id>/`. A module initializer
registers **metadata and a factory only**, never an app/view before native init:

```csharp
[ModuleInitializer]
public static void Register() => ScenarioRegistry.Register(
    new("my-scenario", ExpectedCases: 1,
        CreateDelegate: context => new MyScenario().CreateDelegate(context),
        ExpectedAssertions: 59));
```

`RuntimeScenario` declares a positive `ExpectedCases`, optional exact
`ExpectedAssertions`, and `TimeoutSeconds` (default 90). Specify exactly one of
`CreateDelegate` or `RunManaged`. Managed-only callbacks skip AppKit initialization;
the host completes their result after the callback returns.

For real MAUI window scenarios derive a fixture from `MauiRuntimeScenario`:

- `Prepare(context)` executes after `NSApplication.Init` but before the native loop.
- `Configure(builder)` adds custom handlers/services before app creation.
- `CreateWindow(activationState)` supplies the real MAUI Window/Shell/page root.
- `Task RunAsync(context, window)` starts from `MacOSMauiApplication.OnStarted`.
  Await all queued work, including final disconnect assertions, before returning.

The shared adapter owns the MAUI Application/delegate, `UseMauiAppMacOS`,
Essentials registration, and completion. Advanced fixtures may instead return
their own `NSApplicationDelegate` (the existing layout fixture keeps that lifecycle)
or `MacOSMauiApplication`. They must call `context.Complete()` only after all work.
The host retains/disposes the delegate and owns native initialization and watchdog.

`context.Assert(condition, message, failureId)` records actual assertions.
`context.Pass(caseName)` records a unique completed case and requires new assertions
since the preceding case. `Complete` requires positive assertions and exactly the
declared case count, plus the exact assertion count when specified. Thus a
59-assertion fixture can declare one case, call `Assert` 59 times, then `Pass` once.
Layout retains six cases; case counts are not assertion counts.
Picker retains its 19 state-transition cases, `states.jsonl`, initial/final
native captures and the exact `BASELINE_563` sentinel. Its shared MAUI adapter
creates the same real window, rather than simulating a handler in isolation.
The Picker contract requires all 31 assertions. Its exit-42 baseline is deliberately
exact: the pinned handler displays January while the managed picker is unselected.
A different mismatch fails normally, not as a successful reproduction of #563.
The bundle-resources scenario verifies package-provided assets in the published
application bundle and requires all 26 assertions.

Exceptions go to `context.Fail` (exit 1). A deliberately observed regression can
call `BaselineFailure(id, message)` (exit 42), after recording its concrete
predicate through `Assert`. A timeout is never a baseline success. The first
terminal result wins atomically; `result.json` and process exit must agree.
`WriteJson`, `AppendJson`, `EvidencePath` and `Capture` provide isolated evidence.
Native captures are composited onto white, not claimed as full-screen screenshots.
Call `Capture` and access native UI state only on AppKit's main thread, including
after asynchronous work. JSON file writers have no additional AppKit affinity.
`FlushMainQueueAsync()` drains two native dispatch turns, not a timed sleep.

The `contentview-clipping` scenario uses `CaptureWindowBitmap`, a WindowServer
compositor capture, rather than `CacheDisplay` (which can force the very redraw
under test). It waits up to five seconds for a full-size window at nominal or
backing resolution and an independent
cyan text control in every capture; a locked display or invalid capture fails,
never counts as an exit-42 reproduction. Glyph assertions scan the expected text
regions. Ten out-and-back cycles alternate two strings with distinct glyph counts
to detect blank or stale text. Repeated zoom uses a short, fully visible string:
clipping a longer string can decrease its pixel count even when scaling works.
Zoom captures poll for a changed glyph mask with a bounded deadline while
retaining strict glyph-area growth and the independent visible-text control.
Changed capture dimensions are rejected, not treated as changed glyphs.
Nested ContentViews, Grid, Label and Entry are
followed by batches of 10, 100 and 500 three-property transform updates, with and
without 64 extra labels. `redraw-cost.jsonl` records timings, synchronous/settled
Label draws, draw depth and UI-thread ownership; timings are observations, not a
performance pass threshold. Native-positive scaling preserves logical Bounds
while transforming Frame, so AppKit's visibility calculations agree with the
rendered scale. No descendant traversal or forced synchronous display is needed.
Layer transforms retain rotation, collapsed/mirrored scales and externally
managed containers' existing behavior; those special paths are not evidence of
the cold-text positive-scale fix.

The item tests assert the entire anchored transformed frame, unchanged logical
size, scale and translation resets, native-owner relayout with an unchanged
transform, and a scaled CollectionView's logical item size and rendered text.
Native layout owners use `SetLayoutFrame` to pass logical rectangles through
the connected handler; layout sizing uses Bounds rather than the physical Frame.
Scaling legitimately changes the physical frame; an unchanged-frame
assertion would not test that implementation correctly. The final cases expose
a newly created off-window label by scale alone, verify its native VisibleRect
and glyph pixels, repeat exposure/reset, change anchors, and exercise independent
ScaleX/ScaleY updates. Nested Label and Entry rendering is also checked during
scale, not only after resetting it.
An autoresizing child must retain its logical frame across repeated scaling,
and ten compositor-paced scale updates record actual glyphs and draw counts in
`paced-cost.jsonl`, separately from coalesced update batches.
A cold coloured marker must occupy exactly its expected single-scale region
and area; Entry password/plain replacement must retain native geometry and text.

Combined positive scale, translation and a 180-degree rotation, followed by
mirrored scale with translation, must match an independently constructed,
layer-only native AppKit reference using an asymmetric two-color marker. Zero
scale and collapsed visibility must remove every marker pixel, then restore the
exact prior raster. These cases distinguish single scaling, orientation and
anchor placement from double scaling or misplaced translation; they do not
claim exhaustive 3D rotation coverage.

The combined marker also requires exact single-scale pixel areas and placement
in a 60x30-point region calculated independently from MAUI's untransformed
arranged rectangle. Transform updates occur after attachment/layout settles;
the unique yellow corner isolates zero-scale checks from the earlier lime marker.

The scenario requires all 198 assertions and 19 cases. Native scroll notifications
must not re-arrange unchanged composite item roots, while an ItemsLayout change
must reposition those same roots. Auto Layout-owned toolbar
content must retain its resolved Frame/Bounds while transformed and resized;
these externally owned views use the existing layer-only transform path.
The exact pre-fix overlay still exits 42
only for the original observed clipping plus translated-text failures; it stops
there and does not claim to reproduce the later scale/item cases.

## Runner manifest and baseline

The manifest's `name`, `expectedCases` and optional `expectedAssertions` must match
the compiled registration. `evidence` lists required nonempty files for both runs;
`fixedEvidence` adds success-only files. The runner also verifies `--list` and
unknown-selector rejection against the actual compiled host.

A `baseline` declares exact `exitCode`, `outcome`, `failureId` and `message`.
Source overlays accept either one exact `replace: {old, new}`, or `commit` (full
immutable SHA) plus `source`. Multiple production files can be listed in
`baseline.overlays`. Only production sources change: host and fixture stay
identical. Files are restored in `finally`, including after setup/build failures.
Missing result/evidence, arbitrary nonzero exits, or a different assertion fail CI.
Without `baseline`, a manifest runs only its fixed verification.

Add a row to the **existing** CI matrix with `scenario` and optional `maui-version`.
The runner passes `MicrosoftMauiControlsVersion` as a global MSBuild property; it
does not change central package versions or feeds.

## Assets, package consumers and staged scenarios

`RuntimeTestScenario=<id>` compiles only that module. Building without it registers
all modules. Optional `Scenarios/<id>/*.props` and `*.targets` imports scope fixture
assets/build behavior. Imports are limited to the module's root directory;
root-level imports may explicitly import nested fixture files. Do not change other
modules or copy shipping resources by hand.
Set `RuntimeTestsUseProjectReferences=false` for a package-consumer scenario
and supply its package references through the selected module's props, including
both backend and Essentials used by the shared bootstrap. Keep shipping NuGet
buildTransitive targets authoritative.

A manifest may specify a module-local Python `driver`. Its `run(runner)` composes
shared primitives instead of cloning bootstrap/build/CI code:

- `runner.build(stage, properties={}, publish=False, configuration="Debug",
  extra_args=(), app_root=None)` captures logs/binlog and returns `BuildOutput`
  (`executable`, actual `bundle`, `log`, `binlog`). Per-stage MSBuild overrides
  support package versions/sources and historical targets.
  The host records the Apple SDK's resolved `AppBundleDir` and `_NativeExecutable`
  after the requested Build or Publish target (not the intermediate Build within
  Publish); the runner owns that selector and requires fresh metadata and existing outputs.
  Publishing without an installer may retain the normal `.app` output location:
  the runner does not assume a `publish` directory or copy the bundle.
  Optional `app_root` constrains the reported bundle, rather than overriding discovery.
- `runner.launch(output, stage, expectation=None, required_evidence=())` starts
  a fresh owned process, enforces deadlines, exit/result/counts and required files.
- `with runner.baseline_sources(): ...` applies/restores declared source overlays.
- `runner.command(args, log, timeout=600)` supports additional package/inventory
  stages with explicit failure and owned-process timeout cleanup.

Use unique stage names for clean/incremental/publish runs. A driver cannot finish
without a verified fixed launch; a declared baseline must also be verified.
Actual bundle inventories and stage-specific behavior assertions belong in the
scenario, not in a replacement probe project.
