---
applyTo: "**/package.json,**/package-lock.json,**/.npmrc,.github/dependabot.yml,eng/scripts/audit-npm.mjs"
---

# Dependencies and Supply Chain

This repo **ships**. JavaScript here is not throwaway tooling — the DevFlow Inspector client and
VS Code host are packaged and distributed, so a vulnerable transitive dependency reaches users.

## Non-negotiable: audit before you open the PR

If your change adds, removes, or updates **any** npm dependency — including an indirect change
caused by regenerating a lockfile — run this before opening the PR:

```bash
node eng/scripts/audit-npm.mjs
```

It audits every npm project in the repo and verifies each is registered in
`.github/dependabot.yml`:

| Exit | Meaning |
|------|---------|
| `0` | Every project verified clean |
| `1` | `high`/`critical` advisories, an unregistered project, or a missing lockfile |
| `2` | A project could not be checked against the advisory database |

All three must be resolved before the PR goes up. If you are offline, `--allow-unverified`
downgrades exit `2` to a warning — but CI never passes that flag, so an unverifiable project
still fails there.

Do not skip this because "it's only a devDependency". `devDependencies` still execute on
developer machines and CI runners with repo credentials in scope, and they still land in
`package-lock.json`, which is what advisory scanners read.

### If it reports vulnerabilities

```bash
cd <reported-directory>
npm audit fix          # add --force ONLY if you then verify the build and tests still pass
npm ci && npm test     # confirm the fix did not break anything
```

Commit the updated `package-lock.json`. **Remediation is required — there is no "document it
and move on" path**, because the check stays red and would block every later dependency PR.
If no upstream fix exists, raise it with a maintainer and agree an explicit decision (drop the
package, vendor a patch, or consciously change the threshold); do not leave the PR red and
unexplained.

### If it says a project "could NOT be verified"

That is **not** a pass, and it exits `2`. `npm audit` exits non-zero both for real findings and
for an unreachable registry, so an unverified project means you have no signal at all. Re-run
once you have network access.

### If it says a project has no lockfile

A `package.json` with no committed `package-lock.json` has no resolved dependency graph, so it
cannot be audited at all. Run `npm install` and commit the lockfile.

## Adding a new npm manifest

Any new `package.json` with a lockfile must be registered in `.github/dependabot.yml` **in the
same PR** that introduces it. Dependabot's *security* alerts fire repo-wide without config, but
*version* updates only run for directories listed there. An unregistered manifest silently rots
until a batch of advisories forces the issue — exactly how `src/DevFlow/js` (added in #397)
produced PRs #447, #448, and #449 after the fact.

`node eng/scripts/audit-npm.mjs` fails on an unregistered manifest, so run it after adding one.

## Adding a new dependency

Prefer not to. Each new package is permanent attack surface in a shipping artifact. Before adding:

- Can it be done with Node built-ins or an existing dependency?
- Is the package actively maintained, and how large is its transitive tree?
- Pin it in the lockfile (always commit `package-lock.json`) and never use a floating `*` range.

## .NET dependencies

NuGet versions are centrally managed in `Directory.Packages.props` and flow through Maestro/DARC —
**not** Dependabot. Never add a package version to an individual `.csproj`. See
`.github/instructions/packaging.instructions.md`.
