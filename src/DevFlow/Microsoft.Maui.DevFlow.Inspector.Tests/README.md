# Inspector browser coverage

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
