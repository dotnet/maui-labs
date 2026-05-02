---
name: verification-protocol
description: >
  Mandatory end-to-end verification for .NET MAUI and Comet app tasks. Enforces dependency
  ownership — broken tools and missing config are YOUR problem to fix, not blockers to report.
  USE FOR: any task touching UI, pages, navigation, services, or app behavior. Triggers: after
  code changes, before marking todos done, when reporting task results, when a tool fails.
  DO NOT USE FOR: documentation-only changes, squad config edits, or build system changes
  that don't affect the running app.
---

# Verification Protocol

**Verification** means proving that code changes produce the intended result on a real device,
against reference material, through interactive exercise — not just proving they compile.
A fresh binary is built and deployed, the UI is inspected via DevFlow visual tree, navigation
is exercised interactively, features are tested with real inputs, and results are compared
against the original BaristaNotes app code and reference screenshots. A broken tool in this
chain is a problem to solve, not a reason to skip a step.

No task is done until verified on device. "It builds" is not done. A screenshot of a stale
binary is not done.

## Rule 1: Own Your Dependencies

When a tool or dependency is broken, **fix it**. The solution is in the repo, the reference
code, or the documentation you were given.

| Problem | Wrong | Right |
|---------|-------|-------|
| DevFlow won't connect | "⚠️ Skipped — agent didn't connect" | Diagnose: `maui-devflow diagnose`. Check csproj integration, broker status, entitlements, ports. Fix the issue. DevFlow source is in this repo — build a custom version if needed. |
| API key missing | "⚠️ Needs API key" | Read the reference app's code. It shows exactly how to source the key (IConfiguration, appsettings.json, env vars). Do what it does. |
| Simulator issue | "⚠️ No simulator available" | `xcrun simctl create "Verify" "iPhone 16 Pro"` then boot it. |
| Build fails on iOS TFM | "✅ Builds on Release" | Fix the iOS Debug build. That's the one that deploys. |

**If you report "blocked" and the answer is in the repo — that is a failure.**

## Rule 2: Verify Against Reference

When reference material exists (original app code at `~/work/BaristaNotes`, screenshots at
`~/Downloads/baristanotes-screenshots/`), compare your result against it. Do not check if
"it looks reasonable."

## Rule 3: Verify the Specific Change

Generic verification is not verification. After completing all levels, explicitly test the
exact behavior your task was supposed to produce or fix:

- **Added a page?** → Navigate to it, confirm its controls, interact with its inputs.
- **Fixed a crash?** → Reproduce the original crash trigger. Confirm no crash.
- **Changed a feature?** → Exercise that feature with real data. Compare to reference.

If your report doesn't mention the specific thing you changed, it's incomplete.

## Rule 4: Confirm Fresh Binary

Before accepting any on-device result:
1. `dotnet clean` then `dotnet build` — stale binaries lie.
2. If a deleted page still appears, the binary is stale. Rebuild.
3. Use `maui-devflow MAUI platform app-info` to verify build timestamp.

## Verification Levels

Complete ALL levels L1–L4 for every task that touches the running app. L5 applies when the
task involves a feature listed in L5. Never skip because a tool is "unavailable" — fix the tool.

**L1 Build** — `dotnet build` on target TFM in **Debug** config (that's what deploys), 0 errors. If framework was modified, run `dotnet test`.

**L2 Deploy** — App launches on device/simulator. No runtime crash. Binary confirmed fresh.

**L3 Inspect** — DevFlow connected (`maui-devflow wait`). Visual tree dumped (`MAUI tree --depth 15`). Screenshot taken (`MAUI screenshot`). Compared against reference screenshots.

**L4 Interact** — All tabs navigated. Forms accept input (`MAUI fill`). Buttons tappable (`MAUI tap`). Push/pop navigation works. Data persists after save.

**L5 Feature** — Dark mode toggle renders correctly. AI features work with configured credentials. Filters produce correct results. Empty states display.

## Reporting

Every verification report MUST use this exact template. Fill in every field — blank fields mean
unverified levels. The coordinator uses these fields to decide if the task is done.

```
✅ VERIFIED — {name}
  Task: {what was changed and why}
  Task-specific test: {exact steps taken to confirm the change works}
  L1 Build: {0 errors | N errors} ({TFM}, Debug)
  L2 Deploy: {launched on device/sim} | Binary: {fresh — built HH:MM}
  L3 Inspect: DevFlow {connected port NNNN | FAILED — see diagnostics}
     Tree: {key controls confirmed — list 3-5 specific elements}
     Screenshot: {path}
     Reference comparison: {specific differences or "matches reference"}
  L4 Interact: {navigation paths tested, inputs filled, buttons tapped}
  L5 Feature: {tested | N/A — not in scope because: reason}
  Platform: {device/simulator model, OS version}
  Commit: {short SHA or branch name}
  Known deviations: {intentional differences from reference, or "none"}
```

**Why every field matters:**
- `L3 Inspect > Tree` catches invisible issues (missing bindings, wrong control types) that screenshots miss
- `Binary: Fresh` prevents the #1 verification failure: testing stale code
- `Known deviations` prevents the coordinator from filing bugs for intentional changes
- `Platform` enables reproducing issues on the same device

Never report `⚠️ BLOCKED`, `⚠️ SKIPPED`, or `⚠️ CONDITIONAL PASS`. Those mean you
didn't own the problem. If you have genuinely exhausted all options (built DevFlow from
source, tried platform-native alternatives, spent >30 minutes diagnosing), report
`❌ FAILED — {name}` with a full diagnostic log of everything you tried, and escalate
to the human operator. This should be rare — the answer is almost always in the repo.
