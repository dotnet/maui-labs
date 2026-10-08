# Squad Project Policy Draft

This documentation-only capture preserves the project-specific policy edits currently present in:

- `src/Comet/.squad/config.json`
- `src/Comet/.squad/decisions.md`
- `src/Comet/.squad/identity/wisdom.md`
- `src/Comet/.squad/routing.md`
- `src/Comet/.squad/skills/verification-protocol/SKILL.md`
- `src/Comet/.squad/agents/amos/charter.md`
- `src/Comet/.squad/agents/bobbie/charter.md`
- `src/Comet/.squad/agents/holden/charter.md`

The edits were captured from a checkout that also contained an incomplete generated Squad 0.13 upgrade. Generated upgrade files, histories, memory, casting state, backups, workflow and template upgrades, coordinator changes, and `.claude` settings are intentionally excluded from `src/Comet/docs/research/squad-project-policy-draft.patch`.

These policy edits are not active in this branch. Before applying them, reconcile the draft against one exact Squad version and one explicitly selected state backend.

## High-confidence blockers

- **`TEAM_ROOT` semantics:** The upgrade does not establish one consistent meaning for `TEAM_ROOT` across project files, generated scripts, and automation. Reconciliation must define whether it identifies the repository checkout, `src/Comet`, or `src/Comet/.squad`, then update every consumer consistently.
- **Missing explicit backend contract:** The project does not declare one authoritative state backend or its ownership, migration, concurrency, and recovery behavior.
- **Version skew:** Project policy edits and generated Squad 0.13 material were produced against different assumptions, so they cannot be safely combined without pinning and validating one exact Squad version.
- **Unsynchronized workflows:** Coordinator, workflow, and template surfaces were not upgraded as one coherent versioned set.
- **Partial built-in installation:** Built-in Squad capabilities are only partly installed or referenced, leaving policy and generated runtime behavior out of alignment.

The patch is a review artifact, not an installation or migration script.
