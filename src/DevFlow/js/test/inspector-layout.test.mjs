import test from 'node:test';
import assert from 'node:assert/strict';
import {
  LAYOUT_FINDING_LIMIT,
  createLayoutEventTracker,
  filterLayoutFindings,
  layoutContextPayload,
  layoutRecheckMessage,
  layoutReportView,
  layoutRootElementId,
  layoutRuleLabel,
} from '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-layout.js';

const finding = (id = 'f1', outcome = 'observation') => ({
  id,
  ruleId: 'layout.desired-size-constrained',
  outcome,
  severity: 'info',
  confidence: 'medium',
  message: 'Existing desired size exceeds the allocation.',
  element: { id: 'view1', type: 'Label', automationId: 'Target' },
});
const report = (findings = []) => ({
  schemaVersion: '1.0',
  ruleSetVersion: '1.1',
  snapshot: { id: 's1', stable: true, nodeCount: 4, capturedAt: 'now' },
  coverage: { overall: 'partial', rules: [], limitations: ['Native evidence unavailable.'] },
  summary: { violations: 0, observations: findings.length, incomplete: 0, filtered: 2 },
  findings,
});

test('original friendly rule labels include the new baseline checks', () => {
  assert.equal(layoutRuleLabel('layout.desired-size-constrained'), 'Content under size pressure');
  assert.equal(layoutRuleLabel('layout.child-outside-parent'), 'Child outside parent');
  assert.equal(layoutRuleLabel('layout.future-rule'), 'future rule');
});

test('default scope includes both flyout and detail, preserving a shared navigation ancestor', () => {
  const shell = { id: 'shell', type: 'MainShell' };
  const menu = { id: 'menu', type: 'ContentPage', parentId: 'shell' };
  const navigation = { id: 'navigation', type: 'NavigationPage', parentId: 'shell' };
  const detail = { id: 'detail', type: 'DetailPage', parentId: 'navigation' };
  assert.equal(layoutRootElementId([shell, menu, navigation, detail]), 'shell');
  assert.equal(layoutRootElementId([shell, navigation, detail]), 'navigation');
  assert.equal(layoutRootElementId([shell, { ...menu, isVisible: false }, navigation, detail]), 'navigation');
  assert.equal(layoutRootElementId([shell, { ...detail, parentId: 'shell' }]), 'detail');
});

test('default scope handles non-Page roots and does not guess between disconnected pages', () => {
  assert.equal(layoutRootElementId([{ id: 'window', type: 'Window' }]), 'window');
  assert.equal(layoutRootElementId([]), null);
  assert.equal(layoutRootElementId([{ id: 'a', type: 'OnePage' }, { id: 'b', type: 'TwoPage' }]), null);
  assert.equal(layoutRootElementId([{ id: 'a', type: 'OnePage', parentId: 'a' }]), 'a');
});

test('event-stream reconnection snapshots do not invalidate an unchanged layout', () => {
  const tracker = createLayoutEventTracker();
  for (const timestamp of ['first connection', 'second connection']) {
    const changed = tracker.beginConnection();
    assert.equal(changed({ type: 'lifecycle', timestamp, data: { state: 'started' } }), false);
    assert.equal(changed({ type: 'navigation', timestamp, data: { from: null, to: '//layout' } }), false);
  }
  const changed = tracker.beginConnection();
  assert.equal(changed({ type: 'lifecycle', timestamp: 'third connection', data: { state: 'started' } }), false);
  assert.equal(changed({ type: 'navigation', timestamp: 'third connection', data: { from: null, to: '//other' } }), true);
});

test('same-route navigation and repeated lifecycle events are not connection seeds', () => {
  const changed = createLayoutEventTracker().beginConnection();
  const started = { type: 'lifecycle', timestamp: 'seed', data: { state: 'started' } };
  const currentRoute = { type: 'navigation', timestamp: 'seed', data: { from: null, to: '//layout' } };
  assert.equal(changed(started), false);
  assert.equal(changed(currentRoute), false);
  assert.equal(changed({ ...currentRoute, timestamp: 'reload' }), true);
  assert.equal(changed(currentRoute), true);
  assert.equal(changed({ type: 'treeChange' }), true);
  assert.equal(changed({ type: 'themeChange' }), true);
  assert.equal(changed({ type: 'navigation', data: { from: '//layout', to: '//layout' } }), true);
  assert.equal(changed({ type: 'lifecycle', data: { state: 'stopped' } }), true);
  assert.equal(changed(started), true);
  assert.equal(changed({ type: 'heartbeat' }), false);
});

test('an absent navigation seed does not consume the first real navigation', () => {
  for (const timestamp of ['later navigation', undefined]) {
    const changed = createLayoutEventTracker().beginConnection();
    assert.equal(changed({ type: 'lifecycle', timestamp: 'seed', data: { state: 'started' } }), false);
    assert.equal(changed({ type: 'navigation', timestamp, data: { from: null, to: '//layout' } }), true);
  }
  const changed = createLayoutEventTracker().beginConnection();
  assert.equal(changed({ type: 'navigation', data: { from: null, to: '//layout' } }), true);
});

test('an empty partial or unstable result cannot be presented as complete', () => {
  const partial = layoutReportView(report());
  assert.equal(partial.complete, false);
  assert.match(partial.empty, /incomplete/);
  assert.match(partial.summary, /2 filtered/);
  const unstable = report();
  unstable.coverage.overall = 'complete';
  unstable.snapshot.stable = false;
  assert.equal(layoutReportView(unstable).label, 'Unstable snapshot');
  assert.equal(layoutReportView(unstable).complete, false);
  unstable.snapshot.stable = true;
  unstable.snapshot.nodeCount = 0;
  assert.equal(layoutReportView(unstable).complete, false);
});

test('filters separate informational observations from actionable findings', () => {
  const violation = { ...finding('bad', 'violation'), severity: 'moderate', confidence: 'high' };
  const values = [finding(), violation, { ...finding('suppressed'), suppressed: true }];
  assert.equal(filterLayoutFindings(values).length, 2);
  assert.deepEqual(filterLayoutFindings(values, { outcome: 'actionable' }), [violation]);
  assert.deepEqual(filterLayoutFindings(values, { severity: 'moderate' }), [violation]);
  assert.deepEqual(filterLayoutFindings(values, { confidence: 'high' }), [violation]);
  assert.equal(filterLayoutFindings(values, { rule: 'DESIRED-SIZE', includeSuppressed: true }).length, 3);
  assert.equal(layoutReportView(report(values), { rule: 'missing' }).empty, 'No findings match these filters');
});

test('the dock bounds rendered findings without hiding total or matching counts', () => {
  const values = Array.from({ length: 251 }, (_, index) => finding(`f${index}`));
  const view = layoutReportView(report(values));
  assert.equal(view.findings.length, LAYOUT_FINDING_LIMIT);
  assert.equal(view.matching, 251);
  assert.equal(view.total, 251);
  assert.equal(view.truncated, true);
});

test('a missing finding in partial coverage is not called resolved', () => {
  assert.match(layoutRecheckMessage(finding(), report()), /cannot confirm resolution/);
  assert.match(layoutRecheckMessage(finding(), report([finding('new-id')])), /still present/);
  const complete = report();
  complete.coverage.overall = 'complete';
  assert.match(layoutRecheckMessage(finding(), complete), /not observed.*complete/);
});

test('copy payload is structural, bounded and does not include control text', () => {
  const value = finding();
  value.element.text = 'private content';
  value.evidence = {
    text: { text: 'private content', textLength: 15 },
    sizing: { desiredWidth: 120, arrangedWidth: 100, arbitraryValue: 'secret' },
    limitations: ['Layout and rendered coordinates differ.'],
    nativeProperties: { password: 'secret' },
  };
  const input = report([value]);
  input.summary.arbitraryValue = 'private extension';
  input.coverage.rules = [{ ruleId: value.ruleId, support: 'full', confidence: 'high', secret: 'private extension' }];
  const payload = layoutContextPayload(input, 'f1');
  const json = JSON.stringify(payload);
  assert.equal(payload.findings[0].sizing.desiredWidth, 120);
  assert.doesNotMatch(json, /private content|private extension|textLength|arbitraryValue|nativeProperties/);
  assert.equal(payload.summary.filtered, 2);
  assert.equal(payload.coverage.rules[0].ruleId, value.ruleId);
  assert.equal(layoutContextPayload(report([value]), 'missing').findings.length, 0);
  const many = layoutContextPayload(report(Array.from({ length: 120 }, (_, index) => finding(`f${index}`))));
  assert.equal(many.findings.length, 100);
  assert.equal(many.truncated, true);
});
