# DevFlow maintained sample

This experimental .NET 10 MAUI app exercises native automation and the shared
DevFlow WebView bridge. Build it with the matching Agent, WebView, Blazor, Client
and CLI versions from this repository. Debug builds register the agent and both
WebView adapters; Blazor registration already includes the generic adapter, so
the explicit generic registration also exercises idempotency.
The optional Shell partial hook adds these pages only where their source is
compiled. Backend samples that link the shared Shell keep their existing routes;
this change does not add backend handlers or claim backend runtime coverage.

| Shell route | Maintained fixture |
|---|---|
| `//native` | Native todo controls and screenshots |
| `//blazor` | Real Blazor todo events and `/counter` routing |
| `//webview` | Standard WebView loading packaged HTML, input events, one-click count, console and controlled DOM defects |
| `//hybrid` | Actual `MauiAsset` resources, original Hybrid raw JSON messages in both directions, relative navigation and reload |
| `//mixedwebviews` | All three hosts together: unnamed standard host and intentionally duplicate Hybrid/Blazor AutomationIds |

Use the native restore/recreate buttons to reset fixtures. The standard fixture
has a pink background; Hybrid has a green background. Both include a 60px
ellipsis and an 80x80 child clipped by a 40x40 parent (75% area loss). The mixed
page also has a native 80x80 label clipped by a 40x40 grid, so disabling DOM layout
inspection can be checked without disabling native diagnostics.

Standard inline HTML is self-contained. Its relative link is exercised only
after the integration test navigates that host to its owned loopback HTTP server:
navigation from `HtmlWebViewSource` to cache files is platform-restricted.
Hybrid relative links use the real packaged resources and MAUI resource/message
infrastructure. Tests restore standard HTML **before** disposing their HTTP
fixture; the sample never needs a test server after a test ends.

## Run and inspect

```bash
dotnet build samples/DevFlow.Sample/DevFlow.Sample.csproj -f net10.0-maccatalyst -c Debug
# Launch the resulting MauiTodo.app, then select its actual agent port:
maui devflow --agent-port <port> agent status
maui devflow --agent-port <port> webview webviews
maui devflow --agent-port <port> webview --context-id webview-<index> source
```

Context IDs come from the live contexts response; do not hard-code indexes or
use AutomationIds as browser selectors. Hidden Shell pages may retain registered
hosts, but only the active page participates in layout diagnostics. Recreation
removes old registrations, including when a new native host reuses the same
AutomationId. A native element ID can remain stable while its browser context ID
changes.

## Cross-host integration matrix

```bash
# Reuse a freshly built/running app, or omit DEVFLOW_TEST_PORT to let the fixture build/launch it.
DEVFLOW_TEST_PLATFORM=maccatalyst DEVFLOW_TEST_PORT=<port> \
  dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests \
  --filter 'FullyQualifiedName~GenericWebViewTests|FullyQualifiedName~WebViewTests'
```

`GenericWebViewTests` verifies canonical ready-only contexts, actual owner
correlation, input events/fill/insertText, exactly one click, original Hybrid
ready/ack messages, real Blazor counter events/routing, decoded native-resolution
host-sized PNGs with host-specific colors, HTTP and packaged relative navigation,
reload/recreation, console delivery, measured layout defects and native-only
scope/revision stability. It returns to the native page after each case.

For the class-order screenshot reproduction in [#407](https://github.com/dotnet/maui-labs/issues/407),
run the **complete existing class**, repeatedly on the same app:

```bash
for run in 1 2 3 4 5; do
  sleep 11 # Let the previous process's default 10-second mutation lease expire.
  DEVFLOW_TEST_PLATFORM=maccatalyst DEVFLOW_TEST_PORT=<port> \
    dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests --no-build \
    --filter 'FullyQualifiedName~Microsoft.Maui.DevFlow.Agent.IntegrationTests.WebViewTests' \
    --logger "trx;LogFileName=webview-$run.trx"
done
```

Use a longer wait if the app configures a longer lease timeout. Separate test
runner processes must not compete for the mutation lease; a lease rejection
does not reproduce the in-suite screenshot failure.

The app targets Android, iOS, Mac Catalyst and Windows. A run on one host is not
runtime evidence for the others; repeated local passes alone do not establish
that an intermittent native WebKit crash or #407 is resolved.
