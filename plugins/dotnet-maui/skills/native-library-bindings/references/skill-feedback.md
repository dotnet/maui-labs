# Improving this skill (opt-in self-review feedback)

Native binding work regularly runs into edge cases this skill has not yet
captured. This reference turns that friction into **opt-in feedback that
improves the skill for everyone** — new eval scenarios, missing reference
context, corrected guidance, or a helper-script/asset gap — filed as a GitHub
issue against `dotnet/maui-labs`.

The goal is not to fix the binding during the review, and not to file bugs about
the native SDK itself. The goal is to describe where *this skill's guidance* was
missing, wrong, or slowed the loop, and to propose a concrete skill improvement.

This is opt-in. The user controls whether a review runs, what it covers, whether
anything is written to disk, and whether any issue is filed.

## When to offer a review (nudge triggers)

Offer the review at a natural stop point, not mid-task, when the binding session
shows one or more of:

- A **documented gap** was worked around — the skill did not cover the SDK
  distribution shape, language surface, dependency situation, or packaging case
  that actually came up.
- **Three or more repeated failures** at the same underlying step (acquisition,
  Sharpie cleanup, dependency resolution, packaging, or an update).
- An **upstream update** surprised the flow (unexpected breaking changes, a
  changed distribution/auth model, dropped platform slices).
- A **strategy the skill did not anticipate** was needed (for example an SDK
  requiring a hand-built ObjC wrapper, an unusual credentialed source, or a
  code-generated modular graph).
- Guidance here was **stale or contradicted reality** (a command, property, or
  package name that no longer behaves as described).

Always ask first. Never run the review or file anything automatically.

## Review workflow

### 1. Confirm scope and consent

Ask what to review: the current session only, or a described set of recent
binding sessions (time range, platform, or SDK family). Stay inside that scope.
Do not scan unrelated history and do not ask for session IDs, transcript paths,
log paths, or other local identifiers — use any internal handle only transiently
and never in output.

### 2. Locate binding friction

Look for binding-specific signals within the approved scope: Objective Sharpie
runs, `.xcframework`/AAR/POM handling, `AndroidMavenLibrary`/`AndroidGradleProject`,
`Metadata.xml` transforms, XA4xxx errors, `NoClassDefFoundError`, duplicate
symbols/types, `.props`/`.targets` packaging, and update/re-acquisition steps.

### 3. Classify each friction point against the skill

For every finding, map it to a **skill-improvement category**:

| Category | What it means | Typical fix |
| --- | --- | --- |
| Missing case | A real distribution/language/dependency/packaging shape not covered | Add a reference note + an eval scenario |
| Wrong or stale guidance | A documented command/property/package behaved differently | Correct the reference; add a regression eval |
| Missing detail | The right idea was present but too thin to act on | Expand the relevant reference section |
| Missing/failing script or asset | A helper script or template did not exist or did not work | Add/fix the script or asset |
| Unclear routing | The skill did not steer to the correct workflow/strategy | Tighten SKILL.md routing + anti-patterns |

Separate skill gaps from native-SDK bugs, environment/setup problems, and
unknown causes. Only skill gaps belong in this feedback.

### 4. Propose the concrete improvement

Each finding should name the specific change: which reference/section to add or
correct, which anti-pattern or stop signal to add, and — importantly — a
proposed **eval scenario** (a short prompt plus what the answer must and must not
contain) so the improvement is testable. This mirrors how the skill is already
tested in `tests/dotnet-maui/native-library-bindings/eval.yaml`.

### 5. Report, then optionally file

Default to a **markdown report only**. File a GitHub issue only when the user
asks and `gh` has access to `dotnet/maui-labs`.

## PII and secret scrub (before writing or filing anything)

Binding sessions frequently touch secrets and private identifiers. Before you
save a file or file an issue, remove or generalize:

- names, usernames, emails, handles, or account IDs
- session IDs, transcript IDs, local file paths, home-directory fragments,
  machine names, and artifact paths
- **download tokens, API keys, and credentials** (for example the value of
  `MAPBOX_DOWNLOADS_TOKEN`, a `mapbox_access_token`, `~/.gradle/gradle.properties`
  contents, `.netrc` entries, keystore passwords, or CI secrets) — never echo a
  real value; refer to the variable name only
- private Maven/registry/package-feed URLs, internal repo URLs, and private
  package/app names when they are not public
- request/response bodies, screenshots, and user-authored private text

Paraphrase private transcript content. If a finding cannot be explained without a
secret or private identifier, generalize it (for example "a private Maven feed
that requires a token") or omit it.

## Safe file handling

- Write the markdown report only to a path the user approves; default to the
  current working directory or the session artifacts folder, never to a
  system/shared location.
- Do not write tokens, credentials, keystores, or transcript dumps to disk.
- Do not overwrite unrelated files; use a clearly named report file such as
  `binding-skill-feedback-<date>.md`.
- Re-run the scrub on the final file contents before considering it done.

## Markdown report template

```markdown
# Native binding skill feedback

## Summary

{Scope, session count, and the top skill-improvement opportunities.}

## Context (sanitized)

- Platform(s): {Apple/Android + which heads}
- SDK family / distribution shape: {SPM/CocoaPods/xcframework/Maven/AAR/direct, no private names}
- Strategy used: {slim / full / reuse existing NuGet}
- .NET MAUI / SDK versions: {values or Unknown}

## Findings

### 1. {short title}

- Category: {Missing case / Wrong or stale guidance / Missing detail / Missing script or asset / Unclear routing}
- What the skill said: {guidance followed, or "not covered"}
- What actually worked: {sanitized reality / workaround}
- Proposed skill change: {reference section to add/fix, anti-pattern, script, asset}
- Proposed eval: {one-line prompt + must-contain / must-not-contain}
- Confidence: {Confirmed / Likely / Possible / Unknown}

## Suggested GitHub issues

{Issue titles, or "None requested".}
```

## GitHub issue template

Use this shape when the user asks to file. Title issues as skill improvements,
not SDK bugs.

```markdown
## Skill improvement

Skill: native-library-bindings

{One paragraph: what binding scenario exposed the gap and why the skill should cover it.}

## Context (sanitized)

- Platform(s): {value}
- Distribution / acquisition shape: {value, no private names or tokens}
- Strategy: {slim / full / reuse}
- .NET MAUI / SDK versions: {value or Unknown}

## What the skill did

{Guidance the skill gave, or that the case was not covered.}

## What actually worked

{Sanitized reality or workaround. No secrets, tokens, paths, or private names.}

## Proposed change

- Reference/section: {add or correct}
- Anti-pattern / stop signal: {add, if applicable}
- Script / asset: {add or fix, if applicable}

## Proposed eval

- Prompt: {short scenario prompt}
- Must contain: {key guidance the answer needs}
- Must not contain: {the wrong path to guard against}
```

## Filing guidance

- Ask before filing. Markdown-only is the default.
- Confirm `gh` is authenticated and can access `dotnet/maui-labs`; if not, keep
  the markdown for manual filing.
- Prefer one issue per distinct skill improvement; merge duplicate evidence.
- Label or title clearly as a skill improvement (for example prefix
  `skill: native-library-bindings —`) so maintainers can triage into evals,
  references, scripts, or assets.
- Never include secrets, tokens, private URLs/names, session IDs, file paths, or
  transcript excerpts.

## Title patterns

Good, improvement-facing titles:

- `skill: native-library-bindings — add credentialed Maven acquisition eval + reference detail`
- `skill: native-library-bindings — pure-Swift SDK wrapper routing was unclear`
- `skill: native-library-bindings — AndroidMavenLibrary transitive guidance needs a regression eval`

Avoid vague or blame titles: `binding skill is wrong`, `Sharpie failed`,
`session was bad`.

## Stop signals

- The top skill-improvement opportunities are captured with a proposed change
  and a proposed eval each.
- The markdown report is written after a scrub, or the requested issues are filed
  after a scrub.
- Remaining evidence would require scanning outside the approved scope.
- A finding is a native-SDK bug or environment problem, not a skill gap — exclude
  it from this feedback.
