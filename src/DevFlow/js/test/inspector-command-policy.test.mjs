import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import {
  COMMAND_DEFINITIONS,
  evaluateCommandStates,
  normalizeAgentCapabilities,
  resolveCommandFocus,
} from '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-command-policy.js';

const inspectorHtml = readFileSync(
  new URL('../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector.html', import.meta.url),
  'utf8',
);

function modernAgent({ layout = true, actions = true, tree = true } = {}) {
  const capabilities = {
    'ui.tree': { supported: tree, features: tree ? ['css-selector'] : [] },
    'ui.actions': { supported: actions, features: actions ? ['tap', 'navigate', 'properties'] : [] },
    'ui.screenshot': { supported: true, features: ['fullscreen'] },
  };
  if (layout)
    capabilities['ui.layoutDiagnostics'] = { features: ['clipping', 'coverage'] };
  return normalizeAgentCapabilities({ capabilities });
}

function inputs(overrides = {}) {
  return {
    agent: modernAgent(),
    access: {
      known: true,
      connected: true,
      healthy: true,
      driveAllowed: true,
      controlAllowed: true,
      driveMessage: null,
      isWriter: true,
      heldByOther: false,
      owner: null,
    },
    element: { selected: true, hasSource: true },
    workflow: {
      recording: false,
      recordingStopping: false,
      replaying: false,
      loaded: true,
      checkpoint: true,
    },
    data: { available: true },
    ...overrides,
  };
}

test('every rendered Inspector toolbar command is owned by the shared policy', () => {
  const toolbar = inspectorHtml.slice(
    inspectorHtml.indexOf('id="df-toolbar"'),
    inspectorHtml.indexOf('id="df-status"'),
  );
  const rendered = [...toolbar.matchAll(/<button\b[^>]*\bid="([^"]+)"/g)]
    .map(match => match[1])
    .filter(id => id !== 'df-more');
  assert.deepEqual(
    rendered,
    COMMAND_DEFINITIONS
      .filter(command => command.group !== 'viewport')
      .map(command => command.id),
  );
});

test('viewport header actions use the same screenshot capability policy', () => {
  const viewportCommands = COMMAND_DEFINITIONS.filter(command => command.group === 'viewport');
  assert.deepEqual(viewportCommands.map(command => command.id), [
    'df-zoom-out',
    'df-zoom-in',
    'df-reset-view',
  ]);
  for (const command of viewportCommands) {
    assert.match(inspectorHtml, new RegExp(`<button id="${command.id}"`));
    assert.deepEqual(command.requiredCapabilities, [{ key: 'ui.screenshot' }]);
  }
});

test('legacy metadata stays usable and explicit unsupported domains hide commands', () => {
  const legacy = normalizeAgentCapabilities({ capabilities: {} });
  const legacyStates = evaluateCommandStates(inputs({ agent: legacy }));
  assert.equal(legacy.known, false);
  assert.equal(legacyStates['df-toggle-diagnostics'].visible, true);
  assert.equal(legacyStates['df-mode-interact'].visible, true);

  const withoutLayout = evaluateCommandStates(inputs({ agent: modernAgent({ layout: false }) }));
  assert.equal(withoutLayout['df-toggle-diagnostics'].visible, false);
  assert.equal(withoutLayout['df-toggle-diagnostics'].availability, 'unsupported');

  const withoutActions = evaluateCommandStates(inputs({ agent: modernAgent({ actions: false }) }));
  assert.equal(withoutActions['df-mode-interact'].visible, false);
  assert.equal(withoutActions['df-toggle-record'].visible, false);
  assert.equal(withoutActions['df-mode-inspect'].visible, true);

  assert.equal(normalizeAgentCapabilities({ capabilities: { 'ui.actions': null } }), null);
  assert.equal(normalizeAgentCapabilities({
    capabilities: { 'ui.actions': { supported: 'false' } },
  }), null);
});

test('active recording controls remain available when start capability or Preview health degrades', () => {
  const workflow = {
    recording: true,
    recordingStopping: false,
    replaying: false,
    loaded: false,
    checkpoint: false,
  };
  const degraded = evaluateCommandStates(inputs({
    agent: modernAgent({ actions: false }),
    access: {
      known: true,
      connected: true,
      healthy: false,
      driveAllowed: false,
      controlAllowed: true,
      driveMessage: 'Preview delayed - retry capture before interacting.',
      isWriter: true,
      heldByOther: false,
      owner: null,
    },
    workflow,
  }));
  assert.equal(degraded['df-toggle-record'].visible, true);
  assert.equal(degraded['df-toggle-record'].enabled, true);

  const disconnectedWriter = evaluateCommandStates(inputs({
    access: {
      known: true,
      connected: false,
      healthy: false,
      driveAllowed: false,
      controlAllowed: true,
      driveMessage: 'App disconnected - waiting for DevFlow to reconnect.',
      isWriter: true,
      heldByOther: false,
      owner: null,
    },
    workflow,
  }));
  assert.equal(disconnectedWriter['df-toggle-record'].enabled, true);

  const readOnly = evaluateCommandStates(inputs({
    access: {
      known: true,
      connected: true,
      healthy: true,
      driveAllowed: false,
      controlAllowed: false,
      driveMessage: 'Read-only - VS Code Inspector is driving.',
      isWriter: false,
      heldByOther: true,
      owner: 'VS Code Inspector',
    },
    workflow,
  }));
  assert.equal(readOnly['df-toggle-record'].availability, 'readOnly');
  assert.match(readOnly['df-toggle-record'].reason.text, /VS Code Inspector is driving/);

  const disconnectedWithoutControl = evaluateCommandStates(inputs({
    access: {
      known: true,
      connected: false,
      healthy: false,
      driveAllowed: false,
      controlAllowed: false,
      driveMessage: 'App disconnected - waiting for DevFlow to reconnect.',
      isWriter: true,
      heldByOther: false,
      owner: null,
    },
    workflow,
  }));
  assert.equal(disconnectedWithoutControl['df-toggle-record'].availability, 'disconnected');
  assert.equal(disconnectedWithoutControl['df-toggle-record'].reason.code, 'app-disconnected');
});

test('disconnected, delayed Preview, read-only, and missing-selection reasons stay distinct', () => {
  const disconnected = evaluateCommandStates(inputs({
    access: {
      known: true,
      connected: false,
      healthy: false,
      driveAllowed: false,
      controlAllowed: false,
      driveMessage: 'App disconnected - waiting for DevFlow to reconnect.',
      isWriter: false,
      heldByOther: false,
      owner: null,
    },
  }));
  assert.equal(disconnected['df-toggle-record'].availability, 'disconnected');
  assert.equal(disconnected['df-toggle-record'].reason.code, 'app-disconnected');

  const delayed = evaluateCommandStates(inputs({
    access: {
      known: true,
      connected: true,
      healthy: false,
      driveAllowed: false,
      controlAllowed: true,
      driveMessage: 'Preview delayed - retry capture before interacting.',
      isWriter: true,
      heldByOther: false,
      owner: null,
    },
  }));
  assert.equal(delayed['df-toggle-replay'].availability, 'unavailable');
  assert.equal(delayed['df-toggle-replay'].reason.code, 'preview-not-current');

  const delayedReadOnly = evaluateCommandStates(inputs({
    access: {
      known: true,
      connected: true,
      healthy: false,
      driveAllowed: false,
      controlAllowed: false,
      driveMessage: 'Preview delayed - retry capture before interacting.',
      isWriter: false,
      heldByOther: true,
      owner: 'VS Code Inspector',
    },
  }));
  assert.equal(delayedReadOnly['df-toggle-replay'].reason.code, 'preview-not-current');

  const noSelection = evaluateCommandStates(inputs({
    element: { selected: false, hasSource: false },
  }));
  assert.equal(noSelection['df-open-source'].reason.code, 'source-required');
});

test('take control waits for the first lease result and focus skips hidden commands', () => {
  const unknownLease = evaluateCommandStates(inputs({
    access: {
      known: false,
      connected: true,
      healthy: true,
      driveAllowed: false,
      controlAllowed: false,
      driveMessage: null,
      isWriter: false,
      heldByOther: false,
      owner: null,
    },
  }));
  assert.equal(unknownLease['df-take-control'].visible, false);

  const order = COMMAND_DEFINITIONS.map(command => command.id);
  const states = evaluateCommandStates(inputs({ agent: modernAgent({ layout: false }) }));
  assert.equal(resolveCommandFocus(order, states, 'df-toggle-diagnostics'), 'df-assert');
  assert.equal(resolveCommandFocus(order, states, 'not-a-command'), null);
});
