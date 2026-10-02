import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, mkdtempSync, mkdirSync, readFileSync, renameSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';

const yaml = readFileSync(new URL('./eval.yaml', import.meta.url), 'utf8');
const command = yaml.match(/command_arguments: &migration-audit-command >-\r?\n[ \t]+--eval "([^"\r\n]+)"(?:\r?\n|$)/);
assert.ok(command, 'Keep the native audit grader as one literal --eval argument');
const script = command[1];
const helper = new URL('../../../plugins/dotnet-maui/skills/maui-xcode27-migration/scripts/audit-lifecycle.mjs', import.meta.url);

function fixture(t, source) {
  const root = mkdtempSync(join(tmpdir(), 'x27-acceptance-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  mkdirSync(join(root, '_acceptance'));
  mkdirSync(join(root, 'MyApp'));
  copyFileSync(helper, join(root, '_acceptance', 'audit-lifecycle.mjs'));
  if (source !== undefined) writeFileSync(join(root, 'MyApp', 'Scene.cs'), source);
  return root;
}

function run(root, prelude = '') {
  const result = spawnSync(process.execPath, ['--eval', prelude + script], {
    cwd: root, encoding: 'utf8', timeout: 20_000,
  });
  assert.ifError(result.error);
  return result;
}

test('native grading executes the reviewed instrument and preserves its no-patterns result', t => {
  const root = fixture(t, 'class Scene { void Connect() { foreach (var context in options.UrlContexts?.ToArray<UIOpenUrlContext>() ?? []) Queue(context); } }');
  const result = run(root);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /no-patterns-found/);
});

test('native grading rejects the observed single-cold-URL failure', t => {
  const root = fixture(t, 'class Scene { void Connect() { var url = options.UrlContexts?.ToArray<UIOpenUrlContext>().FirstOrDefault()?.Url; } }');
  const result = run(root);
  assert.equal(result.status, 1, result.stderr);
  assert.match(result.stdout, /X27_SINGLE_URL/);
});

test('native grading rejects retained application-window access', t => {
  const root = fixture(t, 'class AppDelegate : MauiUIApplicationDelegate { void Launch() { Window.RootViewController.Title = "lost"; } }');
  const result = run(root);
  assert.equal(result.status, 1, result.stderr);
  assert.match(result.stdout, /X27_APP_WINDOW/);
});

test('native grading cannot pass when its copied instrument is changed to return success', t => {
  const root = fixture(t, 'class Scene {}');
  writeFileSync(join(root, '_acceptance', 'audit-lifecycle.mjs'), 'process.exit(0);');
  const result = run(root);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Acceptance instrument changed/);
});

test('native grading fails when the instrument is missing', t => {
  const root = fixture(t, 'class Scene {}');
  rmSync(join(root, '_acceptance', 'audit-lifecycle.mjs'));
  assert.notEqual(run(root).status, 0);
});

test('native grading retains an incomplete-scan failure', t => {
  const root = fixture(t);
  const result = run(root);
  assert.equal(result.status, 2, result.stderr);
});

test('native grading executes through a linked ancestor and rejects its real findings', t => {
  const root = fixture(t, 'class AppDelegate : MauiUIApplicationDelegate { void Launch() { Window.RootViewController.Title = "lost"; } }');
  const target = join(root, '_instrument');
  renameSync(join(root, '_acceptance'), target);
  symlinkSync(target, join(root, '_acceptance'), process.platform === 'win32' ? 'junction' : 'dir');
  const result = run(root);
  assert.equal(result.status, 1, result.stderr);
  assert.match(result.stdout, /X27_APP_WINDOW/);
});

const validReport = { status: 'no-patterns-found', filesScanned: 1, errors: [], findings: [] };
for (const [name, stdout] of [
  ['empty stdout', ''],
  ['malformed JSON', '{'],
  ['null JSON', 'null'],
  ['missing fields', '{}'],
  ['zero files', JSON.stringify({ ...validReport, filesScanned: 0 })],
  ['fractional count', JSON.stringify({ ...validReport, filesScanned: 0.5 })],
  ['wrong status', JSON.stringify({ ...validReport, status: 'incomplete' })],
  ['reported error', JSON.stringify({ ...validReport, errors: [{ message: 'unreadable' }] })],
  ['reported finding', JSON.stringify({ ...validReport, findings: [{ rule: 'X27_APP_WINDOW' }] })],
]) {
  test(`native grading rejects exit-zero with ${name}`, t => {
    const root = fixture(t, 'class Scene {}');
    const prelude = `require('node:child_process').spawnSync=()=>(${JSON.stringify({ status: 0, stdout, stderr: '' })});`;
    const result = run(root, prelude);
    assert.notEqual(result.status, 0);
    assert.match(result.stderr, /Acceptance instrument did not produce/);
  });
}
