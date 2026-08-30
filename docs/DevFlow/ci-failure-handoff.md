# DevFlow CI failure handoff

The DevFlow failure publisher is a deterministic `workflow_run` consumer. It is
separate from the DevFlow Integration Tests workflow so an untrusted pull request
cannot change the publisher, its verifier, or its issue template.

## Trust boundary

`.github/workflows/devflow-failure-publisher.yml`:

- listens only for completed runs named `DevFlow Integration Tests`;
- additionally requires the trusted workflow path
  `.github/workflows/devflow-integration.yml`;
- accepts issue publication only for `schedule` or `workflow_dispatch` runs whose
  head repository is the current repository, whose head ref is the repository
  default branch, and which have no associated pull request;
- rejects every `pull_request` run, including same-repository pull requests, and
  every fork head repository before making a GitHub API call;
- checks out the repository default branch, never the pull-request head;
- has only `actions: read`, `contents: read`, and `issues: write`;
- serializes all publisher runs for the repository; and
- passes repository, workflow, source-event, head-repository/ref, default-branch,
  run, commit, and PR provenance from the `workflow_run` event to
  `eng/devflow/Publish-DevFlowFailureIssue.ps1`.

In publish mode the script re-reads the workflow run and artifact metadata from
the GitHub API and compares repository, workflow, source event, head repository,
head ref, default branch, run, attempt, commit, conclusion, and PR association
with the event values before any issue write. It first reads repository metadata;
when Issues are disabled it returns the successful
`ignored-issues-disabled` no-op without looking up or downloading artifacts.

## Producer contract

The integration workflow uploads one artifact named:

```text
devflow-failure-handoff-<run-id>-<run-attempt>
```

The uploaded artifact contains exactly two root entries. GitHub constructs the
download ZIP around those entries; the publisher reads that wrapper directly
with `ZipArchive` and never generally extracts it.

### `manifest.json`

```json
{
  "schema": "devflow-ci-failure-manifest",
  "version": 1,
  "entries": [
    {
      "name": "handoff.json",
      "sha256": "sha256:<64 lowercase hexadecimal characters>",
      "sizeBytes": 1234
    }
  ]
}
```

### `handoff.json`

```json
{
  "schema": "devflow-ci-failure-handoff",
  "version": 1,
  "provenance": {
    "repository": "owner/repository",
    "workflowName": "DevFlow Integration Tests",
    "workflowPath": ".github/workflows/devflow-integration.yml",
    "sourceEvent": "schedule",
    "headRepository": "owner/repository",
    "headRefSha256": "sha256:<64 lowercase hexadecimal characters>",
    "runId": 123456789,
    "runAttempt": 1,
    "commitSha": "<40 lowercase hexadecimal characters>",
    "pullRequestNumber": 0
  },
  "outcome": "failure",
  "qualification": "qualified",
  "category": "test-failure",
  "platform": "android",
  "testIdentitySha256": "sha256:<64 lowercase hexadecimal characters>",
  "evidenceSufficiency": "sufficient"
}
```

The handoff deliberately does not depend on CLI execution or triage types. It is
a small security envelope around safe enums, digests, and trusted provenance.
The concurrently developed execution/triage schema can later produce this
summary without becoming part of the publisher's trust boundary.

Allowed values:

- `outcome`: `failure`, `pass`, `pending`
- `qualification`: `qualified`, `not-qualified`, `pending`
- `category`: `test-failure`, `app-crash`, `timeout`, `device-failure`,
  `harness-failure`, `infrastructure`, `unknown`. `app-crash` is reached only when the run report's
  failure class is `app-crash`, which the classifier emits only from proven abnormal-exit evidence;
  `device-failure` is reserved and no current classifier produces it.
- `platform`: `android`, `ios`, `maccatalyst`, `macos`, `windows`,
  `cross-platform`, `unknown`
- `evidenceSufficiency`: `sufficient`, `partial`, `insufficient`

`eng/devflow/New-DevFlowFailureHandoff.ps1` is the producer. It reads only the
bounded flow-pilot `manifest.json` and `qualification.json`; it does not extract,
copy, or replay evidence. It emits an archive only after all of these conditions:

- manifest `schema: 1`, `kind: devflow-flow-pilot`, trusted commit/run/attempt
  agreement, empty exact-array `validationErrors`, bounded production identity
  facts, explicit `experimental: false` and `officialCoverage: true`, valid
  artifact declarations, and exact array/object types;
- every `firstAttempt` is canonically hashed over its complete JSON value and
  must equal `cleanAttempts[0]`; changing or adding any field is rejected;
- exactly one Tier-1 first attempt is a non-infrastructure failure with safe
  failure class/code, a SHA-256 flow digest, and no mixed cancelled,
  infrastructure, orphaned, or unknown-completion outcome;
- qualification is a complete versioned `maui-preview-qualification` report for
  the same platform. The producer verifies all required gates, review records,
  feature flags, corpus facts, bounded metrics and thresholds, fingerprints,
  profiles, reasons, exclusions, and artifact references. A `pass` report must
  have every required gate at `pass`, metrics that independently satisfy the
  declared thresholds, and fingerprints that match the source manifest;
- qualification contains exactly one `flow-pilot-manifest` reference whose
  digest is the SHA-256 of the exact source manifest bytes;
- the selected attempt has a bounded `flow-run-report` and `.mauitrace` from the
  same run. Kind, path, media type, run ID, report identity digest, entry-byte
  digest, and qualification references must agree. A caller-controlled
  `redacted: true` flag alone is never accepted as proof; and
- the workflow-declared lane is a non-experimental, official physical-device
  flow-QA lane whose source device evidence also says real physical device.

The canonical test identity is UTF-8 SHA-256 of:

```text
devflow-ci-test-identity-v1\n<platform>\n<tier>\n<flow-digest>
```

Each `\n` is a single LF (U+000A), never CRLF. `<flow-digest>` is the flow digest
in exactly the form the source flow-pilot manifest carries it, which the
producer's `Test-Sha256` guard pins to `sha256:<64 lowercase hexadecimal
characters>`; the manifest value is itself the `sha256:`-prefixed
`MauiFlowRunReportSerializer.ComputeFlowDigest` of the committed flow. `<tier>`
is `tier-1`, the only tier a qualifying candidate can declare. The result is
`sha256:<64 lowercase hexadecimal characters>`.

Raw test names, paths, logs, stack traces, branch names, messages, source, and
artifact-provided Markdown are never copied to the handoff. The handoff
provenance is constructed exclusively from workflow inputs (repository,
workflow name/path, source event, head repository, SHA-256 of the head ref, run
ID/attempt, head SHA, and PR number), after source commit/run fields are compared
with those inputs. The raw branch/ref name is never copied into the handoff.

The producer serializes ordered, UTF-8-without-BOM JSON, declares the exact
`handoff.json` SHA-256 and byte length in `manifest.json`, and stages precisely
those canonical entry bytes for `upload-artifact`. It also writes a local
two-entry ZIP with fixed entry order and timestamps as a bounded creation
sentinel and local reproducibility check. GitHub does **not** upload that ZIP;
GitHub creates its own download wrapper, so byte determinism is claimed only for
the two JSON entries, not for the service-generated ZIP container. The producer
limits each source JSON and the local sentinel ZIP to 1 MiB, and each handoff
entry to 256 KiB. `-VerifyOnly` validates and derives the handoff without
writing it; PowerShell `-WhatIf` reports `would-create` without writing it.
Source accounting is bounded to 256 flows/artifacts, 1,000 attempts per flow,
4,096 qualification list items, and 1,000,000 metric observations. Run and
JSON identity integers must remain within the interoperable JSON safe-integer
range; narrower fields use narrower limits.

## Verification limits

The PowerShell verifier rejects the artifact before issue lookup or mutation
when any check fails:

| Limit | Value |
|---|---:|
| Downloaded ZIP | 1 MiB |
| Entry count | exactly 2 |
| Individual uncompressed entry | 256 KiB |
| Total uncompressed data | 512 KiB |
| Per-entry compression ratio | 100:1 |

It also rejects:

- expired, missing, duplicate, or ambiguously named artifacts;
- rooted, traversing, duplicate, directory, or non-allow-listed ZIP entries;
- invalid UTF-8/JSON, schema, version, enum, digest, or integer fields;
- a declared `handoff.json` size or SHA-256 mismatch; and
- repository, workflow, source-event, head-repository, head-ref digest, run,
  attempt, commit, or PR provenance mismatches.

Only `failure` + `qualified` with `sufficient` or `partial` evidence and a trusted
workflow conclusion of `failure` or `timed_out` can write an issue. Malformed,
unverifiable, pass, pending, not-qualified, and insufficient-evidence artifacts
are read-only no-ops.

Publication adds a second trust gate after envelope verification. Only
same-repository, default-branch `schedule` and `workflow_dispatch` runs are
publishable. A valid PR-produced envelope can be verified locally as
`verified-diagnostic-only`, but it is never authenticated for issue publication.
PR incidents therefore remain CI diagnostics or locally verified handoffs until
default-branch-owned raw classification is implemented.

## Issue lifecycle

The fingerprint is SHA-256 over the trusted repository/workflow path and the safe
category, platform, and test-identity digest. The publisher creates or verifies the dedicated `devflow-ci-failure` label and
requires the issue author to be `github-actions[bot]` with GitHub type `Bot`.
Every issue contains a stable hidden marker with a digest of the remainder of
the exact publisher template:

```text
<!-- devflow-ci-failure:v1 fingerprint=sha256:... body=sha256:... -->
<!-- devflow-ci-failure-occurrence:v1 run=... attempt=... -->
<!-- devflow-ci-failure-data:v1 category=... platform=... testIdentity=sha256:... evidence=... -->
```

The serialized publisher creates the first issue, reopens a closed matching issue
on recurrence, and adds a fixed recurrence comment. Lookup is label-scoped and
accepts exactly one matching bot-authored issue whose title, marker structure,
fixed sections, and body digest validate. An untrusted matching marker or
multiple matching issues is a fail-closed no-op. Recurrence comments use their
own body digest and are trusted for idempotency only when bot-authored. Commands
and arbitrary body/comment text are never authority.

Each issue includes the run link, explicit absence of PR publication,
category/platform/test digest, evidence sufficiency, artifact download and
retention guidance, and a local verification command. The producer is expected
to retain this small artifact for 30 days.

## Human investigation

`.github/workflows/devflow-investigate.yml` is a manual
`workflow_dispatch` workflow for one selected issue number. It is ordinary,
deterministic GitHub Actions code: there is no agent engine, prompt, model,
GitHub MCP server, repository checkout, pull-request permission, issue creation,
or prompt/log artifact upload.

The single `github-script` step repeats the publisher's bot author, dedicated
label, exact title, marker, template, and body-digest checks. It then derives
only the allow-listed category, platform, test-identity digest, and evidence
sufficiency and posts fixed maintainer guidance to that same issue. The guidance
does not claim a root cause, parse raw evidence, request repair authority, or
copy issue prose. A digest-bound bot-authored guidance marker makes reruns
idempotent; an untrusted matching marker fails closed.

### Resolving the test identity to a committed flow

The issue names no test. The first step for a human or an agent holding one is
to map `testIdentitySha256` back to the flow that produced it, in a trusted
checkout:

```powershell
maui devflow flow identity `
  --resolve sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef `
  --platform android `
  --search maui-tests
```

This does not weaken the publisher's boundary. The digest is a lookup key, not a
secret: the property CI enforces is that no raw name, path, log, or branch is
ever emitted into a public issue, and that is a property of the *publisher*. The
resolver runs locally over flow files the operator already has on disk, computes
the same construction the producer computes, and discloses nothing that was not
already in the checkout. It performs no network access, reads no handoff
archive, and never writes.

Resolution is exact, and its bounds are stated rather than implied. Every `.md`
file under the search root is parsed, its flow digest computed, and its identity
compared byte-for-byte — excluding `.git`, `bin`, `obj`, `artifacts`, and
`node_modules`, symlinked directories, and files above 1 MiB (the same per-file
limit `flow run` enforces, so no runnable flow is excluded by it). There is no
fuzzy or closest match. Three outcomes are reported, and only the first exits
zero:

- `matched` — the identity reproduces exactly from the flow bytes in this
  checkout. The reported flow is the failing test.
- `matched-superseded` — no current flow reproduces the identity, but a flow's
  plan sidecar still records the digest that does. Because `flow run` refuses a
  bundle whose sidecar and flow disagree on *both* `flow.path` and `flow.digest`,
  and this command re-checks both bindings before honouring a sidecar, the two
  agreed at run time — so this names the test and states plainly that the flow
  has been edited since. Check out the commit named in the issue before
  reproducing.
- `no-match` — nothing under the search root produces the identity. The command
  reports the likely causes: the flow was edited after the run (the digest
  covers flow content, so any edit inside the fenced `json maui-test` block
  changes it), the platform or tier is wrong, the flow lives outside the search
  root, or it lies under an excluded directory. The scan refuses rather than
  truncates when a search root is too large, and every file it could not read is
  counted and named in `scan.skipped`, so `no-match` never silently means
  "stopped looking".

Omitting `--platform` computes one identity per platform CI can publish
(`android`, `ios`, `maccatalyst`, `windows`); each comparison remains exact.
Those four are the only values accepted — the handoff envelope allows a reader
to *see* a wider set, but the producer refuses anything else before an identity
is computed, so any other value would yield a digest that can never appear in an
issue. Passing a flow or directory without `--resolve` prints the identities
that flow would produce, which is how a new flow's expected identity is
obtained. `--json` output is stable and carries the construction it used, for
agent consumption.

## Local verification

After downloading the ZIP into a trusted checkout, use the exact provenance from
the issue:

```powershell
pwsh ./eng/devflow/Publish-DevFlowFailureIssue.ps1 `
  -VerifyOnly `
  -ArchivePath ./devflow-failure-handoff.zip `
  -Repository owner/repository `
  -WorkflowName 'DevFlow Integration Tests' `
  -WorkflowPath '.github/workflows/devflow-integration.yml' `
  -SourceEvent schedule `
  -HeadRepository owner/repository `
  -HeadRef main `
  -DefaultBranch main `
  -WorkflowConclusion failure `
  -RunId 123456789 `
  -RunAttempt 1 `
  -CommitSha 0123456789abcdef0123456789abcdef01234567 `
  -PullRequestNumber 0
```

`verified` means the envelope is valid and qualified. Verification mode performs
no GitHub API calls or writes.

The publisher requires PowerShell 7.3 or later and is always invoked as
`pwsh ./eng/devflow/Publish-DevFlowFailureIssue.ps1`. Only `https://api.github.com` is trusted as
an API base URL. Against that API the publisher sends the token it was actually handed as
`Authorization: Bearer <token>`; the header is built in one place, is never written to a result,
an artifact, or a diagnostic, and is omitted entirely when no token is held, so a missing
credential is reported as `github-token-missing` rather than as an API rejection.

A loopback base URL is accepted for tests only when
`DEVFLOW_PUBLISHER_ALLOW_LOOPBACK_TEST_API=1` is set, and then only together with `-VerifyOnly`:
publishing against a loopback test double is refused as
`loopback-test-api-requires-verify-only`, and the held GitHub token is dropped before any request
can be built, so a repository token is never forwarded to a local listener. There is no opt-in
that relaxes either rule. A test that has to observe the header the publisher really builds drives
the shipped request helper directly against a loopback listener instead.

For a PR artifact, pass `-SourceEvent pull_request`, the workflow-run head
repository/ref, and its positive PR number. A valid envelope returns
`verified-diagnostic-only`; this does not authenticate the PR workflow or permit
issue publication.

## Local reproduction and repair boundary

The issue, handoff ZIP, CI artifact names, and downloaded CI diagnostics remain diagnostic-only.
Do not pass `devflow-failure-handoff.zip` to the flow runner and do not extract arbitrary archive
content. After verifying the trusted handoff envelope above, resolve the issue's test identity to a
committed flow with `maui devflow flow identity --resolve` (see
[Resolving the test identity to a committed flow](#resolving-the-test-identity-to-a-committed-flow));
every step below needs the flow that resolution names. Then obtain an allow-listed per-flow
`flow-run.json` or `.mauitrace` diagnostic from the retained run artifacts and reproduce it against
the committed flow, matching plan, and exact local target:

```powershell
maui devflow flow reproduce maui-tests\checkout.md `
  --plan maui-tests\checkout.maui-plan.json `
  --project src\MyApp\MyApp.csproj `
  --platform android `
  --device emulator-5554 `
  --import downloaded\flow-run.json `
  --output artifacts\devflow\checkout-reproduction
```

The import uses the bounded artifact-trust reader; it never generally extracts, trusts embedded
IDs/provenance, or executes imported content. A new local run is performed by the production
`FlowExecutionCoordinator`. Exact matching fails closed for stale plans, missing facts, platform/
build/source/package/failure/checkpoint drift, infrastructure or unknown completion, unsupported
artifacts/targets, and missing required independent-oracle declarations.

`local-reproduction.json` grants no repair authority. Even an exact CLI match is diagnostic-only:
the CLI-to-broker binding and capability are memory-only, so it cannot unlock repair. Continue
with the human-gated sequence:

1. Open the Inspector with the committed test and original diagnostic:

```powershell
maui devflow inspect --test maui-tests\checkout.md --trace downloaded\flow-run.json
```

2. Import the original diagnostic through the Inspector Workbench **Trace** path.
3. Choose **Reproduce locally** to prepare the broker-owned reproduction. Native approval can be
   completed only in a trusted VS Code Inspector or GitHub Copilot Canvas host when the broker
   reports approval available and the host advertises `nativeApproval`; standalone browser and chat
   remain non-authoritative.
4. Verify the matching broker-owned reproduction against the current flow, app, target, failure,
   and checkpoint facts.
5. Only then open **Repair** review.

Never infer broker authority from the CLI report.

## Current activation state

`devflow-integration.yml` invokes the producer with `if: always()` immediately
after Android flow-pilot manifest and qualification generation. The producer is
given `android-emulator-pilot`, which is deliberately not a qualifying lane.
The Android emulator's `not-qualified` report (or any infrastructure-only,
missing, malformed, pass, or ambiguous source) produces no ZIP, so no
publisher issue can result from that advisory pilot.

The upload step is conditional on the exact ZIP existing and uses the required
`devflow-failure-handoff-<run-id>-<run-attempt>` name. A future genuine
physical-device flow-QA lane becomes active by supplying
`physical-device-flow-qa`; it still must provide a complete, manifest-bound
qualification report with explicit `status: pass` and satisfy all deterministic
producer checks. A PR run
may retain such a diagnostic artifact, but only a same-repository default-branch
schedule or workflow dispatch can reach issue publication.

## Repository enablement and residual platform limits

GitHub Issues must be enabled in repository settings before publication can
create the dedicated label or issues. The current repository has Issues
disabled, so qualified publisher runs intentionally complete with
`ignored-issues-disabled`; this is an expected no-op, not validation failure.

The former gh-aw investigator source and generated lock are intentionally
removed. The generated workflow required checkout, GitHub MCP/repository and PR
surfaces, broader write permissions, and prompt/log artifacts that contradicted
the claimed isolation boundary. Do not restore an agent workflow until its
compiler can verifiably omit every one of those surfaces.
