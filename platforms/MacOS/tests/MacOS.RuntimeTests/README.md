# Shared AppKit runtime tests

This is the renamed/generalized `LayoutRegressionTests` application, not a second
test app. Portable `MacOS.Tests` remains a separate `net10.0` xUnit project.
Native scenarios share one executable, project, process bootstrap and CI runner.
Every launch selects exactly one registered scenario in a fresh process.

The dialog scenarios replace the standalone `AlertRegistrationProbe` app:
`dialog-registration` checks the actual MAUI-consumed subscription, AppKit proxy
and singleton registration across MAUI 10.0.41, 10.0.60, 10.0.70 and 10.0.110.
`dialogs` runs at 10.0.70, first restoring only the legacy registration logic to
prove the missing service in a real window, then verifying fixed native action
sheets, prompts and alerts with visible-sheet captures and typed task results.
A timeout is a failure, not accepted baseline evidence.

## Run on macOS

Use the repository SDK, macOS workload 10.0.203 and Xcode 26.3:

```sh
export NUGET_PACKAGES="$PWD/.packages"
./eng/common/dotnet.sh workload install macos --version 10.0.203
python3 -B platforms/MacOS/tests/MacOS.RuntimeTests/run.py \
  --scenario layout --evidence "$PWD/artifacts/layout-run"
```

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

Exceptions go to `context.Fail` (exit 1). A deliberately observed regression can
call `BaselineFailure(id, message)` (exit 42), after recording its concrete
predicate through `Assert`. A timeout is never a baseline success. The first
terminal result wins atomically; `result.json` and process exit must agree.
`WriteJson`, `AppendJson`, `EvidencePath` and `Capture` provide isolated evidence.
Native captures are composited onto white, not claimed as full-screen screenshots.
Call `Capture` and access native UI state only on AppKit's main thread, including
after asynchronous work. JSON file writers have no additional AppKit affinity.
`FlushMainQueueAsync()` drains two native dispatch turns, not a timed sleep.

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
- `runner.launch(output, stage, expectation=None, required_evidence=())` starts
  a fresh owned process, enforces deadlines, exit/result/counts and required files.
- `with runner.baseline_sources(): ...` applies/restores declared source overlays.
- `runner.command(args, log, timeout=600)` supports additional package/inventory
  stages with explicit failure and owned-process timeout cleanup.

Use unique stage names for clean/incremental/publish runs. A driver cannot finish
without a verified fixed launch; a declared baseline must also be verified.
Actual bundle inventories and stage-specific behavior assertions belong in the
scenario, not in a replacement probe project.
