---
name: maui-devflow-session-review
description: >-
  Review previous AI sessions that used MAUI DevFlow to identify opt-in product
  feedback, friction, repeated attempts, failed advertised features, and
  workarounds. USE FOR: MAUI DevFlow session review, stuck maui devflow debugging
  sessions, reviewing CLI/MCP behavior for friction, markdown feedback reports,
  filing dotnet/maui-labs GitHub issues. DO NOT USE FOR: fixing discovered bugs,
  adding DevFlow to apps (use maui-devflow-onboard), iterative app debugging
  (use maui-devflow-debug), or generic memory search. INVOKES: session
  history/search tools, gh CLI, and maui devflow CLI.
---

# MAUI DevFlow Session Review

Use this skill to turn prior MAUI DevFlow-assisted AI sessions into opt-in
product feedback. The goal is not to fix bugs during the review; the goal is to
identify where DevFlow caused friction, what workarounds were tried, and what
outcome the agent eventually reached.

## When to Use

Use this skill when:

- reviewing previous AI sessions that used `maui devflow`
- summarizing a long or stuck MAUI DevFlow debugging session
- finding repeated failed attempts, retries, or workarounds around DevFlow tools
- checking whether an advertised DevFlow feature did not work in practice
- preparing a markdown feedback report for MAUI DevFlow maintainers
- filing GitHub issues in `dotnet/maui-labs` from session evidence
- another DevFlow skill suggested an optional post-session feedback review

## When to Stop

- The top actionable friction points are summarized with evidence, environment
  metadata, attempts, workaround, and final outcome.
- The markdown report is written or the requested GitHub issues are filed after
  a PII scrub.
- Remaining evidence would require scanning outside the user-approved scope.
- The finding is too ambiguous to classify without more context; label it
  unknown instead of continuing to mine private history.

## Workflow

### 1. Confirm opt-in scope

Identify what the user wants reviewed:

- current session
- recent sessions matching MAUI DevFlow keywords
- a date range, target platform, feature, command family, or app framework
- sessions related to setup, connectivity, UI inspection, CDP, recording, logs,
  network, storage, profiler, or DevFlow Actions

Do not scan unrelated session history. This skill is opt-in telemetry: the user
controls the scope and the output destination.

Do not ask the user for session IDs, transcript paths, log paths, artifact
paths, or other local identifiers. If a tool requires an internal handle to
inspect a session, use it only transiently and never include it in saved or
shared output.

If another DevFlow skill suggested this review, preserve the trigger context:
what challenge was stuck, which workarounds had already been tried, and whether
the user wants markdown-only output or GitHub issue filing.

### 2. Find DevFlow-specific sessions

Use [references/session-sources.md](references/session-sources.md) to choose the
available data source. Good candidate signals include:

- `maui devflow`, `maui-devflow-onboard`, `maui-devflow-debug`, or this skill
- CLI commands such as `maui devflow wait`, `maui devflow list`,
  `maui devflow agent status`, `maui devflow ui tree`,
  `maui devflow ui query`, `maui devflow recording`, `maui devflow logs`,
  `maui devflow network`, `maui devflow diagnose`, or `maui devflow mcp`
- MCP tools such as `maui_wait`, `maui_list_agents`, `maui_status`,
  `maui_tree`, `maui_query`, `maui_tap`, `maui_recording_start`,
  `maui_recording_stop`, `maui_logs`, `maui_network`, `maui_cdp_*`,
  or `maui_invoke_action`
- package and API names such as `Microsoft.Maui.DevFlow.Agent`,
  `Microsoft.Maui.DevFlow.Blazor`, `Microsoft.Maui.DevFlow.Agent.Gtk`,
  `Microsoft.Maui.DevFlow.Driver`, `builder.AddMauiDevFlowAgent()`,
  or `AddMauiBlazorDevFlowTools()`
- broker or port evidence such as `19223`, `9223`, `adb reverse`,
  `adb forward`, agent registration, or direct port fallback

Start narrow and widen only when the first search does not find enough evidence.

### 3. Build the environment fingerprint

For every report and issue, collect the metadata in
[references/environment-fingerprint.md](references/environment-fingerprint.md):

- host platform and OS version
- AI tool or host, if detectable
- model name and reasoning effort, if detectable
- MAUI, .NET SDK, and MAUI DevFlow package versions, if available
- target app platforms, such as Android, iOS, Mac Catalyst, macOS, Windows,
  Linux/GTK, or Blazor WebView runtime
- `maui` CLI version and invocation mode
- embedded DevFlow package/reference versions in the app

Unknown metadata is acceptable. Mark it as `Unknown` rather than guessing.

### 4. Classify friction

Use [references/friction-rubric.md](references/friction-rubric.md). Prioritize:

- advertised DevFlow features that failed or were unavailable
- repeated attempts at the same command, selector, connection, or setup step
- workaround chains that eventually succeeded
- broker discovery, direct agent port, or Android forwarding loops
- platform-specific setup, package, entitlement, or version mismatches
- missing stable `AutomationId`s or missing observability that made DevFlow less
  useful
- AI-host or model behavior that misused DevFlow guidance

Separate confirmed DevFlow issues from app bugs, environment setup problems, and
unknown causes.

### 5. Capture attempts, workarounds, and outcome

For each finding, record:

- symptom
- sanitized evidence summary with no session IDs, file paths, usernames, machine
  names, emails, private URLs, or other PII
- attempts and retries, especially repeated or contradictory actions
- workarounds tried
- final outcome: unresolved, worked around, solved, or unknown
- confidence level and why

Paraphrase private transcript content. Before sending, saving, or filing any
report, scrub PII and identifiers including names, emails, usernames, session
IDs, local file paths, machine names, private URLs, secrets, credentials, request
bodies, screenshots, and user-specific text.

### 6. Report or file issues

Use [references/reporting.md](references/reporting.md). Default to a markdown
report. File GitHub issues only when the user asks and repository access works.

For GitHub issues, prefer one issue per actionable MAUI DevFlow product problem.
Merge near-duplicate session evidence into the same issue body instead of
opening many low-signal issues.

Before saving markdown or filing issues, do a final privacy pass. If a useful
finding cannot be explained without PII or local identifiers, omit it or replace
the details with a generic description.

## Optional Feedback Nudge

Other MAUI DevFlow skills may suggest this review when an agent has retried the
same DevFlow workflow several times, tried multiple workarounds, or finishes a
long DevFlow-assisted debugging session. That nudge should ask the user whether
they want an opt-in review; it should not run this skill automatically.

## Critical Anti-Patterns

| Do not | Do instead |
| --- | --- |
| Auto-scan all past sessions | Ask for scope and stay inside it |
| Ask for session IDs, transcript paths, log paths, or artifact paths | Ask for a high-level scope such as time range, platform, feature, or current session |
| Treat one ambiguous failure as a confirmed DevFlow bug | Label confidence and separate app/environment/unknown causes |
| Include raw private transcript excerpts, screenshots, tokens, file paths, session IDs, or request bodies | Paraphrase safe evidence and scrub PII before output |
| Fix the discovered issues during the review | Produce feedback unless the user separately asks for implementation |
| File GitHub issues without user approval and repo access | Produce markdown first, then file only on request |
| Hide successful workarounds | Capture the final working path and the failed attempts that led to it |

## References

- **Session sources**:
  [references/session-sources.md](references/session-sources.md)
- **Friction rubric**:
  [references/friction-rubric.md](references/friction-rubric.md)
- **Environment fingerprint**:
  [references/environment-fingerprint.md](references/environment-fingerprint.md)
- **Reporting and GitHub issues**:
  [references/reporting.md](references/reporting.md)
