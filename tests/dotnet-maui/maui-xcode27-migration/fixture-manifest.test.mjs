import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, renameSync, rmSync, statSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';

const yaml = readFileSync(new URL('./eval.yaml', import.meta.url), 'utf8');
const command = yaml.match(/command_arguments: &fixture-manifest-command >-\r?\n[ \t]+--eval "([^"\r\n]+)"(?:\r?\n|$)/);
assert.ok(command, 'Keep the native manifest grader as one literal --eval argument');
const script = command[1];
const digest = value => createHash('sha256').update(value).digest('hex');

function fixture(t) {
  const root = mkdtempSync(join(tmpdir(), 'x27-manifest-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  mkdirSync(join(root, 'App'));
  writeFileSync(join(root, 'App', 'App.cs'), 'original\n');
  return root;
}

function run(root) {
  const result = spawnSync(process.execPath, ['--eval', script], {
    cwd: root, encoding: 'utf8', timeout: 20_000,
  });
  assert.ifError(result.error);
  return result;
}

const original = [['App', 'directory'], ['App/App.cs', digest('original\n')]];

test('native manifest expectations override case-insensitive matching and anchor the whole output', () => {
  const patterns = [...yaml.matchAll(/expected_std_output_matches: '([^\r\n]+)'/g)].map(match => match[1]);
  assert.equal(patterns.length, 2);
  for (const pattern of patterns) {
    assert.ok(pattern.startsWith('(?-i)\\A'));
    assert.ok(pattern.endsWith('\\z'));
  }
});

test('native manifest grader reads exact bytes without changing the fixture', t => {
  const root = fixture(t);
  const path = join(root, 'App', 'App.cs');
  const before = statSync(path, { bigint: true });
  const result = run(root);
  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(JSON.parse(result.stdout), original);
  assert.equal(readFileSync(path, 'utf8'), 'original\n');
  assert.equal(statSync(path, { bigint: true }).mtimeNs, before.mtimeNs);
});

for (const [name, mutate] of [
  ['changed bytes', root => writeFileSync(join(root, 'App', 'App.cs'), 'changed\n')],
  ['deleted source', root => rmSync(join(root, 'App', 'App.cs'))],
  ['case-only source rename', root => renameSync(join(root, 'App', 'App.cs'), join(root, 'App', 'app.cs'))],
  ['additional source', root => writeFileSync(join(root, 'App', 'Extra.cs'), 'extra\n')],
  ['root-level build override', root => writeFileSync(join(root, 'Directory.Build.props'), '<Project/>')],
  ['empty generated directory', root => mkdirSync(join(root, 'App', 'obj'))],
]) {
  test(`native manifest grader exposes ${name}`, t => {
    const root = fixture(t);
    mutate(root);
    const result = run(root);
    assert.equal(result.status, 0, result.stderr);
    assert.notDeepEqual(JSON.parse(result.stdout), original);
  });
}

test('native manifest grader rejects links rather than reading their target', t => {
  const root = fixture(t);
  symlinkSync(join(root, 'App'), join(root, 'Linked'), process.platform === 'win32' ? 'junction' : 'dir');
  const result = run(root);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Unsupported fixture entry: Linked/);
});

test('native manifest grader rejects oversized files', t => {
  const root = fixture(t);
  writeFileSync(join(root, 'Large.cs'), Buffer.alloc(1_048_577));
  const result = run(root);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Unsupported fixture entry: Large.cs/);
});

test('native manifest grader bounds the number of entries', t => {
  const root = fixture(t);
  for (let index = 0; index < 100; index++) {
    writeFileSync(join(root, `extra-${index}.cs`), '');
  }
  const result = run(root);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Fixture tree exceeds limits/);
});
