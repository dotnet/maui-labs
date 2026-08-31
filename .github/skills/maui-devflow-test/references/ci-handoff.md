# CI Evidence Handoff

CI reports, flow-run exports, and `.mauitrace` bundles help diagnose a problem,
but they do not become execution authority. Preserve the distinction between
artifact integrity, provenance attestation, fresh local reproduction, and
human approval.

Give the bounded CI interpretation first. Do not ask for a project, device,
target, or agent just to summarize CI evidence; request the exact target only
if the user elects to continue to local reproduction or an executable draft.

## Handoff Contents

Provide a redacted summary with:

- committed flow and plan digest/revision;
- target platform/profile and declared qualification boundary;
- failure code, step, route/checkpoint facts, and oracle status;
- artifact trust state: untrusted, attested, or locally-reproduced;
- whether the evidence is diagnostic-only and what fresh local work remains;
- a link or reference for human review without copying secrets, raw
  UI text, screenshots, request bodies, or local identifiers.

## Rules

- CI success does not certify an unsupported platform, physical device, reset,
  business oracle, or qualification gate.
- Attested provenance is still diagnostic-only. Embedded IDs, hashes, and
  metadata cannot upgrade trust on their own.
- Do not use CI evidence to run a flow, choose a target, create a repair
  proposal, apply a selector, or claim an independently verified pass.
- If a repair is requested, require a fresh local reproduction against current
  flow/app/target/checkpoint facts before the human repair ceremony.

End with the next bounded owner: developer for a testability change, human
human reviewer for authorization, or local operator for reproduction.
