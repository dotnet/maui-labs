# Local evaluation record

Date: 2026-10-01. This records actual local executions, not a claim that the
skill or a compatible CLI has been published. Evaluation was completed
local-only before the user separately requested a draft PR. No global tool
installs, global workload changes, or global Xcode selection changes were made.

## Method and reproducibility

Used Anthropic's skill-creator draft/evaluate/grade/analyze/revise workflow,
grading schema, description-optimization guidance, benchmark aggregator and
static review viewer from `anthropics/skills` revision
`8a1541c4a3ffa5a20a5a91de0dcf3f0bab1d1ef4`.
Claude Code was unavailable, so the execution adapter ran real isolated
Copilot CLI sessions, with its default model (observed `claude-sonnet-5`).
This was not execution of the Claude-specific optimization script.

The runner records prompts, exact commands, successful skill-tool events,
responses, project outputs, hashes, elapsed time and assertions. No model
override or keyword-only trigger simulation was used. Token usage was
unreported, not zero. One run per case/configuration is not a reliability
estimate. Both migration configurations could research official sources.

Reproduction commands are in [README.md](README.md). Raw artifacts for this
execution are in the requesting session's `files/` directory under
`migration-evals/`, `trigger-evals/`, `builds/`, `xcode-distribution/` and
`review/`. They are deliberately not shipped in the plugin.

## Completed checks

| Check | Actual result |
|---|---|
| Repository skill-validator | 24 app-plugin skills, zero errors. Advisory warnings: skill size and numbered headings not recognized as numbered steps. |
| Python oracle regression tests | 11 passed. These test the grader and fixtures, not agent migration behavior. |
| CLI command/distribution integration | 30 passed, including real local marketplace discovery/install/status/update. Final content rerun: 3 passed across CopilotCli, Claude and VsCode. |
| Natural-language trigger development set | 20/20 with the complete 24-skill app catalog and successful tool-event evidence. |
| Fresh held-out trigger set | 8/8 with the complete catalog; not reused for description tuning. |
| Actual simple migration, iteration 3 | 16/16 structural/execution checks. Both Apple heads migrated. |
| Actual central-package migration, iteration 3 | 17/17; central owner and higher OS minima preserved. |
| Actual custom repair, iteration 7 | 22/22, successful skill invocation; manual review confirms activation, separate observer/custom acknowledgements, warm URL/activity paths, base calls, and per-scene deferred cold URLs/activities on both heads. Actual unchanged output fully compiled with the supplemental toolchain. |
| No-skill custom baseline, iteration 5 | 18/22. Reused for the final comparison because the fixture and natural prompt are unchanged; this was not a simultaneous rerun. |
| Actual no-skill simple/central baselines | 15/15 and 16/16 under the earlier scorer, which omitted the candidate's activation assertion. They also succeeded; these results do not show a basic-migration improvement. |
| Fresh audit/safety executions | 7/7 cases, 21/21 checks. Older OS policy, .NET 9, .NET 11 RC1, no Apple heads, unrelated UI, Xcode mismatch and held-out audit. Full project-file hashes, including injected skills, workflows and generated bin/obj, unchanged. Responses manually reviewed. |
| Simple idempotency | 2/2: a second real agent invoked the example-inclusive skill on a copy of the actual migrated project; all project-file hashes remained unchanged, including injected skills/workflows and any generated outputs. This preceded the final custom cold-activity clarification. |
| Simple and central full iOS simulator builds | Passed unchanged agent outputs using the separately approved custom 27.1 toolchain. Supplemental only; see limitations. |
| Ordinary iPhone startup | Simple migrated output rendered its label beyond splash on iPhone 17/iOS 27.0; process survived 12 seconds. Screenshot and runtime log retained; simulator returned to Shutdown. |

The final review combines simple/central iteration 3 with custom iteration 7
and the existing custom iteration 5 baseline. Exact origins are retained in
`review/final-input/origins.json` and `review/final/input-provenance.json`;
individual executions retain commands and skill hashes where recorded.
This is a mixed-revision review, not a uniform benchmark. The description
remained unchanged after the final 28 trigger tests; later revisions tightened
execution guidance. Final custom success is one execution after repeated
failures, not evidence of reliable one-shot behavior across arbitrary projects.
Earlier custom runs are not accepted.

The official static viewer is `review/final/review.html`. It includes the
actual responses/source bundles and a separate final custom qualitative review
without inflating the structural assertion scores.

## Iteration findings retained in the evidence

Initial trigger tests exposed only the skill tool, skewing decisions. A later
runner mistake installed competing skills into migrations instead of trigger
fixtures. Both were corrected, catalog-isolation tests added, and the final
28 trigger cases executed with the correct catalog.

Initial custom fixtures used an invalid activation override and a nondefault
scene configuration without a window-creation path. Those are invalid-input
runs, not migration successes. Corrected fixtures retain a custom registered
delegate, the MAUI default configuration name, and intentional multiwindow.
The custom fixture is therefore a **partially migrated scene app with leftover
application callbacks**, not a pristine legacy app. Its AppDelegate activation
was already inactive under scenes. Repair is required by the prompt's explicit
callback audit/preservation request; leaving it unmapped is incomplete repair,
not evidence that the agent introduced an activation regression.

Real custom migrations subsequently exposed dropped callback/observer behavior,
missing observer completion, missing cold user activities, and an invalid
non-generic NSSet LINQ call (`CS0411`). The skill now has explicit behavior
gates, exact tagged MAUI signatures, typed collection guidance and an additive
custom-lifecycle reference. The reference's complete abstract scene class was
extracted unchanged into an isolated diagnostic project and fully compiled
with the supplemental toolchain; it forces app-specific handlers instead of
providing empty stubs. A manually
corrected diagnostic-copy build is not counted as an agent-output pass.

An early audit changed scene files despite preserving OS minima. Another audit
ran a build without editing source. The skill now prohibits all audit edits and
restore/build, and the safety oracle includes generated files. Earlier snapshots
that omitted workflow/skill/build paths cannot establish full no-file-change
behavior retroactively.

## Environment and coverage limits

The released pairing is SDK 10.0.401, workload set 10.0.401.1, Apple packs
27.0.10722, MAUI 10.0.110+ and Xcode 27.0. SDK 10.0.401 was installed only in
session artifacts. Two isolated workload provisioning attempts failed because
the required Apple packages were unavailable through the permitted feeds.
Public NuGet metadata exists, but repository feed policy was not changed.
Therefore **supported public Xcode 27.0 compilation is blocked**, not passed.

Supplemental full builds used an existing custom SDK 10.0.402/Apple 27.1
toolchain and per-process `DEVELOPER_DIR`. Explicit MSBuild selection left
fixture `global.json` unchanged, so this does not validate ordinary SDK
resolution. Only the preview warning was suppressed, never Xcode version
checks. Build reports assert project sources remained unchanged.

Mac Catalyst compilation/runtime, custom callback runtime delivery,
authentication return, handled/unhandled quick actions, and comprehensive
multiwindow behavior were not executed.
Structural assertions and compilation do not prove those behaviors.

## Publication contract

Skill: `plugins/dotnet-maui/skills/maui-xcode27-migration`, distributed in
`dotnet-maui` plugin version 0.7.0.

```sh
maui ai list skill --env CopilotCli --json
maui ai add skill maui-xcode27-migration --env CopilotCli --yes
maui ai status skill --env CopilotCli --json
```

Use `--env Claude` or `--env VsCode` as appropriate. The CLI installs
instructions; ask the agent naturally to prepare the app for Xcode 27.
Marketplace alternative: `/plugin marketplace add dotnet/maui-labs`, then
`/plugin install dotnet-maui@dotnet-maui-labs`.

**No minimum published CLI version was established.** The latest inspected
public `Microsoft.Maui.Cli` package, `0.1.0-preview.12.26421.1`, does not expose
`ai`. This skill has not been published in the default catalog. The integration smoke exercises
the real parser/installer with checkout bytes through the existing HTTP test
seam; it does not prove the public download works. Both publication conditions
must be satisfied before presenting these as currently available commands.
