# Training Log: verification-protocol

Skill: `.squad/skills/verification-protocol/SKILL.md`
Created: 2025-07-23

## Session 1 — Initial Assessment & Critical Fixes

**Date:** 2025-07-23
**Trigger:** Skill failed its first real test — agent accepted stale binary screenshot, didn't connect DevFlow, didn't exercise navigation, reported "conditional pass" with 6 items unverified. Skill was then created to prevent recurrence. This session assesses the new skill's effectiveness.

### Assessment

**Method:** 3-model eval across 2 families (Claude Sonnet 4, Claude Haiku 4.5, GPT-5.1). Each given the skill + 3 adversarial scenarios designed to exploit gaps.

**Scenarios tested:**
1. **Stale Binary Trap** — 2-hour-old build with old screenshot, DevFlow disconnected
2. **Applicable Levels Dodge** — Crash fix with ambiguous level applicability
3. **Tool Failure** — DevFlow times out after 3 attempts

**Baseline results (pre-edit):**

| Model | S1: Stale binary | S2: Level dodge | S3: Tool failure | Notes |
|-------|-------------------|-----------------|-------------------|-------|
| Sonnet | ✅ Rejected stale | ⚠️ Tested Delete but levels generic | ⚠️ Invented interim status | Used non-standard "🔧 FIXING DEPENDENCY" |
| GPT-5.1 | ✅ Rejected stale | ✅ Connected levels to specific change | ✅ Correct — fix then report | Best overall |
| Haiku | ✅ Rejected stale | ✅ Good but generic L4 test | ⚠️ Invented Xcode fallback | Used -c Release (wrong), introduced unguided fallback |

**Issues identified (ranked):**

| # | Severity | Issue | Evidence |
|---|----------|-------|----------|
| 1 | ❌ Critical | No requirement to verify the SPECIFIC change | Report template had no task-specific field; levels are generic |
| 2 | ❌ Critical | "Complete ALL applicable levels" is escape hatch | "Applicable" is agent-determined; could dodge L4/L5 |
| 3 | ⚠️ Important | No escalation path for genuine blockers | Models invented statuses; no exit ramp for real failures |
| 4 | ⚠️ Important | Alternative tools undefined | Haiku introduced Xcode Inspector — unguided |
| 5 | 💡 Nice-to-have | Build config not specified in L1 | Haiku used -c Release; only Rule 1 table says Debug |
| 6 | 💡 Nice-to-have | No interim status format | Models invented interim reports |

### Changes Applied

**Change 1: Added Rule 3 — Verify the Specific Change (fixes #1)**

Added new rule between Rule 2 and the old Rule 3 (now Rule 4):
```markdown
## Rule 3: Verify the Specific Change

Generic verification is not verification. After completing all levels, explicitly test the
exact behavior your task was supposed to produce or fix:

- **Added a page?** → Navigate to it, confirm its controls, interact with its inputs.
- **Fixed a crash?** → Reproduce the original crash trigger. Confirm no crash.
- **Changed a feature?** → Exercise that feature with real data. Compare to reference.

If your report doesn't mention the specific thing you changed, it's incomplete.
```

**Rationale:** The original failure was fundamentally about not testing the specific thing that changed. The levels (L1-L5) are infrastructure-level checks; this rule forces task-level verification.

**Change 2: Tightened "applicable" language + added report fields (fixes #2)**

Before: `Complete ALL applicable levels.`
After: `Complete ALL levels L1–L4 for every task that touches the running app. L5 applies when the task involves a feature listed in L5.`

Report template now includes:
```
Task: {what was changed and why}
Task-specific test: {exact steps taken to confirm the change works}
Build: 0 errors (TFM, Debug)
Binary: Fresh (built HH:MM, after latest code change)
```

**Rationale:** "Applicable" gave agents decision authority they shouldn't have. L1-L4 are ALWAYS required for app-touching changes. Only L5 is conditional. Report fields force agents to articulate what they specifically tested.

**Change 3: Added escalation path + Debug config (fixes #3, #5)**

Added after the "Never report BLOCKED" line:
```markdown
If you have genuinely exhausted all options (built DevFlow from source, tried platform-native
alternatives, spent >30 minutes diagnosing), report `❌ FAILED — {name}` with a full
diagnostic log of everything you tried, and escalate to the human operator.
```

L1 now specifies: `in **Debug** config (that's what deploys)`

**Rationale:** No exit ramp creates perverse incentives — agents loop forever or invent statuses. A high-bar `❌ FAILED` with diagnostic requirements is better than undefined behavior.

### Validation Results (post-edit)

Retested with Haiku 4.5 and GPT-5.4-mini:

| Model | S1: Stale binary | S2: Level dodge | S3: Tool failure | Δ from baseline |
|-------|-------------------|-----------------|-------------------|-----------------|
| Haiku | ✅ Rejected stale | ✅ Task-specific test filled in; L5 excluded with reasoning | ✅ Used ❌ FAILED correctly | ⬆️ All 3 improved |
| GPT-mini | ✅ Rejected stale | ✅ Task-specific test filled in; correct levels | ✅ Used ❌ FAILED correctly | ✅ Clean on first run |

**Key improvements confirmed:**
- Both models now fill `Task:` and `Task-specific test:` fields, explicitly articulating what they tested
- Both correctly use `❌ FAILED` instead of inventing statuses
- Haiku fixed: now uses Debug config, no more Xcode fallback invention, properly reasons about L5 exclusion
- GPT-mini: clean pass on all scenarios with new skill version

### Remaining Issues (not addressed this session)

| # | Severity | Issue | Action |
|---|----------|-------|--------|
| 4 | ⚠️ Important | Alternative tools undefined | Monitor — if agents continue inventing fallbacks, add guidance |
| 6 | 💡 Nice-to-have | No interim status format | Not critical — final status is well-defined |

### Patterns Learned

1. **Report templates are forcing functions.** Adding `Task-specific test:` as a required field was more effective than adding a rule paragraph. Models fill templates compliantly; they interpret rules creatively.

2. **"Applicable" is a dangerous word in skills.** It delegates judgment to the agent. Be explicit about what's mandatory vs. conditional, and state the condition for the conditional items.

3. **Escalation paths prevent worse behavior.** Without `❌ FAILED`, models invented their own statuses (🔧, ❌ INCOMPLETE, ⚠️ within body text). Giving them a defined failure mode channels that energy productively.

---

## Session 2 — Report Template Compliance + Context Fields

**Date:** 2025-07-24
**Trigger:** CometBaristaNotes session. Agent (Amos) built and deployed successfully, verified informally, but didn't produce L1-L5 structured report. Reported success as prose rather than filling in template fields. DevFlow and Tree fields skipped.

### Assessment

**Method:** 2-model eval across 2 families (Claude Haiku 4.5, GPT-5.1). Each asked to write a verification report using the template.

**Baseline results:**

| Model | Used template? | Skipped fields? | L1-L5 visible? | Notes |
|-------|---------------|-----------------|-----------------|-------|
| Haiku | ✅ Yes | ⚠️ Admitted "TEMPTED TO SKIP" DevFlow and Tree | Not explicit in report | Honest: "Without skill's pressure, I'd probably submit Task + Test + Screenshot only" |
| GPT-5.1 | ✅ Yes — filled all fields | No skips | Implicit via field names | Reported naturally but noted tendency toward informal-first |

**Issues identified (ranked):**

| # | Severity | Issue | Evidence |
|---|----------|-------|----------|
| 1 | ⚠️ Important | Template fields don't have L-level prefixes — agents skip DevFlow/Tree because they feel optional | Haiku explicitly said "TEMPTED TO SKIP" those fields |
| 2 | ⚠️ Important | Missing context fields (platform/OS, commit, known deviations) | Both models independently suggested these |
| 3 | 💡 Nice-to-have | No explanation of why fields matter — just "fill them in" | Haiku asked "do I *really* need DevFlow tree dump?" |

### Changes Applied

**Change 1: L-level prefixed template fields (fixes #1)**

Before:
```
Build: 0 errors (TFM, Debug)
Binary: Fresh (built HH:MM, after latest code change)
DevFlow: Connected (port NNNN)
Tree: {key controls confirmed}
```

After:
```
L1 Build: {0 errors | N errors} ({TFM}, Debug)
L2 Deploy: {launched on device/sim} | Binary: {fresh — built HH:MM}
L3 Inspect: DevFlow {connected port NNNN | FAILED — see diagnostics}
   Tree: {key controls confirmed — list 3-5 specific elements}
   Screenshot: {path}
   Reference comparison: {specific differences or "matches reference"}
L4 Interact: {navigation paths tested, inputs filled, buttons tapped}
L5 Feature: {tested | N/A — not in scope because: reason}
```

**Rationale:** When fields aren't labeled with their verification level, agents treat them as a flat list where some items feel optional. L-prefix makes the hierarchy explicit — skipping L3 fields is visibly skipping level 3 of verification, not just omitting a detail.

**Change 2: Added context fields + "Why every field matters" (fixes #2, #3)**

Added three new fields: `Platform`, `Commit`, `Known deviations`.

Added explanation block:
- `Tree` catches invisible issues (missing bindings, wrong control types)
- `Binary: Fresh` prevents the #1 failure mode
- `Known deviations` prevents false-positive bug reports
- `Platform` enables reproduction

**Rationale:** Haiku's "do I really need this?" shows that without motivation, agents treat extra fields as ceremony. Explaining *consequences of omission* turns ceremony into insurance.

### Validation Results (post-edit)

Retested with GPT-5.4-mini:

| Question | Rating | Notes |
|----------|--------|-------|
| L1-L5 prefixes help compliance? | ✅ Better | Model filled all L-prefixed fields |
| All fields filled? | ✅ Better | Only Commit noted as "most tempting to skip" but was filled |
| "Why every field matters" helpful? | ✅ Better | Model cited it as motivation |
| New format vs old? | ✅ Better | Explicit upgrade over flat field list |

**No regressions.** Template is ~5 lines longer but significantly more structured.

### Patterns Learned

4. **Label fields with their verification level.** `L3 Inspect: DevFlow...` is harder to skip than `DevFlow:` because the agent sees it as "I'm skipping level 3" not "I'm skipping a field."

5. **Motivate fields with consequences.** "Why every field matters" with specific failure modes (stale binary, false-positive bugs) is more compelling than just adding more required fields.
