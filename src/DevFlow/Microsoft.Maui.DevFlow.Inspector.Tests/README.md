# Inspector browser tests

Two separate deterministic suites exercise the shipped Inspector. Neither enables
the opt-in native-app journeys.

## Layout workspace and shared UI synchronization

`InspectorLayoutWorkspaceTests` runs deterministic Chromium regressions without a broker or
native app. A loopback fixture agent supplies the initial tree/screenshot; scenario-specific
HTTP/WebSocket responses are intercepted by Playwright. The page and all JavaScript modules
come from the real `InspectorServer` and the CLI's embedded resources, not copied HTML or scripts.
Each test records the Chromium version and loaded modules and rejects browser page errors.

From the repository root, using the SDK pinned by `global.json`:

```powershell
dotnet build src\DevFlow\Microsoft.Maui.DevFlow.Inspector.Tests\Microsoft.Maui.DevFlow.Inspector.Tests.csproj --configuration Release
& .\artifacts\bin\Microsoft.Maui.DevFlow.Inspector.Tests\Release\net10.0\playwright.ps1 install chromium
dotnet test src\DevFlow\Microsoft.Maui.DevFlow.Inspector.Tests\Microsoft.Maui.DevFlow.Inspector.Tests.csproj `
  --configuration Release --no-build --no-restore `
  --filter "FullyQualifiedName~InspectorLayoutWorkspaceTests" `
  --logger "console;verbosity=detailed" --logger "trx;LogFileName=layout-browser.trx" `
  --results-directory artifacts\TestResults\InspectorBrowser
```

All **18** fixtures must execute and pass: the original eleven Layout workspace checks,
cached Layout → Logs → Layout Data attachment, and sizing-only `diagnosticInputsRevision`
invalidation/Live rescanning with unchanged geometry, two queued Live cancellation checks,
and three property/Network synchronization checks.
Disabling Live cancels queued automatic rescans while preserving an explicit refresh requested
during an active scan; it does not abort the in-flight scan.

The read-only property-filter cases cover descriptor and legacy property loading while the
mutation lease is held elsewhere. The Network case covers out-of-order lists, a late automatic
poll during detail loading, disabled attachment until detail completion, and paused list polling
while detail stays open. Response gates hold each loading state until asserted; Playwright's
clock drives the real polling intervals instead of wall-clock sleeps.

`ci-devflow.yml` provisions Chromium in its dedicated `inspector-layout-browser` Windows job and
sets `MAUI_INSPECTOR_BROWSER_TESTS=1`. That job requires exactly 18 passed results and fails
if any filtered test is skipped; adding another fixture requires updating the explicit count.
It also repeats all three synchronization cases five times, failing immediately on any failure
or skip. The Windows live integration job enables these browser fixtures alongside the native
journeys.
`InspectorBrowserFact` skips these fixtures only in ordinary GitHub Actions/Azure Pipelines
jobs without that opt-in, where Playwright browsers are not provisioned. Local execution
does not require the opt-in; a missing Chromium installation fails with Playwright's guidance.

The remaining `LiveInspectorFact` tests still require a running broker/native app and
`MAUI_INSPECTOR_LIVE_TESTS=1`; they are not included in the browser fixture job.

## Native Layout QA journey

`LayoutBaselineFindings_RespectOutcomeFilterAndHighlightParent` is specific to the
DevFlow.Sample **MauiTodo Layout QA** app. General live-test opt-in does not enable it.
After starting that QA app, explicitly select its agent port:

```powershell
$env:MAUI_INSPECTOR_LAYOUT_QA_TESTS = '1'
$env:LAYOUT_QA_AGENT_PORT = '<actual QA agent port>'
dotnet test src\DevFlow\Microsoft.Maui.DevFlow.Inspector.Tests\Microsoft.Maui.DevFlow.Inspector.Tests.csproj `
  --configuration Release --no-build --no-restore `
  --filter "FullyQualifiedName~LayoutBaselineFindings_RespectOutcomeFilterAndHighlightParent"
```

The journey verifies the app identity before navigation or scanning, creates its own real
InspectorServer targeting that same agent port, and deliberately ignores `INSPECTOR_URL`
(which may target a different app). Preparation releases the direct client's mutation lease
in `finally`, including navigation/scan/assertion failures. The browser journey requires
Playwright Chromium; it is not synthetic native validation.

The separate `LayoutQaSessionTests` verifies the opt-in, wrong-app rejection, real Inspector
target isolation, and lease claim/release HTTP roundtrips using the existing loopback fixture
agent, without Chromium or a native app:

```powershell
dotnet test src\DevFlow\Microsoft.Maui.DevFlow.Inspector.Tests\Microsoft.Maui.DevFlow.Inspector.Tests.csproj `
  --configuration Release --no-build --no-restore --filter "FullyQualifiedName~LayoutQaSessionTests"
```
## HTTP and property browser coverage

`Category=BrowserFixture` runs headless Chromium against the **real InspectorServer
HTTP listener and shipped Inspector JavaScript**. Each test has an isolated loopback
agent HTTP fixture, fresh browser, and dynamically allocated ports. No broker,
`broker.json`, global environment changes, MAUI workload, emulator, or native app
is required.

Coverage includes runtime property writes and rejection, source-button
clean/pending/dirty states, selection preservation after real state refresh,
property filtering, and numeric validation. Only the agent's controlled responses
are mocked; browser requests are not intercepted and HTML is not injected.
Teardown stops browser timers and prevents a late pagehide keepalive request, then
drains HTTP traffic before disposing the
Inspector and awaiting the agent server's shutdown.
The only intercepted browser request is one held `/api/state` response, which keeps
a refresh in flight while a tap lands.

From the repository root (PowerShell):

```powershell
dotnet build src\DevFlow\Microsoft.Maui.DevFlow.Inspector.Tests -c Release
& artifacts\bin\Microsoft.Maui.DevFlow.Inspector.Tests\Release\net10.0\playwright.ps1 install chromium
$env:DEVFLOW_INSPECTOR_BROWSER_FIXTURES = "1"
dotnet test src\DevFlow\Microsoft.Maui.DevFlow.Inspector.Tests -c Release --no-build --filter "Category=BrowserFixture"
```

The `inspector-browser` job in `ci-devflow.yml` installs Chromium with its Linux
dependencies, explicitly enables these fixtures, and runs only this category.
Without `DEVFLOW_INSPECTOR_BROWSER_FIXTURES=1`, ordinary library builds skip this
category with a provisioning explanation. When enabled, a missing browser is a
test failure, not a skip.

`InspectorPageTests` remain separately opt-in via `LiveInspectorFact` and
`MAUI_INSPECTOR_LIVE_TESTS`. They require a provisioned broker and connected app.
These browser fixtures **do not prove native gesture injection, platform rendering,
device source-map generation, or native menu invocation**.
