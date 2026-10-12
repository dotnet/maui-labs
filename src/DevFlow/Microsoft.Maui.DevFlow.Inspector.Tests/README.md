# Inspector browser tests

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

All **13** fixtures must execute and pass: the original eleven Layout workspace checks,
cached Layout → Logs → Layout Data attachment, and sizing-only `diagnosticInputsRevision`
invalidation/Live rescanning with unchanged geometry.

`ci-devflow.yml` provisions Chromium in its dedicated `inspector-browser` Windows job and
sets `MAUI_INSPECTOR_BROWSER_TESTS=1`. That job requires exactly 13 passed results and fails
if any filtered test is skipped; adding another fixture requires updating the explicit count.
`InspectorBrowserFact` skips these fixtures only in ordinary GitHub Actions/Azure Pipelines
jobs without that opt-in, where Playwright browsers are not provisioned. Local execution
does not require the opt-in; a missing Chromium installation fails with Playwright's guidance.

The remaining `LiveInspectorFact` tests still require a running broker/native app and
`MAUI_INSPECTOR_LIVE_TESTS=1`; they are not included in the browser fixture job.
