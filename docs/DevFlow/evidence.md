# Evidence bundles (`.mauitrace`)

Evidence is an on-demand, read-only snapshot for a bug report. It is not a recording,
a replay result, or a device qualification claim. Capture runs locally and never uploads
anything.

## Preview and capture

Choose **Evidence** in the Inspector toolbar, or under **More** in a compact host. The same
dialog is used in the browser, VS Code, and Copilot Canvas. It shows the current app's
environment, the included entries, exclusions, redaction limits, and warnings.
Unchanged connection/control polling does not dismiss the **More** menu while choosing Evidence.

Screenshots and loaded workflow text start **off on every opening**. Enabling an attachment
refreshes the preview and requires a second confirmation before download. Preview never
reads screenshot pixels. Cancel or Escape creates no bundle.

```powershell
# Inspect the capture plan without writing a file
maui devflow evidence preview --json

# Write a bundle; add --overwrite only to replace an existing file
maui devflow evidence capture --output .\maui-traces\issue.mauitrace

# Explicitly include attachments
maui devflow evidence preview --include-screenshot --workflow .\repro.md
maui devflow evidence capture --include-screenshot --workflow .\repro.md

# Validate a bundle and regenerate an offline report
maui devflow evidence view .\maui-traces\issue.mauitrace

# Generate a report without launching a browser
maui devflow evidence view .\maui-traces\issue.mauitrace --no-open --output-report .\report.html
```

CLI and MCP captures use a project-local `maui-traces` folder when a project can be resolved,
or the current directory otherwise. Inspector downloads use the browser/host download
mechanism; the preview does not expose an absolute host output path.

The MCP tools `maui_evidence_preview` and `maui_evidence_capture` use the same builder,
redaction rules, and bundle format as the Inspector and CLI. Both accept `workflowFile`
so the optional attachment can be previewed before capture.

## Environment observations

The preview, `environment.json`, and regenerated report expose the same target-side facts
when the agent can supply them:

| Fact | Meaning |
|---|---|
| App version, build, and package | Reported by the running app, not inferred from a local checkout |
| OS, manufacturer/model, physical or virtual device | Device names, serials, and personal identifiers are not included |
| Framework and agent versions | A framework version is **not** a target runtime version |
| App viewport and density | Default app-window dimensions in logical units (DIP), pixels per DIP, and window count |
| Display and orientation | Display pixels, display density, orientation, rotation, and refresh rate |
| Theme | Effective, requested, and app-override theme, when reported |
| Connectivity | Network-access state and allow-listed transports such as WiFi or Cellular; no SSID or IP address |

The `unavailable` inventory explicitly discloses missing facts. Target runtime version,
locale, font scale/accessibility text settings, and permission states are not exposed by
this capture implementation. Connectivity does not measure or enforce latency, bandwidth,
packet loss, proxy settings, or a simulated offline condition. Capture never requests
permissions or changes the app/device environment.

Missing viewport values are **not** replaced with display dimensions. Missing app density
is not replaced with display density. Neither the host's .NET runtime, locale, browser
theme, nor host font size is substituted for an unknown target value.

Collection uses sequential reads. Preview and capture sample the app separately, so counts
and current state can change between them. An attached workflow does not make this the
environment in which that workflow was originally recorded. Multiple-window apps are
identified explicitly; this capture observes the agent's default window.

## Recording and CI scope

This feature addresses **environment visibility and honest evidence gaps**. It does not
define or enforce a replay environment contract.

Stable AutomationId/semantic locators, business-visible assertions, environment
preconditions, reset/seed policies, and a golden journey across a small physical-device/
emulator matrix belong to recording, replay, and CI changes. Those changes should fail
closed when a required environment condition cannot be satisfied, rather than treating an
unknown field, screenshot, or successful gesture as a passing business assertion.

The existing recorder already supports AutomationId targets and verifying assertions.
Evidence retains structural AutomationIds, but does not repair or promote locators.
Attached structured workflow values and expected assertion text are redacted, so the
attachment is diagnostic context, **not a replayable golden test**.
When a file is loaded before replay validation, an unknown step count is not displayed as zero.

## Bundle and privacy contract

Version 1 is a ZIP with a fixed entry allow-list:

| Entry | Contents |
|---|---|
| `manifest.json` | Format/redaction versions, origin surface, counts, limits, exclusions, warnings, entry sizes and SHA-256 hashes |
| `environment.json` | Target-side observations and explicit environment gaps |
| `tree.json` | Bounded structure: type, AutomationId, role, geometry, state, and normalized source location |
| `layout.json` | Existing agent layout findings and coverage, with at most 500 retained findings; excluded when unsupported |
| `problems.json` | Recognized for reading existing evidence; explicitly excluded from capture because the current agent API has no runtime Problems surface |
| `logs.json` | Scrubbed log entries: 200 by default, at most 500 |
| `network.json` | Request summaries: 100 by default, at most 500; query names only |
| `screenshot.png` | Only after explicit opt-in |
| `workflow.md` | Only after explicit attachment, at most 1 MiB, with secrets and structured values scrubbed |
| `device.json` | Recognized v1 workflow/device metadata; not produced or rendered here, with an explicit reader warning when present |

Tree projection retains at most 5,000 elements and 64 levels. No element Text/Value,
native/framework property dictionaries, view-model object graphs, preferences, secure
storage, geolocation, app file contents, HTTP headers/bodies, or query-string values are
collected into the structured entries. Source paths become project-relative or file-name-only.
The tool version is the CLI's informational product version, not its build-system assembly-version
placeholder.

Redaction is heuristic, not a guarantee that arbitrary prose contains no private data.
Review logs and workflow prose before sharing. Screenshot pixels are not redacted and can
show any on-screen information; that is why they are a separate explicit opt-in.

Bundles are written atomically and read back through the validating reader before publication.
Existing bundles and reports are never overwritten without `--overwrite`. Malformed options
fail before app reads, and unavailable sections become explicit exclusions rather than empty
successes.

Imported bundles are untrusted. The reader rejects unknown, nested, traversing, duplicate,
oversized, or corrupt entries and mismatched manifest hashes. Typed sections have separate
shape/size limits. Content hashes establish integrity, not producer identity or environment
truth. No archive entry is extracted or executed.

The offline report is regenerated from encoded values, includes no scripts, and has a
restrictive Content Security Policy that blocks network requests. Workflow Markdown is shown
as inert text.

## Contributor checks

The capture, route, CLI, and controller checks require no running app:

```powershell
dotnet test .\src\Cli\Microsoft.Maui.Cli.UnitTests\Microsoft.Maui.Cli.UnitTests.csproj --filter FullyQualifiedName~Evidence
node --test .\src\DevFlow\js\test\inspector-evidence.test.mjs
```

Isolated browser checks use the real Inspector and a loopback fixture. They cover light/dark
hosts, narrow/short viewports, 200% text, keyboard containment, fresh attachment consent,
and a real `.mauitrace` download. They are opt-in because normal test runs do not provision
Chromium. Install Chromium with the test project's generated `playwright.ps1 install chromium`
if it is not already available, then run:

```powershell
$env:DEVFLOW_EVIDENCE_BROWSER_TESTS = "1"
dotnet test .\src\DevFlow\Microsoft.Maui.DevFlow.Inspector.Tests\Microsoft.Maui.DevFlow.Inspector.Tests.csproj --filter FullyQualifiedName~EvidenceInspectorPageTests
```

Set `EVIDENCE_TEST_ARTIFACTS` to a local directory to retain the browser screenshots.
