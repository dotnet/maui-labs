import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const sourcePath = resolve(
  here,
  '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/devflow.js');
const source = readFileSync(sourcePath, 'utf8');
const html = readFileSync(resolve(
  here,
  '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector.html'), 'utf8');
const css = readFileSync(resolve(
  here,
  '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/devflow.css'), 'utf8');
const workbenchCss = readFileSync(resolve(
  here,
  '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-workbench.css'), 'utf8');
const evidence = readFileSync(resolve(
  here,
  '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-evidence.js'), 'utf8');
const properties = readFileSync(resolve(
  here,
  '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-properties.js'), 'utf8');
const steps = readFileSync(resolve(
  here,
  '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-steps.js'), 'utf8');
const dialog = readFileSync(resolve(
  here,
  '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-dialog.js'), 'utf8');
const inspectorServer = readFileSync(resolve(
  here,
  '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/InspectorServer.cs'), 'utf8');
const inspectorRoutes = readFileSync(resolve(
  here,
  '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/InspectorServer.Routes.cs'), 'utf8');
const todoService = readFileSync(resolve(
  here,
  '../../../../samples/DevFlow.Sample/TodoService.cs'), 'utf8');
const todoItem = readFileSync(resolve(
  here,
  '../../../../samples/DevFlow.Sample/TodoItem.cs'), 'utf8');

test('Inspector composition script parses as an ES module', () => {
  const result = spawnSync(process.execPath, ['--check', sourcePath], { encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr || result.stdout);
});

test('Problems renderer is a top-level dock renderer, not nested inside Logs', () => {
  const problems = source.indexOf('  function renderProblems(j) {');
  const logs = source.indexOf('  function renderLogs(j) {');
  const network = source.indexOf('  function networkSignature(');

  assert.ok(problems >= 0);
  assert.ok(logs > problems);
  assert.ok(network > logs);
  assert.equal(source.slice(logs, network).includes('function renderProblems'), false);
});

test('workflow rebuild resume presents a stored recording capability', () => {
  assert.match(source, /recordingCapabilityStorageKey = 'maui-devflow-recording-capabilities-v1'/);
  assert.match(
    source,
    /body: JSON\.stringify\(candidate \? \{ recordingId: candidate \} : \{\}\)/);
  assert.doesNotMatch(source, /requestRecordingStatus\(null\)/);
});

test('Inspector accessibility structure keeps hidden inputs out of tab order', () => {
  assert.match(html, /id="df-workflow-file-input"[^>]*tabindex="-1"[^>]*aria-hidden="true"/);
  assert.match(html, /id="df-hit-candidates"[^>]*role="group"/);
  assert.doesNotMatch(source, /setAttribute\('role', 'option'\)/);
  assert.match(html, /id="df-problems-status"[^>]*aria-live="polite"/);
});

test('Layout discovery stays in Data and reuses shared cached state', () => {
  assert.doesNotMatch(html, /id="df-layout-entry"/);
  assert.match(html, /id="df-toggle-dock"[\s\S]*id="df-data-status"/);
  assert.match(source, /function openLayoutDiagnostics\(options = \{\}\)/);
  assert.match(source, /propertyGrid\.refreshDiagnostics\(\)/);
  assert.match(source, /function updateLayoutStatus\(\)/);
  assert.match(source, /if \(options\.forceScan === true\)[\s\S]*await runLayoutScan\(\)/);
  assert.doesNotMatch(source, /function renderLegacyLayoutDiagnostics/);
  assert.ok(source.indexOf('  function renderLayoutDiagnostics() {') <
    source.indexOf('  async function runLayoutScan() {'));
  assert.match(source, /tab\.textContent = latestLayoutReport && issues\.length \? `Layout \(\$\{issues\.length\}\)` : 'Layout'/);
  assert.match(source, /title: layoutScanBusy \? 'Checking layout' : layoutError \? 'Layout check unavailable' : 'Layout not checked'/);
  assert.match(html, /id="df-dock-panel" role="tabpanel"[\s\S]*id="df-dock-action-strip"[\s\S]*id="df-dock-body" role="region"/);
  assert.doesNotMatch(source, /class: 'df-layout-strip'/);
  assert.doesNotMatch(source, /class: 'df-performance-strip'/);
  assert.match(source, /Showing \$\{view\.findings\.length\} of \$\{view\.matchingFindings\} matching findings/);
  assert.doesNotMatch(properties, /if \(findings\.length === 0\) return null/);
  assert.match(properties, /actions\.append\(action\('Start'/);
  assert.match(properties, /actions\.append\(action\('Retry'/);
});

test('shared Inspector CSS references only defined DevFlow tokens', () => {
  const combined = `${css}\n${workbenchCss}`;
  const definitions = new Set(
    [...combined.matchAll(/(--df-[a-z0-9-]+)\s*:/gi)].map((match) => match[1]));
  const references = new Set(
    [...combined.matchAll(/var\(\s*(--df-[a-z0-9-]+)/gi)].map((match) => match[1]));
  const runtimeTokens = new Set(['--df-workbench-height', '--df-workbench-top']);
  const unresolved = [...references].filter(
    (token) => !definitions.has(token) && !runtimeTokens.has(token));

  assert.deepEqual(unresolved, []);
  assert.doesNotMatch(combined, /font(?:-size)?\s*:[^;]*\b9px\b/);
});

test('Inspector chrome separates modes, destinations, contextual actions, and export', () => {
  assert.match(html, /id="df-mode-interact"[\s\S]*id="df-mode-inspect"/);
  assert.match(html, /id="df-toggle-workbench"[^>]*df-destination/);
  assert.match(html, /id="df-toggle-dock"[^>]*df-destination/);
  assert.match(html, /id="df-toggle-tree"[^>]*df-toggle-control/);
  assert.match(html, /id="df-open-source"[^>]*df-context-action[^>]*hidden/);
  assert.match(html, /id="df-send-copilot"[^>]*aria-label="Add to Copilot"[^>]*hidden/);
  assert.match(html, /id="df-evidence"[^>]*df-export-action/);
  assert.match(html, /<title>MAUI DevFlow Inspector<\/title>/);
});

test('Inspector modes share visual hover while Interact binds tappable targets', () => {
  assert.match(source, /const tapEl = hoverHitTest\(e\.clientX, e\.clientY\)/);
  assert.match(source, /const targetId = isTapTargetOverlay\(targetEl\)/);
  assert.match(source, /if \(targetId\) payload\.elementId = targetId/);
  assert.match(source, /if \(tap\.ok && tap\.target\) await recordStep\('tap', tap\.target\)/);
  assert.match(source, /result\.ok !== true/);
  assert.match(source, /Tap did not run/);
  assert.match(source, /const node = document\.elementFromPoint\(clientX, clientY\)/);
  assert.doesNotMatch(source, /document\.elementsFromPoint\(clientX, clientY\)/);
  assert.match(source, /Interactive element/);
  assert.match(source, /Scrollable element/);
  assert.match(source, /Visual element/);
});

test('active toolbar buttons use only a thin accent border', () => {
  const toolbarState = css.match(
    /\.df-tool-btn\.df-toggle-control\.df-active,[\s\S]*?#df-more\.df-active\s*\{([\s\S]*?)\}/);
  assert.ok(toolbarState);
  assert.match(toolbarState[1], /border-color:\s*var\(--df-accent\)/);
  assert.match(toolbarState[1], /background:\s*var\(--df-surface-2\)/);
  assert.match(toolbarState[1], /box-shadow:\s*none/);
  assert.doesNotMatch(toolbarState[1], /inset|color-mix/);

  const testsState = workbenchCss.match(
    /#df-toggle-workbench\.df-workbench-entry\[aria-expanded="true"\]\s*\{([\s\S]*?)\}/);
  assert.ok(testsState);
  assert.match(testsState[1], /border-color:\s*var\(--df-accent\)/);
  assert.match(testsState[1], /box-shadow:\s*none/);
  assert.doesNotMatch(testsState[1], /inset|color-mix/);

  const overflowState = css.match(
    /\.df-toolbar-overflow \.df-tool-btn\.df-active\s*\{([\s\S]*?)\}/);
  assert.ok(overflowState);
  assert.match(overflowState[1], /border-color:\s*var\(--df-accent\)/);
  assert.match(overflowState[1], /background:\s*var\(--df-surface-2\)/);
  assert.match(overflowState[1], /box-shadow:\s*none/);
  assert.doesNotMatch(overflowState[1], /inset|color-mix/);
});

test('Evidence dialog redirects tab focus back inside when focus escapes', () => {
  assert.match(evidence, /if \(!box\.contains\(document\.activeElement\)\)/);
});

test('Device tab integrates the complete standalone Canvas action inventory', () => {
  for (const action of [
    'create', 'boot', 'shutdown', 'restart', 'reveal', 'erase', 'delete',
    'tap', 'long-press', 'swipe', 'type-text', 'press-button', 'press-key',
    'rotate', 'recording-start', 'recording-stop',
  ]) {
    assert.match(source, new RegExp(`['"]${action}['"]`));
  }
  assert.match(source, /\/api\/device\/host/);
  assert.match(source, /\/api\/device\/screenshot/);
  assert.match(source, /\/api\/device\/recording/);
  assert.match(source, /new device/i);
  assert.match(source, /Exact coordinate input/);
  assert.match(source, /startDeviceScreenshotPolling/);
  assert.match(source, /function inspectorRequestHeaders/);
  assert.match(source, /headers: inspectorRequestHeaders\(\), cache: 'no-store'/);
  assert.match(source, /headers: inspectorRequestHeaders\('application\/json'\)/);
  assert.match(source, /const previous = managedDeviceId/);
  assert.match(source, /managedDeviceId !== previous\) clearManagedDeviceScreenshot/);
  assert.match(source, /response\.device\.id !== managedDeviceId\) clearManagedDeviceScreenshot/);
  assert.match(dialog, /requiredText/);
  assert.match(dialog, /confirm\.disabled = confirmationInput\.value !== requiredText/);
});

// The broker device layer that answers /api/device/host arrives in a later layer. Until it does,
// the panel has to say so rather than fetch a route this Inspector answers with 404.
test('the managed device host stays gated on the surface the server advertises', () => {
  assert.match(source, /deviceHost: metaFlag\('devflow-surface-device-host'\)/);
  assert.match(
    source,
    /async function loadManagedDeviceHost\(\)\s*\{\s*if \(!inspectorSurfaces\.deviceHost\)/);
  assert.match(inspectorRoutes, /devflow-surface-device-host", "\/api\/device\/host", Served: false/);
});

test('workflow assertions never read Text from password entries', () => {
  assert.match(steps, /looksSensitive/);
  assert.match(steps, /Expected value \(never prefilled\)/);
  assert.match(steps, /Never copy its[\s\S]*current value into an authored assertion/);
});

test('sample repeated items expose deterministic stable test identity', () => {
  assert.match(todoService, /_nextItemId = 1/);
  assert.match(todoService, /\$"todo-\{_nextItemId\+\+:D4\}"/);
  assert.match(todoItem, /IDevFlowStableItemKey/);
  assert.match(todoItem, /DevFlowStableItemKey => Id/);
});
