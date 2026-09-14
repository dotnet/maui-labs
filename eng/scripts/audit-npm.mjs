#!/usr/bin/env node
// Audits every npm project in the repo and verifies each is registered with Dependabot.
//
// Why this exists: src/DevFlow/js was added in PR #397 without being registered in
// .github/dependabot.yml and without anyone running `npm audit`. Several advisories only
// surfaced months later, post-merge, as a burst of Dependabot PRs (#447 / #448 / #449).
// Both halves of that failure are checked here so they are caught locally, before the PR.
//
// Usage:
//   node eng/scripts/audit-npm.mjs                      # fail on high/critical (default)
//   node eng/scripts/audit-npm.mjs --audit-level=moderate
//   node eng/scripts/audit-npm.mjs --allow-unverified   # offline: downgrade "unverified" to a warning
//
// Exit codes:
//   0  every project verified clean
//   1  vulnerabilities at/above the threshold, an unregistered project, or a missing lockfile
//   2  at least one project could not be checked against the advisory database (fail closed)

import { spawnSync } from 'node:child_process';
import { readdirSync, readFileSync, existsSync } from 'node:fs';
import { join, relative, dirname, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const dependabotConfig = join(repoRoot, '.github', 'dependabot.yml');

const SEVERITIES = ['info', 'low', 'moderate', 'high', 'critical'];
const args = process.argv.slice(2);
const allowUnverified = args.includes('--allow-unverified');
const threshold = args.find(a => a.startsWith('--audit-level='))?.split('=')[1] ?? 'high';
if (!SEVERITIES.includes(threshold)) {
  console.error(`Invalid --audit-level=${threshold}. Expected one of: ${SEVERITIES.join(', ')}`);
  process.exit(1);
}
const thresholdIndex = SEVERITIES.indexOf(threshold);

const SKIP_DIRS = new Set(['node_modules', 'artifacts', 'bin', 'obj', '.git', '.dotnet']);

// Scaffolding templates that are copied into generated projects. They are not built or
// installed here, so they have no lockfile and must not be treated as real projects.
const NOT_REAL_PROJECTS = new Set(['src/Comet/.squad/templates']);

const toPosix = p => p.split(sep).join('/');

function findFiles(dir, name, found = []) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (!SKIP_DIRS.has(entry.name)) findFiles(join(dir, entry.name), name, found);
    } else if (entry.name === name) {
      found.push(dir);
    }
  }
  return found;
}

// npm workspace members share the root lockfile and are never registered separately with
// Dependabot, so they must not be reported as unregistered or as missing a lockfile.
function workspaceMembersOf(pkgDir) {
  let pkg;
  try {
    pkg = JSON.parse(readFileSync(join(pkgDir, 'package.json'), 'utf8'));
  } catch {
    return [];
  }
  const patterns = Array.isArray(pkg.workspaces) ? pkg.workspaces : pkg.workspaces?.packages;
  if (!Array.isArray(patterns)) return [];

  const members = [];
  for (const pattern of patterns) {
    if (!pattern.includes('*')) {
      members.push(join(pkgDir, ...pattern.split('/')));
      continue;
    }
    // Only the common single-level `dir/*` form is expanded; anything deeper is rare here.
    const base = join(pkgDir, ...pattern.slice(0, pattern.indexOf('*')).split('/').filter(Boolean));
    if (!existsSync(base)) continue;
    for (const entry of readdirSync(base, { withFileTypes: true })) {
      if (entry.isDirectory() && !SKIP_DIRS.has(entry.name)) members.push(join(base, entry.name));
    }
  }
  return members;
}

// Structural-enough parse of dependabot.yml: strips comments, splits on each update block, and
// accepts both the `directory:` scalar and the `directories:` list form that Dependabot allows.
function registeredNpmDirectories() {
  const raw = readFileSync(dependabotConfig, 'utf8')
    .split('\n')
    .filter(line => !/^\s*#/.test(line))
    .join('\n');

  if (!/^\s*updates\s*:/m.test(raw)) {
    throw new Error("no top-level `updates:` key — Dependabot would reject this configuration");
  }

  const dirs = [];
  for (const block of raw.split(/^\s*-\s+package-ecosystem\s*:/m).slice(1)) {
    if (!/^\s*["']?npm["']?\s*$/.test(block.split('\n')[0])) continue;

    const scalar = block.match(/^\s*directory\s*:\s*(.+)$/m)?.[1];
    if (scalar) dirs.push(scalar.trim().replace(/^["']|["']$/g, ''));

    const flow = block.match(/^\s*directories\s*:\s*\[(.+?)\]/ms)?.[1];
    if (flow) {
      for (const d of flow.split(',')) dirs.push(d.trim().replace(/^["']|["']$/g, ''));
    } else if (/^\s*directories\s*:\s*$/m.test(block)) {
      const start = block.split('\n').findIndex(l => /^\s*directories\s*:\s*$/.test(l));
      for (const line of block.split('\n').slice(start + 1)) {
        const item = line.match(/^\s*-\s*(.+)$/);
        if (!item) break;
        dirs.push(item[1].trim().replace(/^["']|["']$/g, ''));
      }
    }
  }
  return dirs.filter(Boolean);
}

// A project root is any package.json that is not a workspace member of another package.json.
const allPkgDirs = findFiles(repoRoot, 'package.json');
const members = new Set(allPkgDirs.flatMap(workspaceMembersOf).map(toPosix));
const roots = allPkgDirs
  .filter(d => !members.has(toPosix(d)))
  .filter(d => !NOT_REAL_PROJECTS.has(toPosix(relative(repoRoot, d))))
  .sort();

if (roots.length === 0) {
  console.log('No npm projects found.');
  process.exit(0);
}

if (!existsSync(dependabotConfig)) {
  console.log('FAIL .github/dependabot.yml is missing — no manifest can be registered.');
  process.exit(1);
}

let registered;
try {
  registered = registeredNpmDirectories();
} catch (e) {
  console.log(`FAIL .github/dependabot.yml could not be parsed: ${e.message}`);
  process.exit(1);
}

const unregistered = [];
const missingLockfile = [];
let vulnerableCount = 0;
let unverifiedCount = 0;

for (const dir of roots) {
  const display = toPosix(relative(repoRoot, dir)) || '.';
  const configPath = '/' + display;

  // A project Dependabot does not know about never receives routine version bumps, so it
  // drifts until an advisory forces the issue. That is the exact #397 failure.
  if (!registered.includes(configPath)) unregistered.push(configPath);

  // Without a committed lockfile there is no resolved dependency graph to audit at all,
  // so a project could otherwise pass simply by being unauditable.
  if (!existsSync(join(dir, 'package-lock.json'))) {
    missingLockfile.push(display);
    console.log(`FAIL ${display} — no package-lock.json, cannot be audited`);
    continue;
  }

  // Explicit --include flags stop a local .npmrc `omit=dev` from silently shrinking coverage;
  // devDependencies run on CI runners with repo credentials in scope and must be audited.
  const result = spawnSync(
    'npm',
    ['audit', '--json', '--include=dev', '--include=optional', '--include=peer'],
    { cwd: dir, encoding: 'utf8', shell: process.platform === 'win32', maxBuffer: 64 * 1024 * 1024 }
  );

  let report;
  try {
    report = JSON.parse(result.stdout);
  } catch {
    report = null;
  }

  // `npm audit` exits 1 both for "found vulnerabilities" and for "could not reach the registry".
  // Only the JSON tells them apart, so an unparseable result must never be read as a pass.
  if (!report?.metadata?.vulnerabilities) {
    unverifiedCount++;
    const reason = (result.stderr || '').trim().split('\n').filter(Boolean).pop() || 'unknown error';
    console.log(`??   ${display} — could NOT be verified (${reason})`);
    continue;
  }

  const counts = report.metadata.vulnerabilities;
  const relevant = SEVERITIES.slice(thresholdIndex).reduce((sum, s) => sum + (counts[s] || 0), 0);

  if (relevant === 0) {
    console.log(`OK   ${display} — no ${threshold}+ vulnerabilities`);
    continue;
  }

  vulnerableCount++;
  const summary = SEVERITIES.slice(thresholdIndex)
    .filter(s => counts[s] > 0)
    .map(s => `${counts[s]} ${s}`)
    .join(', ');
  console.log(`FAIL ${display} — ${summary}`);

  for (const [name, v] of Object.entries(report.vulnerabilities)) {
    if (SEVERITIES.indexOf(v.severity) < thresholdIndex) continue;
    const advisories = (v.via || []).filter(entry => typeof entry === 'object');
    for (const a of advisories) {
      console.log(`       - ${name} (${a.severity}): ${a.title}`);
      console.log(`         ${a.url}`);
    }
    if (advisories.length === 0) {
      console.log(`       - ${name} (${v.severity}): vulnerable via ${(v.via || []).join(', ')}`);
    }
  }
}

console.log('');

if (unregistered.length > 0) {
  console.log('Projects missing from .github/dependabot.yml:');
  for (const dir of unregistered) console.log(`  ${dir}`);
  console.log('Add an npm update block for each so it receives routine version updates.\n');
}

if (missingLockfile.length > 0) {
  console.log('Projects with no committed package-lock.json:');
  for (const dir of missingLockfile) console.log(`  ${dir}`);
  console.log('Run `npm install` and commit the lockfile so the tree can be audited.\n');
}

if (vulnerableCount > 0) {
  console.log(
    `${vulnerableCount} project(s) have ${threshold}+ vulnerabilities. ` +
    'Run `npm audit fix` in the affected directory and commit the updated lockfile.\n'
  );
}

if (unverifiedCount > 0) {
  console.log(
    `${unverifiedCount} project(s) could not be checked against the advisory database. ` +
    'This is NOT a clean result.' +
    (allowUnverified
      ? ' Continuing because --allow-unverified was passed.'
      : ' Re-run once the registry is reachable, or pass --allow-unverified to work offline.')
  );
}

if (vulnerableCount > 0 || unregistered.length > 0 || missingLockfile.length > 0) process.exit(1);
if (unverifiedCount > 0 && !allowUnverified) process.exit(2);
process.exit(0);
