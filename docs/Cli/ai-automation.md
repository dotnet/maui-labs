# Automating `maui ai`

The [version 1 JSON Schema](ai-result.schema.json) describes handled AI command
results. Use `--json --ci --env <client>` for unattended commands. `--ci` and
`--json` suppress interaction; neither permits replacement of customized content.
Use `--force` only when that replacement or adoption is intended.

## Target selection

There is no default Copilot target. With no `--env`, commands use detected
environments, including user-level Copilot CLI configuration and applicable
recorded installations. Detection describes configuration markers, not whether a
client executable is installed, running, trusted, or connected.

Interactive `init` can offer a client selector when no effective environment is
found. Automation must always specify the intended targets: `--ci`, `--json`,
`--dry-run`, `--yes`, and redirected input do not trigger that first-run selector.
An actionable failure lists valid names instead of guessing a target.

```sh
maui ai init --env Claude --dry-run --json --ci
maui ai add skill maui-devflow-debug --env Claude --yes --json --ci
maui ai status --env Claude --json --ci
```

`--env` may be repeated or take several values. All explicit asset/environment
pairings must be compatible. In particular, project agent definitions support
`VsCode` and `CopilotCli`, not `Claude` or `OpenCode`. Skills and known MCP
registrations support all four.

Skills and agents are project-scoped. Copilot CLI MCP is **user-wide**, at
`~/.copilot/mcp-config.json`; the other clients' MCP registrations are
project-scoped. Scope is an output field, not implied by `--json`, `--ci`, or the
phrase "global option." Always inspect the plan's destinations.

## Result and exit contract

A handled command invocation with `--json` writes one JSON object to stdout:

| Field | Meaning |
|---|---|
| `schemaVersion` | Envelope compatibility version; currently `1` |
| `command` | `init`, `list`, `status`, `update`, or typed `add skill/agent/mcp` |
| `dryRun` | Whether mutations were disabled |
| `status` | `failed` if any result is blocked or failed; otherwise `success` |
| `results` | Typed asset rows, including per-row state, intended action, and outcome |
| `messages` | Human explanations; not a stable machine-parsing interface |
| `environments` | Selected clients, selection reasons/markers, and skill/MCP destinations and scope |

Each environment includes `name`, `reasonCode`, nullable `markerPath`, `scope`,
`skillsPath`, `mcpPath`, and `mcpScope`. Selection reasons are `detected-marker`,
`explicit-selection`, `guided-selection`, or `managed-registry`. `scope` is the
selection evidence's scope, defaulting to project when there is no existing
marker or registry evidence; use the asset row's scope for the actual mutation and
`mcpScope` for the MCP configuration. In particular, user-level Copilot detection
does not make its project skills user-scoped.

| Exit | Meaning |
|---|---|
| `0` | The handled command completed without blocked/failed rows. This includes successful no-ops and declined application. |
| `1` | A handled planning/application error or required blocked action. |

**Parser errors, help, and cancellation are outside this envelope.** Do not assume
every nonzero process exit produces JSON, or interpret every nonzero exit as an
asset conflict. Capture stderr and process exit status as well as stdout.
Cancellation is not success; it may interrupt an operation without a final result.

`status: success` is not a claim that every asset is installed, current, or
managed. Inventory can successfully report missing or unmanaged rows.
`status` does not contact remote sources or test MCP connectivity.

### Actions, outcomes, and reasons

`action` is one of `create`, `replace`, `adopt`, or `skip`. Adoption establishes
ownership; an exact unmanaged MCP registration can be adopted without rewriting
its configuration. `update` never adopts unmanaged assets or restores missing
ones, even with force.

| Outcome | Meaning |
|---|---|
| `planned` | Read-only catalog/preview row, or a planned mutation not yet applied |
| `succeeded` | The selected mutation and its tracking operation completed |
| `skipped` | No mutation, including current, unmanaged, missing, or declined cases |
| `blocked` | A required action is not authorized or cannot safely proceed |
| `failed` | Planning or application failed |
| `not-executed` | A planned mutation was withheld because another action blocked/failed |

For automation, use `outcome` and `reasonCode`, not string matching on `message`
or `error`. Known reasons include:

| Reason | Meaning |
|---|---|
| `available` | Catalog asset |
| `create`, `replace`, `adopt` | Planned/applied mutation |
| `already-current` | Desired owned content already present |
| `already-current-unmanaged`, `already-configured-unmanaged` | Matching unmanaged content; no implicit ownership |
| `unmanaged` | Unmanaged inventory or excluded from update |
| `skipped-missing` | Update deliberately does not restore a missing installation |
| `content-conflict` | Explicit force required for a conflicting replacement |
| `uncheckable` | Cannot establish that replacement is safe from existing metadata |
| `unknown-origin` | Required ownership/source information is unavailable |
| `invalid-destination` | Incompatible filesystem entry occupies the destination |
| `declined` | User declined application |
| `previous-action-failed` | Later action withheld after failure |
| `preflight-failed` | Command-level preflight error; name/path may be empty |
| `environment-selection-required` | No effective client; specify `--env` in unattended invocations |
| `environment-selection-cancelled` | Interactive first-run selection was cancelled or empty |
| `apply-failed` | Mutation or tracking operation failed |

Inventory may use an observed `state` as its reason. State/reason vocabularies are
extensible and owner-specific, including bundled DevFlow states. Preserve and
display unfamiliar reasons instead of treating them as success or crashing.
`requiresForce` indicates a replaceable conflict; `--force` does not bypass invalid
destinations, corrupt configuration, or missing provenance.

## Deterministic remote sources

Within one plan, each remote repository/ref is resolved to an immutable
**repository commit**. Marketplace and plugin manifests, trees, frontmatter,
and content are read from that snapshot. A moving branch cannot mix revisions
within a plan. Failure to resolve a snapshot is an error, not permission to fall
back to mutable content.

`origin.branch` retains the selected source ref for future updates.
`origin.resolvedCommit`, when available, identifies the snapshot used for the
current source read, or the recorded snapshot for offline inventory. It is null
for legacy provenance without a recorded snapshot. A tree SHA or last commit touching an individual file is
not this repository snapshot.

Separate invocations following `main` may resolve different commits. To make a
preview and subsequent apply use the same remote source, pass the recorded full
40-character repository commit as `--branch` to both:

```sh
# SOURCE_COMMIT is the actual full repository commit selected by your pipeline.
maui ai init --env VsCode --repo dotnet/maui-labs --branch "$SOURCE_COMMIT" --dry-run --json --ci
maui ai init --env VsCode --repo dotnet/maui-labs --branch "$SOURCE_COMMIT" --yes --json --ci
```

An installation recorded with a branch follows that branch on update; an
installation recorded with an explicit commit stays pinned unless overridden.
Use the same CLI version too: bundled skills and the curated MCP definition come
from the running CLI, not the remote repository. Deterministic source bytes do
not make the local filesystem immutable between preview and apply.

## Compatibility and recovery

Version 1 preserves existing required fields, their types, and action/outcome
semantics. Additive fields and new state/reason values may appear without a
version increase. Consumers must ignore unknown properties and handle unknown
state/reason values conservatively. Incompatible changes to required fields,
types, or existing meanings require a schema version change. The schema allows
additional properties intentionally.

Null `origin`, `entryKey`, or `error` is valid. Catalog and command-level failure
rows may lack an installation destination. Output never includes raw MCP
configuration, environment values, or credentials as ownership data.

Commands are not a cross-asset or content-plus-registry transaction. A failed
application may have applied earlier rows; inspect results and then run status
before retrying. Directory replacement is not crash-atomic. JSONC rewrite backups
are latest-only `<config>.bak`, not history. Concurrency and crash-recovery limits
are described in the [CLI guide](../../src/Cli/README.md).
