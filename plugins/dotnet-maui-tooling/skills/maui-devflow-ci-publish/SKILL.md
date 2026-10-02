---
name: maui-devflow-ci-publish
description: >-
  Prepare or publish one human-requested draft PR for an already verified local
  DevFlow fix. USE FOR: an explicit request to review and publish the exact
  verified repair files to a named repository and base branch.
  DO NOT USE FOR: automatically publishing after a run, running UI tests,
  requesting broker approval, repairing selectors or source, inferring permission
  from a CI issue, merging, or closing an issue.
---

# Human-controlled DevFlow draft publication

This experimental, opt-in skill is a **separate** increment from testing,
CI triage and local reproduction. It is distributed only through the tooling
plugin; it is not registered with `maui devflow init` or the bundled CLI installer.
There is no workflow trigger, publisher service, MCP write tool or auto-apply.

It adapts the bounded publication checks developed in fork commit `4b8f833e`,
but intentionally does **not** inherit its automatic-publication default.
Loading this skill, receiving an issue, "fix it", "looks good", a passed run or
a plan digest does not authorize Git or GitHub writes.

## Inputs and initial state

Require an explicit human publication request naming the repository, base,
reviewed file allowlist and intended draft PR. If absent, prepare an inert
publication summary and stop. In a noninteractive session, missing authority
is a stop; do not invent approval.

An ordinary source-control publication request is not a broker run/repair
grant. This skill never obtains either, never reads an owner approval token,
and never runs a test to manufacture missing publication prerequisites.

## Eligibility checks

Read [the publication checklist](references/publication.md). Require all of:

- The initial failure and separate post-change local execution records refer
  to the exact reviewed app/flow/plan revisions and selected target.
- The post-change primary result is terminal, passed and independently verified,
  cleanup is complete, and no secondary failure or unknown completion exists.
- An independent business oracle actually succeeded; UI assertions, CI green
  checks and screenshots cannot substitute for it.
- The current worktree diff is exactly the reviewed verified file set. No
  unrelated staged or dirty paths, generated output or credentials are included.
- Assertions, expected values, action order, reset/oracle requirements and
  selector specificity were not weakened to obtain a pass.

Missing facts stop at an inert summary. Imported evidence remains diagnostic-only.
Do not upgrade indeterminate CI correspondence or erase earlier failed attempts.
Do not run or retry a destructive/non-replayable flow.

## Publication after the exact human request

Recheck the allowlist, base SHA and evidence bindings immediately before writes.
Use ordinary Git/GitHub operations, not the restricted test-agent profile.
Create one new non-force branch, stage only the named verified files, create a
Conventional Commit with the repository-required coauthor trailer, push to the
authorized fork, and open **one draft PR** against the authorized repository/base.

The PR must state classification, what changed, original/post-fix evidence
references, oracle and cleanup results, correspondence limitations, and platform
scope. Link the incident with `Refs`, not an automatic issue-closing keyword.
Do not copy raw UI text, screenshots, logs, local identifiers or secret values.

For a demo, use **DEMO ONLY - DO NOT MERGE**, retain the demo filename and
isolation, and explain that merging disables the intentionally failing showcase.
An emulator demo is not production qualification.

If a push or PR response is lost, query the exact branch/head and repository
before further action. Reuse an already-created matching draft; never create
a duplicate as a retry or force-push to resolve uncertainty.

## Validation and completion

Verify the remote head equals the local commit and the PR is draft with the
intended base and exact file allowlist. Report the commit and PR URL, and state
that nothing was merged and the incident remains open.

The **human PR reviewer** owns review and merge. No result here grants
qualification, future execution, repair, source-apply or issue-closure authority.
