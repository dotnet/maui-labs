# DevFlow authoring-time measurement protocol

**Protocol:** `maui-devflow-authoring-time`
**Protocol version:** 1
**Status:** defined, **no data collected yet**

Goal #4 claims DevFlow will be evaluated on "authoring time … across real applications and
platforms." This document defines how that number is to be produced. It is written before any
sessions have been run so that the design cannot be chosen after seeing the result.

## The honest statement up front

**Without an unassisted control arm the authoring-time number is meaningless.**

The Test Workbench can already measure how long a session took: `timeToGoalMs`,
`recordingDurationMs`, `reviewToSaveDurationMs`, `timeToFirstResultMs`. Every one of those is a
description of a session *conducted inside DevFlow*. None of them is evidence that DevFlow made
authoring faster, because there is nothing to be faster *than*. "Authors saved a test in four
minutes" is compatible with DevFlow halving authoring time and with DevFlow doubling it.

A single-arm number is also actively misleading in a specific way: it is measured on people who
chose to open the tool, on a task they were told to do, with the tool's own instrumentation
deciding when the clock starts. Publishing it as "authoring time" invites the reader to supply
the missing comparison themselves, and they will supply a favourable one.

So: `maui devflow flow study` reports `insufficient-evidence` and emits **no** headline duration
until both arms are present at the required counts, and the assisted-only case carries the
explicit sentence that assisted durations are not evidence of a reduction. This is enforced in
`MauiAuthoringStudyProtocol.Aggregate`, not left to the reader.

## Arms

| Arm | Identifier | What the participant has |
|---|---|---|
| Assisted | `assisted` | DevFlow Test Workbench: record, selector proposals, improve scan, repair proposals |
| Unassisted control | `unassisted-control` | The same app, the same task, their normal editor and test runner, no Workbench |

Both arms author a test that satisfies the same written acceptance criteria for the same task
against the same app build on the same platform. The control arm is not "no tooling" — it is
"the tooling they have today". Comparing against a deliberately crippled baseline would be the
same dishonesty in the other direction.

Assignment is **counterbalanced within participant**: each participant performs some tasks
assisted and some unassisted, in an order that is rotated across participants, so that
participant skill and task difficulty do not load onto one arm. A participant must never perform
the same `taskId` in both arms — the second attempt measures memory, not tooling.

## Fixed task set

The task set is fixed in `PROTOTYPE_STUDY_TASK_IDS` (`inspector-study.js`) and
`MauiAuthoringStudyProtocol.TaskIds`. Adding, removing, or editing a task is a protocol version
bump; sessions from different protocol versions are never pooled.

| Task id | Goal | Acceptance criteria |
|---|---|---|
| `task-01-first-run-smoke` | Author a test that launches the app and asserts the landing page rendered | Test saved; passes on a clean first attempt |
| `task-02-form-entry-assertion` | Fill a form field and assert the resulting displayed value | Test saved; at least one hard assertion on the resulting value |
| `task-03-navigation-round-trip` | Navigate to a detail page and back, asserting both states | Test saved; asserts on both the detail and the returned-to page |
| `task-04-list-scroll-select` | Scroll a list to an off-screen item and select it | Test saved; asserts the selected item's detail state |
| `task-05-repair-a-broken-selector` | Given a supplied test that fails because a selector no longer matches, make it pass without weakening its assertions | Test passes; assertion count and hardness not reduced |

Task 05 is the only task where the two arms are doing visibly different work, and it is included
deliberately: repair is where assisted authoring is most likely to help and also most likely to
produce a wrong-but-green test. Its acceptance criteria therefore forbid weakening assertions.

## Timing definition

The clock is the participant's own session journal, not a stopwatch held by the operator.

- **Start:** the `goal-defined` event in the assisted arm. **In the control arm there is currently
  no capture path at all.** `inspector-study.js` — the Test Workbench instrumentation — is the only
  session producer in this repository, and it is the assisted tool. An operator running the control
  arm today would have to hand-author the export JSON. No control-arm journal shim exists, and no
  control session has ever been recorded.
- **`timeToGoalMs`:** start → the participant states the goal. This is a *reading* interval and
  is expected to be similar across arms; a large difference indicates a task-card problem, not a
  tooling effect.
- **`timeToFirstResultMs`:** start → first terminal run result. This is the primary comparison,
  and `timeToFirstResultSampleCount` reports how many sessions actually carried it — an arm whose
  primary-endpoint sample is below the session minimum is blocked, not published.
- **Completion:** a session counts as completed only if a test was saved (`savedTestMetrics`
  present). `completedTasks` is reported per arm. **Abandoned sessions still contribute their
  durations to the medians**, which biases the medians in the favourable direction ("faster because
  they gave up"); no completion-rate *difference* is computed today. Read `completedTasks`
  alongside every median.

> **The arm is a self-declared `?studyArm=` query parameter.** Nothing validates that a session
> labelled `unassisted-control` was produced without the tool — the same Workbench instrumentation
> emits both. Until a separate control-arm capture path exists, an arm label is an assertion by the
> operator, not evidence.

Sessions whose journal reports `completeEventHistory` in `missingFields` (the event cap
discarded entries) are **rejected**, not truncated-and-used. Any rejection makes
`maui devflow flow study` exit nonzero, so an aggregation job cannot drop sessions and still
report success; the report is still written so the operator can see what was dropped and why.

## Participant salt

`participantSalt` is a value of the form `participant-<8-64 hex>`, generated **offline by the
operator** and handed to the participant with their task card. It is not derived from any
attribute of the person or machine. A session whose salt is not that exact shape is rejected with
`study-session-participant-salt-invalid`; surrounding whitespace is trimmed first, because salts
are compared ordinally and a trailing newline picked up from a text-file round-trip would otherwise
split one person into two participants and silently empty the cross-arm blocker.

Its only purpose is to let two sessions be known to come from the same participant so that
counterbalancing works and so that one fast participant contributing ten sessions cannot look
like ten participants. `MinimumParticipantsPerArm` is enforced separately from
`MinimumSessionsPerArm` for exactly this reason.

The mapping from salt to person is held by the operator outside this repository and is not part
of any exported artifact. The exported envelope contains the salt and nothing else identifying;
the pre-existing per-session `redactionSalt` continues to opaque-ify run, approval, and proposal
ids.

A session without a participant salt is **rejected** by the aggregator
(`study-session-participant-unlinkable`) rather than counted as an anonymous extra data point, and a
salt that does not match `participant-<8–64 lowercase hex>` is rejected as
`study-session-participant-salt-invalid`. That format check matters most for the control arm, whose
exports are hand-authored: two cosmetically different salts for one person would inflate the
participant count and slip past the both-arms blocker.

## Recording and export

1. The operator opens the Test Workbench with the assignment in the query string:
   `?studyParticipant=participant-<hex>&studyTask=task-02-form-entry-assertion&studyArm=assisted`
   The journal stamps `protocolVersion`, `taskId`, `arm`, and `participantSalt` into the session
   at creation.
2. The assignment can only be stamped **before the first recorded event**. `assign()` refuses
   with `session-already-has-evidence` afterwards. Re-arming a session after seeing how it went
   is the most obvious way to bias this study, so it is not possible through the API.
3. At the end of the session the participant uses the existing evidence download. The exported
   envelope now carries a `protocol` block with `eligibleForAggregation` and, when false,
   `ineligibleReasons`.
4. The operator collects the downloaded files into a directory and aggregates:

```powershell
maui devflow flow study --session-dir .\study-sessions --study-out .\study-report.json
```

Add `--fail-on-insufficient` in CI or a scripted report to make "we do not have enough evidence"
an error rather than a paragraph nobody reads.

## Reporting thresholds

| Threshold | Value | Why |
|---|---|---|
| `MinimumSessionsPerArm` | 5 | Below this a median is a single person's day |
| `MinimumParticipantsPerArm` | 3 | Stops one participant's repeated sessions standing in for a sample |

A task is `comparable` only when **both** arms clear **both** thresholds, both have a recorded
`timeToFirstResultMs`, and **no participant appears in both arms** (a participant who has already
done the task carries that knowledge across, so the difference would measure learning rather than
tooling). Otherwise the task reports `insufficient-evidence` with explicit `blockers`.

The reported difference is `medianTimeToFirstResultDifferenceMs` — the primary comparison.
`medianTimeToGoalDifferenceMs` is reported beside it for completeness but must not be read as a
tooling effect: goal time includes the participant reading the task card.

These thresholds are a floor for *reporting anything at all*, not a claim of statistical power.
The aggregator emits medians and a median difference. It does **not** emit a p-value, a
confidence interval, or the word "significant", because at n=5 per arm it would not be entitled
to. Anyone wanting an inferential claim needs a larger, pre-registered study; this protocol is
the minimum honest instrument, not the final one.

## What this protocol still does not establish

- **External validity.** Five scripted tasks on a sample app are not "real applications and
  platforms". Results transfer to real codebases only as a hypothesis.
- **Novelty and observation effects.** Participants know they are being measured and most will
  be new to the Workbench. Both effects push in unknown directions.
- **Test quality.** Faster authoring of a worse test is not an improvement. The acceptance
  criteria constrain quality crudely; `durableSelectorRatio` and the assertion counts in the
  session summary are reported alongside duration and should be read together with it.
- **Causality.** Even a clean two-arm difference on this design is an association under a
  specific task set, not a general causal claim about authoring.

## Current recorded value

**None.** No sessions have been collected under protocol version 1. `maui devflow flow study`
with no inputs reports `insufficient-evidence`, zero sessions in both arms, and the statement
that assisted-arm durations are not evidence of a reduction. That is the accurate state of this
metric today, and it should stay visible until real sessions exist.
