import assert from 'node:assert/strict';
import test from 'node:test';
import { evaluate, requiredContracts } from './assertions.mjs';

// These are validator unit tests, never app/device evidence.
function sample() {
  const input = { manifestId: 'source-sha', startupProcess: 'startup', coldProcess: 'cold',
    warmUrls: ['existingapp://warm-a', 'existingapp://warm-b'], coldUrl: 'existingapp://cold' };
  const event = (kind, extra = {}) => ({ kind, process: 'startup', delivery: 'os', scene: 'scene-a', ...extra });
  const events = [
    event('boot', { detail: { candidate: { id: input.manifestId } } }),
    event('base.connect'), event('base.activate', { window: 'window-a' }),
    event('original.scene-activation'), event('original.application-activation'),
    ...input.warmUrls.flatMap(url => [
      event('base.url', { detail: { urls: [url] } }), event('original.warm-url', { value: url }),
    ]),
    ...requiredContracts.map(name => event('assertion', {
      delivery: 'injected-contract', value: name, detail: { pass: true },
    })),
    event('contract.complete', { delivery: 'injected-contract' }),
    event('boot', { process: 'cold', detail: { candidate: { id: input.manifestId } } }),
    event('base.connect', { process: 'cold', detail: { urls: [input.coldUrl] } }),
    event('original.cold-url', { process: 'cold', value: input.coldUrl, window: 'window-cold',
      detail: { title: input.coldUrl } }),
  ];
  return { input, events };
}

test('a complete subset still reports qualification blocked', () => {
  const { input, events } = sample();
  const result = evaluate(events, input);
  assert.equal(result.runtimeSubset, 'passed');
  assert.equal(result.qualification, 'blocked');
  assert.equal(result.nativeEvaluator, 'not-assessed');
  assert.match(result.coverage.injectedColdCollections, /does not validate UIKit collection extraction/);
  assert.match(result.coverage.priorGeneratedArtifacts, /Not exercised/);
  assert.ok(result.remainingGates.length);
});

test('every missing contract fails closed', () => {
  for (const name of requiredContracts) {
    const { input, events } = sample();
    assert.equal(evaluate(events.filter(e => e.value !== name), input).runtimeSubset, 'failed', name);
  }
});

test('a duplicate or failed assertion cannot count as passing', () => {
  const { input, events } = sample();
  const assertion = events.find(e => e.kind === 'assertion');
  assert.equal(evaluate([...events, assertion], input).runtimeSubset, 'failed');
  assertion.detail.pass = false;
  assert.equal(evaluate(events, input).runtimeSubset, 'failed');
});

test('injected events cannot prove OS URL delivery', () => {
  for (const kind of ['base.url', 'original.warm-url', 'base.connect', 'original.cold-url']) {
    const { input, events } = sample();
    for (const event of events.filter(e => e.kind === kind)) event.delivery = 'injected-contract';
    assert.equal(evaluate(events, input).runtimeSubset, 'failed', kind);
  }
});

test('wrong build identity, same-process cold delivery, and missing title work fail', () => {
  const { input, events } = sample();
  assert.equal(evaluate(events, { ...input, manifestId: 'different-source' }).runtimeSubset, 'failed');
  assert.equal(evaluate(events, { ...input, coldProcess: input.startupProcess }).runtimeSubset, 'failed');
  events.find(e => e.kind === 'original.cold-url').detail.title = 'wrong';
  assert.equal(evaluate(events, input).runtimeSubset, 'failed');
});

test('cold-only initialization during warm delivery is rejected', () => {
  const { input, events } = sample();
  events.push({ kind: 'original.cold-url', process: input.startupProcess, delivery: 'os', value: input.warmUrls[0] });
  assert.equal(evaluate(events, input).runtimeSubset, 'failed');
});

test('warm effects must belong to the OS delivery scene and process', () => {
  for (const change of [{ scene: 'other-scene' }, { process: 'other-process' }]) {
    const { input, events } = sample();
    Object.assign(events.find(e => e.kind === 'original.warm-url'), change);
    const result = evaluate(events, input);
    assert.equal(result.checks.find(c => c.name === 'os-warm-url-1').pass, false);
  }
});

test('duplicate warm effects in another scene are rejected across the process', () => {
  const { input, events } = sample();
  events.push({ ...events.find(e => e.kind === 'original.warm-url'), scene: 'other-scene' });
  assert.equal(evaluate(events, input).checks.find(c => c.name === 'os-warm-url-1').pass, false);
});

test('duplicate cold effects in another scene are rejected across the process', () => {
  const { input, events } = sample();
  events.push({ ...events.find(e => e.kind === 'original.cold-url'), scene: 'other-scene', window: 'other-window' });
  assert.equal(evaluate(events, input).checks.find(c => c.name === 'os-cold-url-new-process').pass, false);
});

test('cold effects must belong to the connection scene and process', () => {
  for (const change of [{ scene: 'other-scene' }, { process: 'other-process' }]) {
    const { input, events } = sample();
    Object.assign(events.find(e => e.kind === 'original.cold-url'), change);
    assert.equal(evaluate(events, input).checks.find(c => c.name === 'os-cold-url-new-process').pass, false);
  }
});
