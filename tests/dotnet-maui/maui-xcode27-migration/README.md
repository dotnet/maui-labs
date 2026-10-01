# Xcode 27 migration evaluations

See [RESULTS.md](RESULTS.md) for the executed local checks, iteration findings
and explicit toolchain/publication limits.

These tests follow Anthropic's
[skill-creator workflow](https://github.com/anthropics/skills/tree/main/skills/skill-creator):
draft, run paired executions, grade actual artifacts, inspect traces, revise,
then run held-out requests. The execution adapter uses the installed Copilot CLI
and its default model; it does not require or install Claude Code. The official
Anthropic description-optimization loop is Claude-specific, so this suite uses
successful Copilot `skill` tool events for description evaluation instead.

`eval.yaml` participates in the repository skill-validator evaluations.
`evals.json` defines executable project scenarios. `triggers.json` is the
description-development set; `triggers-held-out.json` records the initial
confirmation set. `triggers-held-out-final.json` is a fresh set authored after
the initial confirmation exposed a false positive. Do not tune the description
on a held-out set and continue calling it held-out.

## Run locally

Requirements: Python 3.10+, authenticated Copilot CLI with JSONL output,
`--available-tools` and `COPILOT_HOME` support. The runner uses POSIX process
groups for timeout cleanup (macOS/Linux). Use disposable, trusted session
artifacts, not a real customer project. The agents can execute tools during
migration; their path permissions and explicit prompt scope are not an OS
sandbox. Network research is available to both paired configurations.

```sh
python3 -m unittest discover -s tests/dotnet-maui/maui-xcode27-migration -p 'test_*.py' -v

# Replace /absolute/session-artifacts with an existing disposable artifact root.
python3 tests/dotnet-maui/maui-xcode27-migration/run_evals.py migration \
  --output /absolute/session-artifacts/migration/iteration-1 \
  --cases simple central custom --workers 2

python3 tests/dotnet-maui/maui-xcode27-migration/run_evals.py migration \
  --output /absolute/session-artifacts/safety/iteration-1 \
  --cases older-os net9 net11-rc1 no-apple version-mismatch unrelated-ui audit-held-out \
  --configuration with_skill

python3 tests/dotnet-maui/maui-xcode27-migration/run_evals.py triggers \
  --output /absolute/session-artifacts/triggers/iteration-1
python3 tests/dotnet-maui/maui-xcode27-migration/run_evals.py triggers \
  --trigger-file tests/dotnet-maui/maui-xcode27-migration/triggers-held-out-final.json \
  --output /absolute/session-artifacts/triggers/held-out

# A second real agent pass over the FIRST run's migrated files, not a helper rerun.
python3 tests/dotnet-maui/maui-xcode27-migration/run_evals.py idempotency \
  --output /absolute/session-artifacts/migration/iteration-1/simple/with_skill
```

New evaluations refuse to overwrite existing evidence. Each run has the exact
command, prompt metadata, JSONL tool trace, response, elapsed time, output files,
before hashes, and `grading.json`. Trigger tests offer all app-plugin skills
and read-only inspection tools, and check a successful `skill` execution;
assistant claims or keyword matches alone do not count. Trigger-only tests do
not prove the agent can execute a migration. Failed assertions (and empty case
selections) exit nonzero; preserve the reports even when a run fails.

The structural oracle checks plist dictionaries (including duplicate root
keys), scene registration, preserved metadata, central version ownership,
inline minima and target frameworks. Its unit tests deliberately feed invalid
fixtures to ensure it rejects them. Those oracle unit tests are **not** actual
agent executions or compilation tests.

Audit-only checks compare all project-file hashes, not just OS minima. Review
`response.md` and actual C# separately for callback ownership, exactly-once
completion, base calls and deferred cold links. Regex/name presence cannot prove
that behavioral code works. Record those qualitative expectations with
`text`, `passed`, and concrete `evidence` in the run's `grading.json`.

## Official review viewer

Download the official `scripts/aggregate_benchmark.py`,
`eval-viewer/generate_review.py`, and its sibling `viewer.html` from
`anthropics/skills/skills/skill-creator` into one session-artifact directory;
record the upstream commit used. No global installation is needed.

```sh
python3 tests/dotnet-maui/maui-xcode27-migration/build_report.py \
  --input /absolute/session-artifacts/migration/iteration-1 \
  --output /absolute/session-artifacts/review/iteration-1 \
  --skill-creator /absolute/session-artifacts/skill-creator
```

This stages the official layout, runs Anthropic's aggregator, corrects its
default missing-token/three-run assumptions to actual evidence, and runs
`generate_review.py --static`. The resulting `review.html` contains responses,
project source bundles and assertion evidence, without starting a server.
Unreported token usage remains unknown. One run per case does not measure
run-to-run reliability. Compare which assertions also pass without the skill
before claiming an improvement.

## Compilation and distribution

Compilation is separate from structural grading. A supported Xcode 27.0 test
requires the released SDK 10.0.401/workload set 10.0.401.1 combination and MAUI
10.0.110+, plus a compatible macOS host. Obtain approval before provisioning
tools or dropping older OS support. Do not disable Xcode checks.

`build_fixture.py` can compile an agent's actual output using an already
installed SDK and an explicit per-process `DEVELOPER_DIR`. It invokes that
SDK's MSBuild directly so it does not rewrite the fixture's `global.json`;
the report explicitly records this SDK-selection override. It is **not**
validation of the app's normal `dotnet` SDK selection. Source hashes must
remain unchanged. For example:

```sh
python3 tests/dotnet-maui/maui-xcode27-migration/build_fixture.py \
  --project /absolute/session-artifacts/migration/iteration-1/simple/with_skill/outputs/MyApp.csproj \
  --dotnet-root /absolute/installed/dotnet --sdk-version 10.0.401 \
  --xcode /Applications/Xcode-27.0.app \
  --output /absolute/session-artifacts/builds/simple-ios
```

For a separately approved custom 27.1 toolchain, use
`--supplemental-preview`. It suppresses only `XCODE_27_1_PREVIEW`, never
Xcode version checks; report such results as supplemental, not a supported
27.0 pass. Build logs, exact commands/environment, exit status, source
hash comparison and timing are preserved. Simulator launch is a separate
explicitly coordinated manual step, not part of this script.

`XcodeMigrationDistributionTests` in the CLI test project reads this checkout's
real manifests and skill bytes through the existing HTTP test seam, then invokes
the real command parser/discovery/installer. It does not publish the skill or
add a production-only test-feed switch:

```sh
dotnet test src/Cli/Microsoft.Maui.Cli.UnitTests \
  --filter FullyQualifiedName~XcodeMigrationDistributionTests
```

That is an in-process command integration smoke, not proof that an unpublished
skill can be downloaded from the public marketplace. Verify CLI availability
and the remote catalog separately before promising an install command works
for users.
