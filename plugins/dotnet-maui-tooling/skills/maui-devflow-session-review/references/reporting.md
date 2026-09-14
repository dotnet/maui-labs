# Reporting and GitHub issues

Default to a markdown report. File GitHub issues only when the user asks and the
agent has access to `dotnet/maui-labs`.

Before sending, saving, or filing any output, perform a PII scrub. Reports and
issues must not contain session IDs, transcript IDs, local file paths, artifact
paths, names, usernames, emails, machine names, private URLs, credentials,
tokens, request bodies, screenshots, or user-specific private text.

## Markdown report template

```markdown
# MAUI DevFlow session review

## Summary

{One paragraph with scope, session count, and top findings.}

## Environment

- Host OS/platform: {value}
- AI tool/host: {value}
- Model: {value}
- MAUI CLI: {value}
- App framework(s): {value}
- Target platform(s): {value}
- MAUI DevFlow package/reference versions: {value}
- Review scope: {current session/recent sessions/date range/platform/feature}

## Findings

### 1. {short title}

- Severity: {High/Medium/Low/Unknown}
- Confidence: {Confirmed/Likely/Possible/Unknown}
- Outcome: {Solved/Worked around/Unresolved/Unknown}
- Symptom: {what happened}
- Evidence: {sanitized behavior summary, no identifiers or paths}
- Attempts: {failed attempts, retries, detours}
- Workaround or success route: {final working approach, if any}
- Product feedback: {what MAUI DevFlow could improve}

## Suggested GitHub issues

{List issue titles or say "None requested".}
```

## GitHub issue template

Use this shape when the user asks to file issues:

```markdown
## Summary

{A concise product-facing description of the friction.}

## Environment

- Host OS/platform: {value}
- AI tool/host: {value}
- Model: {value}
- MAUI CLI: {value}
- App framework(s): {value}
- Target platform(s): {value}
- MAUI DevFlow package/reference versions: {value}

## What happened

{Observed symptom and user/agent goal.}

## Attempts and workarounds

1. {attempt}
2. {attempt}
3. {workaround or final state}

## Expected behavior

{What MAUI DevFlow advertised or should reasonably do.}

## Actual behavior

{What happened instead.}

## Outcome

{Solved, worked around, unresolved, or unknown. Include workaround if present.}

## Scope

{High-level review scope only, with no session IDs or file paths.}
```

## Filing guidance

- Ask before filing. Markdown-only is the default.
- Check that `gh` is authenticated and has access to `dotnet/maui-labs`.
- Prefer one issue per distinct MAUI DevFlow product problem.
- Merge repeated evidence into one issue when multiple sessions hit the same
  symptom.
- Do not include private transcript excerpts, secrets, screenshots, request
  bodies, session IDs, file paths, or user-specific data.
- If access fails, keep the issue body in the markdown report for manual filing.

## Title patterns

Good issue titles are product-facing and specific:

- `Broker discovery loop when agent is registered but maui devflow wait times out`
- `Android agent registration guidance missed adb reverse for broker port`
- `maui_query failure required full tree workaround for stable AutomationId`
- `Blazor WebView CDP guidance lacked fallback when webview list was empty`

Avoid titles that blame the AI host without evidence:

- `Copilot failed`
- `Session was bad`
- `DevFlow is broken`
