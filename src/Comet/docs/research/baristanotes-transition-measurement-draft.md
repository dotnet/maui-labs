# BaristaNotes transition measurement draft

## Status

This is an experimental draft preserved for documentation and possible future
investigation. The companion patch records the incomplete source exactly as it
existed when preserved:

`src/Comet/docs/research/baristanotes-transition-measurement-draft.patch`

`BARISTA_MEASUREMENT` is not wired into any project or active build
configuration. Normal builds do not define it, do not use this measurement
path, and are unchanged by these documentation artifacts.

The draft depends on the `Barista.Measurement` and
`Barista.Measurement.AndroidSupport` protocol sources. Those sources are
external to this repository and are not included in the patch. Enabling the
symbol without first supplying the reviewed protocol sources is expected to
fail compilation.

The accepted transition measurement results are documented elsewhere. This
draft is not the canonical results record and should not be used to replace or
reinterpret those results.

## Preserved scope

The patch contains only the current changes from these paths:

- `src/Comet/sample/CometComposeProbe/BaristaNotes/Comparison/BaristaFixtureHost.cs`
- `src/Comet/sample/CometComposeProbe/BaristaNotes/Comparison/BaristaMeasurement.cs`
- `src/Comet/sample/CometComposeProbe/BaristaNotes/Comparison/MeasurementControlReceiver.cs`
- `src/Comet/sample/CometComposeProbe/MainActivity.cs`
- `src/Comet/sample/CometComposeProbe/MainActivity.Measurement.cs`
- `src/Comet/sample/Shared/BaristaNotes/BaristaNotesApp.cs`
- `src/Comet/sample/Shared/BaristaNotes/Comparison/ComparisonPage.cs`
- `src/Comet/sample/Shared/BaristaNotes/Components/ActivityShotRow.cs`
- `src/Comet/sample/Shared/BaristaNotes/Pages/ActivityFeedPage.cs`
- `src/Comet/sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs`
- `src/Comet/src/Comet/Backend/View.Backend.cs`
- `src/Comet/src/Comet/Platform/Compose/ComposeDrawObservation.cs`
- `src/Comet/src/Comet/Platform/Compose/ComposeLeafNodes.cs`
- `src/Comet/src/Comet/Platform/Compose/ComposeNode.cs`
- `src/Comet/src/vendor/Microsoft.AndroidX.Compose/DrawAcknowledgment.cs`
- `src/Comet/src/vendor/Microsoft.AndroidX.Compose/Java/DrawGeometry.java`
- `src/Comet/src/vendor/Microsoft.AndroidX.Compose/Microsoft.AndroidX.Compose.csproj`

## Review blockers

The draft must not be activated until all of these blockers are resolved:

1. Recover the exact external protocol source version and review its ownership,
   licensing, serialization contracts, rejection codes, and Android support
   behavior before adding it to any build.
2. Review the metric contract end to end: monotonic start and end points,
   content-readiness rules, draw-versus-frame-commit semantics, callback
   ordering, cancellation, timeout handling, duplicate events, and retention
   of rejected or failed trials.
3. Review lifecycle and concurrency ownership around
   `BaristaMeasurement.Current`, activity teardown, repeated composition,
   observer registration, subscriptions, and callbacks that may outlive their
   page or native peer.
4. Security-review the exported Android broadcast receiver, its
   `android.permission.DUMP` protection, every accepted action and input, and
   the result-export path. The control surface must remain unavailable in
   ordinary application builds.
5. Add focused tests for content qualification, row identity, fully-visible
   geometry, clipping, repeated draws, stale revisions, cancellation, error
   export, and receiver rejection behavior.
6. Validate the Java bridge and JNI peers across clean Android builds, Native
   AOT, trimming, release packaging, supported Android versions, and repeated
   process launches.
7. Confirm that the conditional framework hooks add no behavior, packaging, or
   API exposure when `BARISTA_MEASUREMENT` is absent.
8. Complete an independent code review of the restored patch and protocol
   sources before collecting or publishing any new measurements.

## Steps required before activation

1. Restore the patch on a clean branch and import the reviewed external
   protocol sources without changing their contracts.
2. Introduce a dedicated, opt-in measurement build configuration that defines
   `BARISTA_MEASUREMENT`; do not add the symbol to normal product builds.
3. Resolve the security, lifecycle, metric-contract, JNI, Native AOT, and
   trimming blockers above.
4. Add and run focused automated tests, then verify that an ordinary build
   remains behaviorally and structurally unchanged.
5. Build the dedicated configuration from a clean tree and run controlled
   device trials using the approved fixture, route definitions, sample
   retention rules, and percentile calculations.
6. Compare exported evidence with the separately documented accepted results
   and investigate discrepancies rather than overwriting the accepted record.
7. Obtain independent review approval before considering any product-code
   activation or permanent integration.
