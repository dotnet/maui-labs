# Session sources

Use the narrowest source that can answer the user's request. The review is
opt-in; do not broaden from the approved scope to unrelated history without
asking.

## Source order

1. **User-approved scope**: current session, recent sessions, date range, target
   platform, feature, command family, or DevFlow workflow.
2. **Current session context**: useful when the review is prompted because the
   current DevFlow workflow was stuck.
3. **Session history tools**: use when available to search recent sessions by
   MAUI DevFlow-specific signals.
4. **Already-provided sanitized material**: use only if the user has already
   supplied it. Do not ask for session IDs, transcript paths, log paths, artifact
   paths, or local file names.

Do not recursively scan a home directory or cloud-synced folder just because it
might contain session logs.

## Session history query strategy

When a session-store query tool is available:

- Start with a time bound and `LIMIT`.
- Search for DevFlow-specific terms first: `maui devflow`,
  `Microsoft.Maui.DevFlow`, `AddMauiDevFlowAgent`, `maui_wait`, `maui_tree`,
  `maui_query`, `maui devflow wait`, `maui devflow ui tree`, `recording`,
  `broker`, `9223`, or `19223`.
- Prefer finding candidate sessions within the scoped search, then inspect only
  those sessions. If a tool exposes internal handles, use them only
  transiently; never ask the user for them or include them in output.
- Include exact filters such as agent/tool fields when available.
- Widen the time window only if the first query finds too little evidence.

Example intent, not a required exact query:

```sql
-- Find recent candidate sessions with MAUI DevFlow-specific text.
SELECT timestamp, user_message, assistant_response
FROM turns
WHERE timestamp > now() - INTERVAL '14 days'
  AND (
    COALESCE(user_message, '') ILIKE '%maui devflow%'
    OR COALESCE(assistant_response, '') ILIKE '%maui devflow%'
    OR COALESCE(assistant_response, '') ILIKE '%Microsoft.Maui.DevFlow%'
    OR COALESCE(assistant_response, '') ILIKE '%maui_wait%'
    OR COALESCE(assistant_response, '') ILIKE '%maui_tree%'
    OR COALESCE(assistant_response, '') ILIKE '%AddMauiDevFlowAgent%'
  )
LIMIT 50
```

After selecting candidate sessions, look for retry patterns and outcomes in the
same session rather than scanning every historical turn.

## Provided material fallback

If no session-store tool is available, work from the current conversation or
material the user already provided. Do not ask for local paths or identifiers.
Collect only non-PII facts:

- relevant time window, turn number, or command family
- command/tool attempts and results
- final answer or stopping point

If the user provides a transcript that includes private material, summarize the
safe pattern and avoid quoting sensitive text.

## Evidence to preserve

For each finding, preserve the smallest useful evidence set:

- sanitized evidence summary
- turn/time range or command sequence without local identifiers
- observed failure or repeated attempt
- workaround attempts
- final successful command or unresolved blocker
- environment metadata source type, such as CLI output or package manifest,
  without local file paths

Do not preserve session IDs, file paths, full transcripts, screenshots, tokens,
request bodies, private URLs, usernames, emails, machine names, or private user
text in the report.

## Output scrub

Before sending, saving, or filing anything, scan the summary for PII and local
identifiers. Remove or generalize:

- names, usernames, emails, handles, or account IDs
- session IDs, transcript IDs, file paths, artifact paths, machine names, and
  home-directory fragments
- private hostnames, internal URLs, tokens, credentials, request bodies, or
  screenshots
- user-authored private text copied from a transcript

## Stop signals

- You have enough evidence to explain the top actionable findings.
- Candidate sessions are unrelated to MAUI DevFlow after a quick scan.
- Further investigation would require private or out-of-scope history.
- The output would become a transcript dump instead of product feedback.
