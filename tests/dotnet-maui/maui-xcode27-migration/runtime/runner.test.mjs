import assert from 'node:assert/strict';
import { createHash, randomUUID } from 'node:crypto';
import { appendFileSync, existsSync, mkdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = dirname(fileURLToPath(import.meta.url));
const run = (...args) => spawnSync(process.execPath, [join(root, 'runner.mjs'), ...args],
  { cwd: root, encoding: 'utf8' });

test('preparation is byte-preserving, refuses overwrite, and verifies the compiled inputs', t => {
  const output = join(root, 'results', `validator-${randomUUID()}`);
  t.after(() => rmSync(output, { recursive: true, force: true }));
  const prepared = run('prepare', '--candidate', join(root, 'Candidate'), '--output', output);
  assert.equal(prepared.status, 0, prepared.stderr);
  const metadata = JSON.parse(prepared.stdout);
  const manifest = JSON.parse(readFileSync(metadata.candidateManifest, 'utf8'));
  assert.equal(manifest.provenance.kind, 'reference-control');
  assert.equal(manifest.provenance.evaluationId, null);
  assert.deepEqual(readFileSync(join(metadata.candidateDirectory, 'MigrationCallbacks.cs')),
    readFileSync(join(root, 'Candidate', 'MigrationCallbacks.cs')));
  assert.equal(run('prepare', '--candidate', join(root, 'Candidate'), '--output', output).status, 1);
  const verify = () => run('verify-build', '--candidate', metadata.candidateDirectory,
    '--manifest', metadata.candidateManifest);
  assert.equal(verify().status, 0);
  // Mutate this unit test's disposable copy, never an evaluation artifact.
  appendFileSync(join(metadata.candidateDirectory, 'MigrationCallbacks.cs'), '\n// unit-test mutation\n');
  const rejected = verify();
  assert.equal(rejected.status, 1);
  assert.match(rejected.stderr, /byte-for-byte/);
});

test('the bundled control cannot be labeled agent output', () => {
  const rejected = run('prepare', '--candidate', join(root, 'Candidate'),
    '--output', join(root, 'results', `not-created-${randomUUID()}`),
    '--candidate-kind', 'agent-output', '--evaluation-id', 'not-a-real-evaluation');
  assert.equal(rejected.status, 1);
  assert.match(rejected.stderr, /reference control is not agent evaluation output/);
});

test('preparation does not allow output outside runtime', () => {
  const rejected = run('prepare', '--candidate', join(root, 'Candidate'), '--output', join(root, '..', 'not-created'));
  assert.equal(rejected.status, 1);
  assert.match(rejected.stderr, /child directory of runtime/);
});

test('a symlink ancestor cannot redirect preparation or result writes outside runtime', t => {
  const directory = join(root, 'results', `symlink-validator-${randomUUID()}`);
  mkdirSync(directory, { recursive: true });
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const link = join(directory, 'escape');
  symlinkSync(dirname(root), link, 'junction');
  const name = `must-not-be-created-${randomUUID()}`;
  const output = join(link, name);
  const rejected = run('prepare', '--candidate', join(root, 'Candidate'), '--output', output);
  assert.equal(rejected.status, 1);
  assert.match(rejected.stderr, /symlink ancestors/);
  assert.equal(existsSync(join(dirname(root), name)), false);
  const resultRejected = run('run-ios', '--prepared', output, '--app', 'not-used');
  assert.equal(resultRejected.status, 1);
  assert.match(resultRejected.stderr, /symlink ancestors/);
});

function appPreflightFixture(t, platform, failure) {
  const directory = join(root, 'results', `preflight-validator-${randomUUID()}`);
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const prepared = join(directory, 'prepared');
  const preparation = run('prepare', '--candidate', join(root, 'Candidate'), '--output', prepared);
  assert.equal(preparation.status, 0, preparation.stderr);
  const manifest = JSON.parse(readFileSync(join(prepared, 'candidate-manifest.json'), 'utf8'));
  const app = join(directory, 'Fixture.app');
  const contents = platform === 'ios' ? app : join(app, 'Contents');
  const resources = platform === 'ios' ? app : join(contents, 'Resources');
  const executableDirectory = platform === 'ios' ? app : join(contents, 'MacOS');
  mkdirSync(resources, { recursive: true });
  mkdirSync(executableDirectory, { recursive: true });
  // Synthetic bundle files test identity checks only; they are never executed.
  const executableBytes = Buffer.from('not a native executable: host unit test only');
  writeFileSync(join(executableDirectory, 'Fixture'), executableBytes);
  writeFileSync(join(contents, 'Info.plist'), `<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>${failure === 'identity' ? 'wrong.app' : 'com.example.xcode27qualification'}</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleExecutable</key><string>Fixture</string>
<key>CFBundleVersion</key><string>1</string>
</dict></plist>`);
  writeFileSync(join(resources, 'candidate-manifest.json'),
    JSON.stringify(failure === 'manifest' ? { ...manifest, id: 'stale-build' } : manifest));
  const commands = join(directory, 'device-commands.log');
  const bin = join(directory, 'bin');
  mkdirSync(bin);
  for (const command of ['xcrun', 'xcodebuild']) {
    writeFileSync(join(bin, command),
      '#!/bin/sh\nprintf "%s\\n" "$0 $*" >> "$QUALIFICATION_COMMAND_LOG"\nexit 23\n', { mode: 0o755 });
  }
  const invoke = (...argumentsList) => spawnSync(process.execPath,
    [join(root, 'runner.mjs'), ...argumentsList], {
      cwd: root, encoding: 'utf8', timeout: 10_000,
      env: { ...process.env, PATH: `${bin}:${process.env.PATH}`, QUALIFICATION_COMMAND_LOG: commands },
    });
  return { directory, prepared, app, manifest, executableBytes, commands, invoke };
}

// The runner uses the macOS-shipped plist reader; these tests never invoke simctl/open.
for (const failure of ['identity', 'manifest']) {
  test(`rejects wrong app ${failure} before any device command`, { skip: process.platform !== 'darwin' }, t => {
    for (const platform of ['ios', 'maccatalyst']) {
      const fixture = appPreflightFixture(t, platform, failure);
      const result = fixture.invoke(platform === 'ios' ? 'run-ios' : 'run-catalyst',
        '--prepared', fixture.prepared, '--app', fixture.app, '--toolchain', 'supplemental',
        '--build-log', join(fixture.directory, 'never-read.log'), '--udid', 'unit-test-only',
        '--events', join(fixture.directory, 'never-read.jsonl'));
      assert.equal(result.status, 1, result.stderr);
      assert.match(result.stderr, failure === 'identity' ? /bundle identity/ : /published manifest/);
      assert.equal(existsSync(fixture.commands), false, 'Preflight rejection must precede xcodebuild and all device commands.');
    }
  });
}

test('read-only app preflight records identity and hashes without asserting authenticity',
  { skip: process.platform !== 'darwin' }, t => {
    const fixture = appPreflightFixture(t, 'ios');
    const result = fixture.invoke('verify-app', '--prepared', fixture.prepared,
      '--app', fixture.app, '--platform', 'ios');
    assert.equal(result.status, 0, result.stderr);
    const evidence = JSON.parse(result.stdout);
    assert.equal(evidence.bundleIdentifier, 'com.example.xcode27qualification');
    assert.equal(evidence.manifestId, fixture.manifest.id);
    assert.equal(evidence.executableSha256, createHash('sha256').update(fixture.executableBytes).digest('hex'));
    assert.match(evidence.appSha256, /^[a-f0-9]{64}$/);
    assert.match(evidence.assurance, /not cryptographic authenticity/);
    assert.equal(existsSync(fixture.commands), false);
  });
