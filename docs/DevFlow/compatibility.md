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

## Package validation

The package validation lane packs the package, inspects the `.nupkg`, verifies its README,
repository metadata, `net9.0` asset, Driver dependency, and absence of CLI/broker/provider
payloads, then restores it from an artifact-local feed. The consumer matrix is compile coverage
only; device or simulator runtime qualification is tracked separately. See
[human-authored testing](testing.md) for platform gates and
[the package consumer project](../../tests/DevFlow/PackageConsumer/README.md) for the local-feed
command.
