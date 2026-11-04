import assert from 'node:assert/strict';
import test from 'node:test';

import {
  createAnnouncementGate,
  createInspectorPresentationState,
  createStatusChannel,
  deriveInspectorPresentation,
  updateInspectorPresentationState,
} from '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-presentation.js';

const writer = { isWriter: true, authority: 'agent' };

function liveState(overrides = {}) {
  return createInspectorPresentationState({
    broker: 'reachable',
    agent: 'available',
    capture: { status: 'ok', hasFrame: true },
    stream: 'live',
    ...overrides,
  });
}

test('capture failure preserves stale inspection but blocks mutation', () => {
  const state = updateInspectorPresentationState(liveState(), {
    capture: { status: 'failed', reason: 'capture-failed' },
  });
  const presentation = deriveInspectorPresentation(state, writer);

  assert.equal(presentation.baseKind, 'capture-delayed');
  assert.equal(presentation.showBlockingOverlay, false);
  assert.equal(presentation.showHealthBanner, true);
  assert.equal(presentation.driveAllowed, false);
  assert.equal(presentation.controlAllowed, true);
  assert.match(presentation.driveMessage, /retry capture/i);
  assert.match(presentation.presence.title, /waits for a current Preview/);
});

test('broker and agent failures take precedence over capture health', () => {
  for (const patch of [
    { broker: 'browser-offline' },
    { broker: 'unreachable' },
    { broker: 'timed-out' },
    { agent: 'unavailable' },
    { agent: 'unregistered' },
  ]) {
    const presentation = deriveInspectorPresentation(
      updateInspectorPresentationState(liveState(), patch),
      writer);
    assert.equal(presentation.baseKind, 'disconnected');
    assert.equal(presentation.showBlockingOverlay, true);
    assert.equal(presentation.driveAllowed, false);
  }
});

test('legacy event fallback is visible without adding a live badge', () => {
  const polling = deriveInspectorPresentation(
    updateInspectorPresentationState(liveState(), { stream: 'unsupported' }),
    writer);
  const live = deriveInspectorPresentation(liveState(), writer);

  assert.deepEqual(polling.badges.map(badge => badge.label), ['Polling']);
  assert.deepEqual(live.badges, []);
});

test('resolution degradation comes only from explicit committed clamping', () => {
  const baseline = deriveInspectorPresentation(liveState(), writer);
  const downsampled = deriveInspectorPresentation(liveState(), writer, {
    captureScale: 1,
    requestedCaptureScale: 1,
    nativeScale: 3,
    resolutionClamped: false,
  });
  assert.deepEqual(downsampled.badges, []);

  const clamped = deriveInspectorPresentation(liveState(), writer, {
    captureScale: 0.75,
    requestedCaptureScale: 1.5,
    nativeScale: 3,
    resolutionClamped: true,
  });
  assert.equal(clamped.badges[0].label, 'Resolution limited');
  assert.match(clamped.badges[0].title, /Captured at 0.75×; requested 1.5×; native 3×/);
  assert.equal(clamped.healthAnnouncementKey, baseline.healthAnnouncementKey);
});

test('read-only access remains separate from connection health', () => {
  const presentation = deriveInspectorPresentation(liveState(), {
    isWriter: false,
    heldByOther: true,
    holderLabel: 'A'.repeat(120),
    expiresInMs: 1200,
  });

  assert.equal(presentation.baseKind, 'live');
  assert.equal(presentation.driveAllowed, false);
  assert.match(presentation.presence.label, /^Read-only/);
  assert.match(presentation.driveMessage, /Take control/);
  assert.deepEqual(presentation.badges, []);
});

test('retry changes feedback without changing the underlying health state', () => {
  const state = updateInspectorPresentationState(liveState(), {
    agent: 'unavailable',
    capture: { status: 'failed', reason: 'agent-unavailable' },
    retrying: true,
  });
  const presentation = deriveInspectorPresentation(state, writer);

  assert.equal(presentation.baseKind, 'disconnected');
  assert.equal(presentation.label, 'Retrying');
  assert.equal(presentation.busy, true);
  assert.match(presentation.statusText, /Checking for the running app/);
});

test('lease uncertainty preserves the last known writer recovery controls', () => {
  const delayed = updateInspectorPresentationState(liveState(), {
    capture: { status: 'failed', reason: 'capture-failed' },
  });
  const reliable = deriveInspectorPresentation(delayed, {
    isWriter: true,
    authority: 'agent',
    leaseReliable: true,
  });
  const unknown = deriveInspectorPresentation(liveState(), {
    isWriter: true,
    authority: 'unknown',
    leaseReliable: false,
  });

  assert.equal(reliable.driveAllowed, false);
  assert.equal(reliable.controlAllowed, true);
  assert.equal(unknown.driveAllowed, true);
  assert.equal(unknown.controlAllowed, true);
  assert.match(unknown.accessAnnouncement, /temporarily unavailable/i);
});

test('status channel preserves feature text and reports hidden health fallback', () => {
  const writes = [];
  const states = [];
  const channel = createStatusChannel((text, state) => {
    writes.push(text);
    states.push(state);
  });
  const selection = channel.set('Selected: Button');

  channel.setHealth('App disconnected');
  assert.equal(writes.at(-1), 'Selected: Button');
  assert.equal(states.at(-1).healthRendered, false);
  channel.replace(selection, 'Selected: Label');
  assert.equal(writes.at(-1), 'Selected: Label');
  channel.clear(selection);
  assert.equal(writes.at(-1), 'App disconnected');
  assert.equal(states.at(-1).healthRendered, true);
});

test('announcement gate suppresses identical health transitions', () => {
  const announcements = [];
  const gate = createAnnouncementGate(text => announcements.push(text));

  assert.equal(gate.announce('capture-delayed:settled', 'Preview delayed.'), true);
  assert.equal(gate.announce('capture-delayed:settled', 'Preview delayed.'), false);
  assert.equal(gate.announce('live:settled', 'Preview live.'), true);
  assert.deepEqual(announcements, ['Preview delayed.', 'Preview live.']);
});
