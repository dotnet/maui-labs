# Appium black-box smoke tests

`Microsoft.Maui.DevFlow.Appium.SmokeTests` is an opt-in, external Appium lane. It is
intentionally separate from DevFlow semantic flow execution.

| Concern | DevFlow semantic tests | Appium smoke tests |
|---|---|---|
| Target | An instrumented app and the DevFlow agent | A launchable or attachable app through an Appium server |
| Execution | `MauiFlowRunner` is the canonical flow executor | The official `Appium.WebDriver` client drives native UI externally |
| Best for | Deterministic app semantics, diagnostics, and bounded flow evidence | Release-like or uninstrumented builds, native compatibility, and OS-owned UI |
| System dialogs | Not a replacement for operating-system UI | Optional Android/iOS permission-dialog contract |
| Repair/source authority | Strictly human-gated DevFlow proposal workflow | None |
| Qualification | DevFlow's separate preview qualification evidence | Never a DevFlow repair or qualification pass |

The project has no project reference to the DevFlow agent, broker, driver, CLI, or Testing
package. It does not connect to a broker, consume Markdown flows, produce `flow-run.json`, or
call repair/source-proposal services. It can therefore exercise a release-like build that omits
the in-app DevFlow agent, but it cannot substitute for semantic flow coverage or mark repair
qualification as passed.

## Contract

The default contract uses existing stable IDs in `DevFlow.Sample`:

1. Appium launches the configured app, or attaches to its configured package/bundle/window.
2. It finds `ShowModalButton` by Appium accessibility ID and taps it.
3. It finds the visible `ModalTitle` result and verifies its `Modal Page` text.
4. On failure it writes a screenshot when supported, redacted page source, and available Appium
   server/platform logs.

No sample application behavior or AutomationId is added for this lane. A consuming app can use
the same contract only when it provides equivalent stable app-owned IDs.

`AutomationId` is located with `MobileBy.AccessibilityId` on every current platform. The native
mapping differs: Android uses `content-desc`, iOS/Mac use an accessibility identifier, and
Windows maps that Appium strategy to the UI Automation `AutomationId`. Do not replace this with
coordinates, indexes, or display text.

## Host and driver support

| Target | Host and Appium driver | Lane status |
|---|---|---|
| Android | Windows, macOS, or Linux; `uiautomator2` | Supported smoke configuration |
| iOS simulator/device | macOS; `xcuitest`, Xcode, and normal signing/device prerequisites | Supported when a macOS test environment is provisioned |
| Mac Catalyst / Mac2 | macOS; `mac2` | Best-effort external compatibility lane; Mac2-driver behavior is not an official MAUI platform-support claim |
| Windows | Windows; `windows` driver and its compatible Windows automation dependency | Supported only where the driver can launch or attach the target app |
| Linux desktop | Not currently a desktop Appium target in this lane | Unsupported; Linux can host Android Appium tests |

The lane deliberately does not install Node.js, Appium, an Appium driver, Xcode, Android tools, or
Windows automation dependencies. Those belong to the machine or a dedicated device-lab image.

## Setup and invocation

Install and start the driver appropriate for the target. For example, an Android emulator:

```powershell
npm install -g appium
appium driver install uiautomator2
appium --address 127.0.0.1 --port 4723
```

Build or provide an APK, app bundle, or executable independently. The black-box test does not
require the DevFlow agent in that build. Configure only environment variables; the test rejects
server URLs containing user info and does not accept hard-coded credentials.

```powershell
$env:DEVFLOW_APPIUM_SMOKE = '1'
$env:DEVFLOW_APPIUM_PLATFORM = 'android'
$env:DEVFLOW_APPIUM_SERVER_URL = 'http://127.0.0.1:4723/'
$env:DEVFLOW_APPIUM_DEVICE_NAME = 'Android Emulator'
$env:DEVFLOW_APPIUM_UDID = 'emulator-5554'
$env:DEVFLOW_APPIUM_APP = 'C:\build\DevFlow.Sample.apk'
$env:DEVFLOW_APPIUM_ARTIFACT_ROOT = 'artifacts\TestResults\appium'

dotnet test src\DevFlow\Microsoft.Maui.DevFlow.Appium.SmokeTests\ `
  --filter 'Category=AppiumSmoke'
```

For an already-installed Android app, use `DEVFLOW_APPIUM_APP_PACKAGE` and optionally
`DEVFLOW_APPIUM_APP_ACTIVITY` instead of `DEVFLOW_APPIUM_APP`. iOS and Mac2 require
`DEVFLOW_APPIUM_APP` or `DEVFLOW_APPIUM_BUNDLE_ID`; Windows requires
`DEVFLOW_APPIUM_APP` or `DEVFLOW_APPIUM_APP_TOP_LEVEL_WINDOW`.

The required variables are:

| Variable | Purpose |
|---|---|
| `DEVFLOW_APPIUM_SMOKE` | Set to `1` or `true` to run the external smoke contract. It is otherwise skipped with a clear reason. |
| `DEVFLOW_APPIUM_PLATFORM` | `android`, `ios`, `mac2` (also accepts `maccatalyst`), or `windows`. |
| `DEVFLOW_APPIUM_DEVICE_NAME` | Appium device name. |
| `DEVFLOW_APPIUM_SERVER_URL` | Optional HTTP(S) server URL; defaults to `http://127.0.0.1:4723/`. User info is rejected. |
| `DEVFLOW_APPIUM_APP` | App path for Appium to launch/install. |
| `DEVFLOW_APPIUM_APP_PACKAGE`, `DEVFLOW_APPIUM_APP_ACTIVITY` | Android package/activity attach or launch configuration. |
| `DEVFLOW_APPIUM_BUNDLE_ID` | iOS/Mac2 attach or launch configuration. |
| `DEVFLOW_APPIUM_APP_TOP_LEVEL_WINDOW` | Windows attach handle. |
| `DEVFLOW_APPIUM_UDID`, `DEVFLOW_APPIUM_PLATFORM_VERSION` | Optional device selection. |
| `DEVFLOW_APPIUM_COMMAND_TIMEOUT_SECONDS`, `DEVFLOW_APPIUM_ELEMENT_TIMEOUT_SECONDS` | Optional bounded timeouts, from 1 through 600 seconds. |
| `DEVFLOW_APPIUM_ARTIFACT_ROOT` | Optional artifact root; defaults to `artifacts/TestResults/appium`. |
| `DEVFLOW_APPIUM_CAPTURE_SCREENSHOTS` | Set to `0` or `false` to omit screenshots; enabled by default after the explicit smoke opt-in. |

For Apple and Windows, replace the installed driver as appropriate:

```powershell
appium driver install xcuitest # iOS, on macOS
appium driver install mac2     # Mac Catalyst/Mac2, on macOS
appium driver install windows  # Windows, on Windows
```

## Optional permission/system-dialog contract

Only Android and iOS support the optional permission-dialog smoke. It is off by default because
permission labels, system UI, and prior device state are platform- and locale-specific. Before
running it, reset the dedicated test device's permission state and provide the app-owned route
or launch configuration that reaches the sample dialog page.

```powershell
$env:DEVFLOW_APPIUM_PERMISSION_SMOKE = '1'
$env:DEVFLOW_APPIUM_PERMISSION_NAVIGATION_ID = '<optional app-owned navigation accessibility ID>'
$env:DEVFLOW_APPIUM_PERMISSION_TRIGGER_ID = 'RequestCameraBtn'
$env:DEVFLOW_APPIUM_PERMISSION_ALLOW_ID = '<platform and locale-specific system Allow accessibility ID>'
$env:DEVFLOW_APPIUM_PERMISSION_RESULT_ID = 'DialogStatusLabel'
```

`RequestCameraBtn` and `DialogStatusLabel` already exist in `DevFlow.Sample`. The allow identifier
is deliberately not guessed: a device-lab configuration must declare the system-dialog identifier
it expects. A missing or unsupported permission configuration fails an explicitly enabled run
rather than reporting a DevFlow pass.

## Artifacts and CI

Failure artifacts are written below:

```text
artifacts/TestResults/appium/smoke-<UTC>-<random>/
  failure.txt
  page-source.xml
  appium-logs.txt
  screenshot.png
```

Text artifacts are path-confined, size-bounded, and redact common password/token/authorization
shapes. Screenshots cannot be reliably text-redacted; use a dedicated test account and set
`DEVFLOW_APPIUM_CAPTURE_SCREENSHOTS=0` where image retention is inappropriate.

The fast `src/DevFlow/DevFlow.slnf` intentionally excludes this device lane. Runs that include
this project execute its configuration/unit tests and skip the live contract unless
`DEVFLOW_APPIUM_SMOKE=1`. The repository's `devflow-integration.yml` currently provisions MAUI
integration environments but does not provision a pinned Appium server, external driver, or
release artifact handoff. Consequently there is no required PR or implicit nightly Appium job:
run this documented command manually or from a separately provisioned opt-in device-lab/nightly
environment.
