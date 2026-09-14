# Friction rubric

Classify friction by product impact, repeatability, and confidence. The goal is
to explain what slowed the AI/user loop and what MAUI DevFlow could improve.

## Severity

| Severity | Signal |
| --- | --- |
| High | Advertised MAUI DevFlow feature failed, was missing, or contradicted docs; no reasonable workaround; repeated sessions hit the same issue |
| Medium | Workflow eventually worked but required retries, hidden prerequisites, stale guidance, manual fallback, or platform-specific knowledge |
| Low | Minor confusion, unclear output, noisy guidance, or a one-off workaround with low repeatability |
| Unknown | Evidence shows friction but not enough to attribute cause |

## Common friction patterns

### Advertised feature did not work

Look for cases where the agent expected a `maui devflow` command, MCP tool, or
skill workflow to exist or behave a certain way, but it failed. Capture the
advertised expectation, actual behavior, and whether docs or skill guidance
caused the expectation.

### Repeated command or tool loop

Count repeated attempts at the same underlying task, not just identical command
strings. Three or more failed attempts at connection, selection, query, action,
recording, logging, WebView/CDP, or setup are usually worth reporting.

### Workaround chain

Record the failed approaches before the workaround. A useful report explains why
the successful approach was not obvious:

- broker discovery failed, direct `--agent-port` worked
- Android registration failed until `adb reverse tcp:19223 tcp:19223`
- Android CLI-to-agent traffic failed until `adb forward tcp:<port> tcp:<port>`
- targeted query failed, shallow tree plus manual element selection worked
- platform setup docs missed a package, entitlement, or debug-only guard
- a recording or screenshot was needed because structured state was unavailable

### Broker, discovery, and port confusion

High-signal evidence includes repeated switching between broker discovery,
`localhost:9223`, `19223`, `maui devflow list`, `maui devflow wait`,
`maui devflow agent status`, and explicit host/port overrides without clear
success criteria.

### Platform package or version mismatch

Look for mismatch between `Microsoft.Maui.Cli`, embedded DevFlow package
versions, target app framework versions, or runtime platform. Examples: missing
GTK package, missing Blazor package, Mac Catalyst entitlement gaps, old direct
port configuration, or package names that changed.

### Missing stable identifiers or observability

Report when DevFlow could connect but the workflow became brittle because the
app had no stable `AutomationId`, route, DevFlow Action, log, network entry,
storage root, profiler data, or assertion point.

### AI-host or model/tool mismatch

Capture when the AI host did not expose the expected session history, MCP tools,
model metadata, or filesystem access. This may not be a DevFlow bug, but it is
useful product feedback if DevFlow guidance assumes capabilities that are not
available in common hosts.

## Confidence

| Confidence | Use when |
| --- | --- |
| Confirmed | Multiple evidence points agree, or the same DevFlow failure reproduced in-session |
| Likely | Session evidence strongly suggests DevFlow friction but one variable is missing |
| Possible | DevFlow was involved, but app code, environment, or AI host could also explain the issue |
| Unknown | Evidence is insufficient; include as a question or omit from issue filing |

## Outcome labels

- **Solved**: the session reached the intended DevFlow-assisted result.
- **Worked around**: the original DevFlow path failed, but another path
  succeeded.
- **Unresolved**: the session stopped before success.
- **Unknown**: the transcript does not show the final state.

## Stop signals

- You have identified the top actionable friction points.
- Additional candidates repeat the same pattern without adding new evidence.
- The issue is likely app-specific and not useful MAUI DevFlow product feedback.
- Confidence remains unknown after checking the approved evidence.
