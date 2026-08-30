# Evidence bundles (`.mauitrace`)

`maui devflow evidence` captures a small, redacted, shareable snapshot of a running app so a bug
report carries facts instead of screenshots and prose. A bundle is an **on-demand static artifact** —
DevFlow never records continuously and never uploads anything.

```bash
# See exactly what would be shared — nothing is written
maui devflow evidence preview

# Write the bundle (defaults to ./maui-traces/<app>-<timestamp>.mauitrace)
maui devflow evidence capture

# Validate a bundle and open a generated offline report
maui devflow evidence view ./maui-traces/MyApp-20260729-112233.mauitrace

# Choose the report path; existing reports require explicit replacement
maui devflow evidence view trace.mauitrace --output-report report.html --overwrite

# Inspect a bounded trust projection only; this does not open, import, persist, or replay the file
maui devflow evidence inspect-trust ./artifacts/flow-run.json --json

# Verify a returned Apple flow-QA ZIP without extraction or execution; optional diagnostics remain untrusted
maui devflow evidence verify-apple-qa ./devflow-flow-qa-<run-id>-ios.zip --import-diagnostics --json
```

`layout.json.systemEvidence` is not populated by this layer: correlating a layout finding with the
device's own accessibility tree needs device capture, which arrives with the Mobile Device Canvas
layer. Bundles written here carry an app-scoped layout scan only, so nothing in a bundle claims a
keyboard, permission dialog, alert, or share sheet was ruled in or out. Readers already tolerate the
field being absent.

## What a bundle contains

Format version 1 is a ZIP with a fixed, allow-listed set of entries:

| Entry | Contents |
|---|---|
| `manifest.json` | Schema and redaction ruleset versions, tool/app/platform/capability metadata, per-entry sizes and SHA-256 hashes, counts, limits, exclusions, warnings |
| `environment.json` | App, platform, device, display, agent capabilities, current route |
| `tree.json` | Element **structure**: type, safe automation id, role, bounds, state, child counts, source hash, project-relative or file-name-only source location |
| `layout.json` | Layout diagnostics: rule outcomes, per-rule coverage, and element identity/geometry for each finding — included only when the connected agent supports `diagnostics.layout` |
| `problems.json` | Binding/property diagnostics (metadata only, messages re-redacted, paths normalized) |
| `logs.json` | Bounded, scrubbed log entries |
| `network.json` | HTTP **summaries**: method, host, path, query parameter *names*, status, duration, sizes, content types, scrubbed error |
| `screenshot.png` | Only when explicitly opted in |
| `workflow.md` | Only when reproduction steps are attached — scrubbed like every other payload (max 1 MB) |

No new ZIP entry is added for flow replay. When a replay captures failure evidence, the existing
`manifest.json` may include an additive `flowRun` object with the run ID, failed step ID, typed
failure code, report digest, local `flow-run.json` path/reference, and capture-completeness state.
This links the redacted `.mauitrace` v1 bundle back to its bounded structured report without
changing the v1 allow-list. A missing path means the host retained the report in memory only; the
run ID and digest still identify the report returned by CLI, MCP, Inspector, or broker status.

## Privacy contract

Redaction happens at ingestion, before anything is serialized, so every surface (CLI, MCP, Web
Inspector) shares one ruleset — `EvidenceRedaction`, versioned as `redactionVersion` in the
manifest and in every preview.

Never captured:

- element `Text`/`Value` content, native and framework property dictionaries;
- `BindingContext` or view-model object graphs;
- preferences and secure-storage values;
- geolocation;
- file contents from app storage;
- absolute user/machine paths (replaced by project-relative paths or file names);
- HTTP headers, bodies, and query-string values.

Bounded and scrubbed:

- logs are capped (default 200, max 500) and each message is secret-masked, path-stripped and
  truncated;
- redaction ruleset v2 recognizes keyed secrets plus common standalone credential shapes such as
  `sk-`/Slack/GitHub/GitLab/Google/AWS tokens, basic-auth URLs, and PEM private-key blocks;
- network summaries are capped (default 100, max 500);
- the visual tree is capped at 5,000 elements and 64 levels deep;
- the bundled layout scan examines at most 2,000 elements and retains at most 500 findings; each
  finding's message, explanation, and limitations are scrubbed and truncated, and source paths are
  made project-relative. Findings carry element identity and geometry only — never text or values.
  An agent without `diagnostics.layout` produces an explicit exclusion instead of an empty entry.

Screenshots are **opt-in everywhere** (`--include-screenshot`, the MCP `includeScreenshot`
parameter, or the checkbox in the Inspector dialog). The manifest always records whether a
screenshot was included and, when it was not, why.

Attached reproduction steps are opt-in too: the CLI and MCP tools only read a file you name
(`--workflow steps.md`), and the Inspector only attaches the loaded workflow when you tick the
second checkbox in the confirmation dialog — because recorded steps quote the text and values you
typed. Whatever is attached is scrubbed for secrets and absolute paths before it enters the bundle,
and the manifest warns that the bundle carries it.

## Flow replay report linkage

`MauiFlowRunner` emits `flow-run-report-v1` for every pass, divergence, cancellation, timeout,
lease loss, infrastructure failure, and unknown command completion. The broker retains that report
with terminal run status and writes `<artifact-root>/<runId>/flow-run.json` atomically. The default
broker artifact root is `~/.mauidevflow/recordings/workflow-runs/`; only broker-owned `run_*`
directories are pruned, using the same bounded terminal-run count as in-memory status.
`maui devflow flow replay`, MCP replay, and Inspector replay preserve their legacy
`FlowReplayReport` fields while adding optional `report`, `reportPath`, and `reportDigest` fields.

The report records redacted selector/value descriptors, actionability polls, command receipts,
assertion disclosure state, typed failure classification, artifact references, and additive
side-effect admission facts: policy, reset/seed evidence, expected and observed preconditions,
compensator outcome, independent business-oracle results, and explicit replay/repair eligibility
reasons. A run is not marked verified unless its required independent oracle succeeds. These are
identity-only facts; reset strategies, evidence references, seed identities, and messages are
scrubbed with the same report redactor and never execute a reset or compensator.

Legacy manual schema-2 flow replay remains usable. Its report makes the absence of safety evidence
explicit with `sideEffectPolicy: "unspecified"` and `repairEligibility: false`; it does not claim a
repair-verified clean state. The report intentionally does not embed a `.mauitrace`, raw secrets,
or typed sensitive values. Evidence capture is failure-only and metadata/tree/log oriented by
default; screenshots remain opt-in.

## Preview before you share

Every surface shows the same plan object before a bundle is produced:

- the entries that will be included, with item counts;
- the entries that will be excluded, with reasons;
- the data classes that are never captured;
- the applied limits, screenshot status, redaction ruleset version, and warnings;
- the destination path (CLI/MCP) or suggested download name (Inspector).

In the Web Inspector, the **Evidence** toolbar action fetches the plan and shows it in an accessible
confirmation dialog. The bundle is only fetched and downloaded after that confirmation, and the
screenshot checkbox starts unchecked every time.

## Writing is atomic

A capture writes to a uniquely named temporary file beside the destination, validates the finished
archive with the same hostile-input reader used for imports, and only then moves it into place.
Overwriting requires `--overwrite`; without it an existing file is left untouched.

## Reading is defensive

Bundles are treated as hostile input. Before any content is decompressed the reader checks:

- entry names against the allow-list (flat names only — no traversal, nesting, or rooted paths);
- duplicate entries;
- entry count, per-entry size, total uncompressed size, and per-entry compression ratio;
- the manifest schema id, format version, and JSON shape.

Each allow-listed entry retains its capture-side limit on import (for example, workflow Markdown is
at most 1 MB and screenshots at most 16 MB). Parsed JSON sections are also semantically bounded
before rendering: null required collections, oversized arrays/strings, non-finite geometry, or
trees beyond the capture depth/count are ignored with a warning rather than reaching the report
renderer.

`evidence view` never renders or executes anything from the bundle. It parses the trusted entries
and **regenerates** a self-contained static HTML report: every value is HTML-encoded, the document
declares `default-src 'none'; script-src 'none'` (images limited to embedded `data:` URIs), and any
bundled `workflow.md` is rendered as inert, encoded text. Terminal control and Unicode format
characters are removed before rendering. Explicit report paths must use `.html`/`.htm`, reject
Windows device/alternate-stream names, and never overwrite unless `--overwrite` is supplied.
Generated reports live in a dedicated temporary folder and are aged out by TTL and count — only
files this tool created are deleted.

## Imported artifact trust

`verify-apple-qa` is a separate read-only handoff verifier for the iOS Simulator, Mac Catalyst,
and experimental AppKit return ZIP/directory described in [platform flow QA](flow-qa.md). It
accepts only the documented path allowlist, rejects traversal/duplicate/symlink/oversize/bomb
entries, validates manifest hashes, and never extracts or executes the ZIP. Compatible per-flow
`flow-run.json` and `.mauitrace` entries can be projected with `--import-diagnostics`, but receive
fresh isolated `imported-artifact` IDs and remain `untrusted` until a new local reproduction.

When a `.mauitrace` v1 bundle is imported into the broker artifact-trust surface, the existing
hostile ZIP reader still enforces its entry, size, compression-ratio, UTF-8, manifest, and typed
shape limits. The broker calculates a whole-artifact SHA-256 and may validate manifest entry
hashes, but those hashes prove **integrity only**. They never establish producer provenance or make
the bundle executable.

The broker keeps no raw ZIP after import: only a bounded redacted typed projection, a fresh
`imported-artifact` ID, and a short-lived capability token remain in memory. There is no broad list
or raw-download endpoint. Imported content starts `untrusted`; independently verified external
facts can make it `attested`, but both states remain diagnostic-only. A new local run must match
the current flow/app/target/failure facts before it can be `locally-reproduced` and eligible for a
future approvable repair or source proposal. Imports never auto-open, replay, or append repair
history.

## Prototype-study local journal

The Test Workbench Results card has a separate, file-only **Prototype evidence (local only)**
export for local authoring studies. It is not a `.mauitrace` entry, broker artifact, or telemetry
surface. A bounded browser `sessionStorage` journal records only allow-listed timestamps, safe
provenance enums, booleans, bounded counts/durations, and locally pseudonymized run/proposal
references. It has no upload, HTTP endpoint, or network egress.

The export is explicitly `localSessionOnly: true` and omits Goal text, flow content, UI text,
typed values, selectors, source paths/content, screenshots, prompts, reviewer identity, URLs,
payloads, device serials, and secrets. It summarizes authoring/review/result timing, selector
durability, in-session replay stability, safe classification counts, repair decisions, Improve
usage, and explicit gaps. It can be cleared only by an explicit confirmation in the current tab.

This local research-assessment aid complements digest-bound flow-run reports, qualification
accounting, and device artifacts; it does not alter qualification-v1 requirements, establish
provenance, or certify a platform or device.

## Qualification privacy and adversarial evidence

The engineering-preview qualification report is an additional **redacted accounting document**,
not a new evidence bundle format. It stores hashes, counts, fixed reason codes, and safe artifact
references only. It does not contain raw UI text, Markdown, logs, network content, source,
absolute paths, grant material, prompt text, secrets, screenshots, model context, or imported ZIP
contents.

The static adversarial corpus covers prompt injection in UI/log/network/Markdown/artifact fields;
secret/path/source canaries; hostile JSON/ZIP/traversal/origin/grant/idempotency input; imported
artifact trust; prohibited test-agent tools; and repair/source non-apply behavior. Canary checks
fail closed if a canary reaches a report, evidence, audit, model-projection, or artifact
projection. The corpus validates the local redaction/projection boundary; it is not a claim that
an untested remote provider or a physical device was exercised.

The Inspector Trace tab uses this same import boundary. It presents only the safe projection and
trust reasons in captured/read-only mode; it cannot reopen the raw imported ZIP/report after the
boundary has discarded it. For a local broker run, Trace can download a retained linked
`.mauitrace` v1 failure bundle and displays its report/evidence digest linkage and completeness.
The automatic failure capture is redacted and excludes screenshots and flow text unless the human
explicitly opted into each attachment during the Run check.

## MCP tools

| Tool | Purpose |
|---|---|
| `maui_evidence_preview` | Return the plan for a capture without writing anything |
| `maui_evidence_capture` | Write a bundle atomically and return its path plus the manifest summary |

## Implementation

| Area | Location |
|---|---|
| Format, models, redaction, paths | `src/Cli/Microsoft.Maui.Cli/DevFlow/Evidence/` |
| CLI commands | `src/Cli/Microsoft.Maui.Cli/DevFlow/Evidence/EvidenceCommands.cs` |
| MCP tools | `src/Cli/Microsoft.Maui.Cli/DevFlow/Mcp/Tools/EvidenceTools.cs` |
| Inspector routes | `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/InspectorServer.cs` (`/api/evidence/*`) |
| Inspector UI | `src/Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-evidence.js` |
| Tests | `src/DevFlow/Microsoft.Maui.DevFlow.Tests/Evidence*Tests.cs`, `src/Cli/Microsoft.Maui.Cli.UnitTests/EvidenceCliTests.cs`, `src/DevFlow/js/test/inspector-evidence.test.mjs` |
