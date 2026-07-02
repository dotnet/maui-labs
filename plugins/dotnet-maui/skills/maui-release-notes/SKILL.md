---
name: maui-release-notes
description: >-
  Turn official .NET/MAUI release notes into app upgrade plans. USE FOR: app-team upgrade guidance, breaking changes, Xcode/JDK/Android SDK requirements, `global.json`, workload install/update `--version`, CI image changes, package/workload updates, validation checklists, `dotnet-workload-info` for exact versions. DO NOT USE FOR: SDK maintainer notes, publishing tables, or store copy.
---

# MAUI Release Notes

Use this skill when an app team needs to answer: "What does this .NET or MAUI
release mean for our app?" Focus on adoption guidance for app developers, QA,
and CI owners rather than on shipping the MAUI SDK itself.

## Response Checklist

- Keep the audience app-focused: repo owners, app developers, QA, and CI owners.
- Convert release notes into concrete app actions (SDK/workload/tooling updates,
  risks, and validation plan), not maintainer publishing tasks.
- Use `dotnet-workload-info` when exact workload or environment dependency data
  is needed.

## Typical Inputs

- The app's current .NET version, workload set, and MAUI package baseline.
- The target release the team is considering.
- Platforms in scope: Android, iOS, Mac Catalyst, macOS, Windows.
- Whether the output should be a short summary, an upgrade checklist, or rollout
  guidance.

If exact workload, package, Xcode, JDK, or Android SDK version data is missing,
use `dotnet-workload-info` to confirm it. Use live version discovery to support
app upgrade planning, not to dump maintainer-only manifest data unless the user
explicitly asks for it.

## What to Extract from Release Notes

1. Breaking changes or deprecations that affect app code, project files, or
   deployment.
2. Required environment updates such as Xcode, JDK, Android SDK, Visual Studio,
   or workload-set changes that affect building or shipping the app.
3. MAUI package, workload, or CI updates the app repo needs.
4. Known issues, regressions, or unsupported combinations that could block the
   upgrade.
5. Validation work the app team should plan after the upgrade.
6. Rollback considerations if the new release introduces app-specific risk.

## Recommended Workflow

1. Establish the app's current baseline: target framework, workloads, packages,
   CI image/tooling, and supported platforms.
2. Read the official .NET and MAUI release notes for the target release.
3. Pull only the changes that are relevant to app builders: environment
   requirements, app-facing breaking changes, tooling shifts, and known issues.
4. Use `dotnet-workload-info` when the team needs exact upgrade commands or
   authoritative workload dependency requirements.
5. Translate the release into app-specific actions, risks, and tests instead of
   repeating framework internals.

## Suggested Output Shape

````markdown
# Upgrade notes for <app/team> adopting .NET MAUI <version>

## Should we upgrade now?
- Recommendation
- Main benefits
- Main risks or blockers

## What changes for our app?
- Breaking or behavior changes that touch app code, project files, CI, or dependencies

## Required environment updates
- Xcode / JDK / Android SDK / workloads / IDE requirements

## Repo and CI changes
- global.json, workload install/update, package pinning, CI image/tooling updates

## Validation checklist
- Clean restore/build
- Device or simulator smoke tests
- Critical app flows such as auth, notifications, payments, deep links, or media
- Archive, signing, and store submission checks if relevant

## Known issues and mitigations

## Rollback plan
````

Only include manifest or package-version tables when the user explicitly asks
for that level of detail.

## Guardrails

- Do not write from the perspective of MAUI workload maintainers.
- Do not default to manifest tables, package internals, or repo publishing notes
  unless the user asks for them.
- Keep App Store or Play Store marketing notes separate from framework upgrade
  notes.
- Do not recommend an upgrade until platform and tooling prerequisites are
  confirmed.
- Prefer official release notes and linked issues over memory or guesses.

## Validation Checklist

- The output is framed for app builders, QA, and CI owners.
- App-facing changes are called out separately from framework internals.
- Environment prerequisites match the platforms the app ships.
- Upgrade commands and CI guidance align with the target release.
- Known issues, mitigations, and validation steps are concrete enough for a real
  app rollout.
