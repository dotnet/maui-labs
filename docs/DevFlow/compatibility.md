# DevFlow Testing preview compatibility policy

`Microsoft.Maui.DevFlow.Testing` is an **experimental preview** package. It targets `net9.0` so
the same assembly can be consumed by the .NET 9 and .NET 10 CLI and by compatible .NET 10 MAUI
hosts. Preview status permits change, but it does not permit an undocumented compatibility break.

## Public API

The committed `PublicApiBaseline.txt` is checked in the required DevFlow test build. During a
preview train, public APIs are expected to remain both binary- and source-compatible unless the
change is explicitly approved as breaking:

- adding a new type or optional member is normally compatible;
- removing, renaming, changing accessibility, changing a public/protected signature, changing a
  constructor default, or changing an enum/constant's established semantics is breaking;
- changing the behavior of a successful flow, selector, assertion, reset admission, report, or
  approval boundary is treated as a semantic breaking change even when the CLR signature remains
  the same;
- adding a virtual member to a public extensible base type requires the same review as a binary
  compatibility change.

When an intentional break is necessary, update the baseline in the same change, add the
`breaking-change` label to the pull request, explain binary/source and behavioral impact in the PR
description and release notes, and document the migration. Do not silently rewrite the baseline.

## JSON, Markdown, and HTTP contracts

Versioned schemas use stable `$id` values under
[`docs/DevFlow/spec/schemas`](spec/README.md#testing-contract-index). Within an existing schema
version, changes must be additive: readers preserve unknown extension fields and new fields remain
optional. A writer must not make a previously optional field required, reinterpret an existing
value, or use a new required semantic without explicit version negotiation.

New required execution semantics need a new schema/versioned contract or an explicit capability
and preflight rejection. Hosts must reject unsupported required semantics before any mutation.
Schema 1 and schema 2 flows remain readable as documented; a future schema is not implied by
adding diagnostic fields.

The local broker and restricted test-agent endpoints follow the same rule. Capability-token,
approval, repair, source-proposal, and trust boundaries are security semantics, not best-effort
fields: compatibility adapters may retain old response shapes, but they may not weaken a denial,
auto-apply a proposal, or silently target a different agent.

### Layout diagnostics schema 2.1 (breaking)

The layout diagnostics report moved from `2.0` to `2.1`. This is an intentional, documented break:

- **`textEvidence.text` and `textEvidence.textLength` were removed** from the wire contract and
  from `Microsoft.Maui.DevFlow.Driver.LayoutTextEvidence`. Neither was ever populated — this layer
  reads no element text or values — so retaining members that could hold them allowed a report to
  be misread as evidence that text had been captured. Removing them is a **source and binary
  breaking change** for Driver consumers that referenced either property; there is no replacement,
  because there is no such data. `coverage.neverCaptured` continues to be published on every
  report.
- **`privacy.text` accepts only `none`.** `length` and `full` are now rejected with an explicit
  reason rather than silently downgraded, because this layer cannot honour them.
- **`suppressionKey` changed meaning.** It is now derived only from restart-stable identity — rule,
  subtype, source path/line, AutomationId, and type — and never from runtime element ids. Existing
  fingerprints persisted in `.mauidevflow` under `layoutDiagnostics.suppressions` no longer match
  and must be re-created from a fresh scan; in exchange, a new fingerprint keeps matching after a
  page rebuild or an app restart, which is what a durable suppression is for. This is restart
  stability, not portability: **every input to the key can change without the finding changing.**
  The source path in the key is the one the app reported, so a fingerprint stops matching after a
  file move or rename, a declaration-line move, or a build from a different checkout, clone path,
  or machine. It equally stops matching when the element's `AutomationId` is added, removed, or
  renamed, when its type changes (a `Label` refactored to a `Border`, or a control replaced by a
  custom subclass), or when the rule subtype or the related-element identity a rule reports
  changes — including a rename or removal of the related element a finding is reported against. A
  committed `.mauidevflow` is not guaranteed to match on another contributor's machine or in CI,
  and a suppression that stops matching after an ordinary refactor is expected rather than a bug:
  re-create it from a fresh scan. Making these keys checkout-portable would need a
  repository-relative source path, which this layer does not have; making them survive an identity
  change would mean matching on something other than identity, which would suppress findings the
  reviewer never saw.

### Layout diagnostics request versions

**Clients send `2.0`, not `2.1`.** The agent ships inside the app under inspection, while the
Driver, CLI, MCP server, and Inspector ship with the tooling and are updated independently, so a
current client is routinely pointed at an app built against the previous package. An agent
validates the declared request `schemaVersion` and rejects an unknown one with HTTP 400, and `2.1`
changed only the response — removed fields and a redefined `suppressionKey` — so declaring it in a
request buys nothing and breaks every scan against a `2.0` agent. `2.0` is the newest version both
`2.0` and `2.1` agents accept, so it is the default in
`Microsoft.Maui.DevFlow.Driver.LayoutInspectionRequest` and the version the Inspector and the VS
Code host send explicitly.

Responses are read at whatever version the agent declares: `1.0`, `2.0`, and `2.1` are all
understood. A `2.0` response may still carry the text members `2.1` removed; no client surfaces
them, so an older response never becomes a way to obtain text or a text length.

Agents still accept `1.0` and `2.0` **requests**, so an older Driver keeps working; only the
response version and the removed response fields moved.

## Package validation

The package validation lane packs the package, inspects the `.nupkg`, verifies its README,
repository metadata, `net9.0` asset, Driver dependency, and absence of CLI/broker/provider
payloads, then restores it from an artifact-local feed. The consumer matrix is compile coverage
only; device or simulator runtime qualification is tracked separately. See
[human-authored testing](testing.md) for platform gates and
[the package consumer project](../../tests/DevFlow/PackageConsumer/README.md) for the local-feed
command.
