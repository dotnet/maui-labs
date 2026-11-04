// Pure capability and availability policy for the shared Inspector commands.

const UNSUPPORTED = 'unsupported';

const REQUIREMENT_REASONS = Object.freeze({
  selected: Object.freeze({ code: 'selection-required', text: 'Select an element first.' }),
  source: Object.freeze({ code: 'source-required', text: 'Select an element with XAML source mapping.' }),
  workflow: Object.freeze({ code: 'workflow-required', text: 'Load or record a workflow first.' }),
  recording: Object.freeze({ code: 'recording-required', text: 'Start recording before adding an assertion.' }),
  checkpoint: Object.freeze({ code: 'checkpoint-required', text: 'Record or replay a workflow with a start route first.' }),
  context: Object.freeze({
    code: 'context-required',
    text: 'Select an element, load a workflow, or open a Data view first.',
  }),
});

const BLOCK_REASONS = Object.freeze({
  replaying: Object.freeze({ code: 'replay-in-progress', text: 'Replay is already in progress.' }),
  recording: Object.freeze({ code: 'recording-active', text: 'Stop the active recording first.' }),
  recordingStopping: Object.freeze({ code: 'recording-stopping', text: 'Recording is stopping.' }),
});

export const COMMAND_DEFINITIONS = Object.freeze([
  Object.freeze({
    id: 'df-mode-interact',
    name: 'Interact',
    group: 'modes',
    requiredCapabilities: ['ui.actions'],
    requiredAccess: 'drive',
    permissionVerb: 'interact',
  }),
  Object.freeze({
    id: 'df-mode-inspect',
    name: 'Inspect',
    group: 'modes',
    requiredCapabilities: ['ui.tree'],
  }),
  Object.freeze({
    id: 'df-toggle-tree',
    name: 'Tree',
    group: 'primary',
    requiredCapabilities: ['ui.tree'],
  }),
  Object.freeze({
    id: 'df-toggle-fit',
    name: 'Fit',
    group: 'primary',
    requiredCapabilities: ['ui.screenshot'],
  }),
  Object.freeze({
    id: 'df-toggle-record',
    name: 'Record',
    group: 'primary',
    requiredCapabilities: ['ui.actions'],
    requiredAccess: 'drive',
    activeAccess: 'control',
    permissionVerb: 'record',
    blockedBy: ['recordingStopping', 'replaying'],
  }),
  Object.freeze({
    id: 'df-toggle-bounds',
    name: 'Bounds',
    group: 'secondary',
    requiredCapabilities: ['ui.tree'],
  }),
  Object.freeze({
    id: 'df-toggle-diagnostics',
    name: 'Layout',
    group: 'secondary',
    requiredCapabilities: ['ui.layoutDiagnostics'],
  }),
  Object.freeze({
    id: 'df-assert',
    name: 'Assert',
    group: 'secondary',
    requiredCapabilities: ['ui.actions'],
    requiredAccess: 'drive',
    permissionVerb: 'add assertions',
    blockedBy: ['replaying'],
    requirements: ['recording', 'selected'],
  }),
  Object.freeze({
    id: 'df-load-flow',
    name: 'Load test',
    group: 'secondary',
    blockedBy: ['recording', 'replaying'],
  }),
  Object.freeze({
    id: 'df-toggle-replay',
    name: 'Replay',
    group: 'secondary',
    requiredCapabilities: ['ui.actions'],
    requiredAccess: 'drive',
    permissionVerb: 'replay',
    blockedBy: ['recording', 'replaying'],
    requirements: ['workflow'],
    mirrors: ['df-workflow-replay'],
  }),
  Object.freeze({
    id: 'df-goto-checkpoint',
    name: 'Start route',
    group: 'secondary',
    requiredCapabilities: ['ui.actions'],
    requiredAccess: 'drive',
    permissionVerb: 'navigate',
    blockedBy: ['replaying'],
    requirements: ['checkpoint'],
  }),
  Object.freeze({
    id: 'df-open-source',
    name: 'Source',
    group: 'secondary',
    requiredCapabilities: ['ui.tree'],
    requiredAccess: 'connected',
    requirements: ['source'],
  }),
  Object.freeze({
    id: 'df-send-copilot',
    name: 'Copilot',
    group: 'secondary',
    requirements: ['context'],
  }),
  Object.freeze({
    id: 'df-toggle-dock',
    name: 'Data',
    group: 'secondary',
  }),
  Object.freeze({
    id: 'df-take-control',
    name: 'Take control',
    group: 'lease',
    visibilityPolicy: 'when-not-writer',
    requiredAccess: 'connected',
  }),
]);

function isRecord(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function boundedText(value, maxLength = 200) {
  if (typeof value !== 'string') return null;
  const text = value.trim();
  return text ? text.slice(0, maxLength) : null;
}

export function normalizeAgentCapabilities(payload) {
  if (!isRecord(payload?.capabilities)) return null;
  const domainsValue = payload.capabilities;
  const domains = {};
  for (const [key, value] of Object.entries(domainsValue)) {
    if (!isRecord(value)) return null;
    if (Object.prototype.hasOwnProperty.call(value, 'supported') &&
        typeof value.supported !== 'boolean') return null;
    domains[key] = Object.freeze({
      supported: value.supported !== false,
      reason: boundedText(value.reason),
    });
  }

  return Object.freeze({
    known: Object.keys(domains).length > 0,
    domains: Object.freeze(domains),
  });
}

function capabilitySupport(agent, key) {
  const domain = agent?.domains?.[key];
  if (domain)
    return domain.supported
      ? Object.freeze({ supported: true, reason: null })
      : Object.freeze({ supported: false, reason: domain.reason });

  if (key === 'ui.layoutDiagnostics' && agent?.known) {
    return Object.freeze({
      supported: false,
      reason: 'Layout diagnostics are not supported by this agent.',
    });
  }

  return Object.freeze({ supported: null, reason: null });
}

function requirementAvailable(requirement, inputs) {
  switch (requirement) {
    case 'selected':
      return !!inputs.element?.selected;
    case 'source':
      return !!inputs.element?.selected && !!inputs.element?.hasSource;
    case 'workflow':
      return !!inputs.workflow?.loaded;
    case 'recording':
      return !!inputs.workflow?.recording;
    case 'checkpoint':
      return !!inputs.workflow?.checkpoint;
    case 'context':
      return !!inputs.element?.selected || !!inputs.workflow?.loaded || !!inputs.data?.available;
    default:
      return true;
  }
}

function blocked(blocker, inputs) {
  switch (blocker) {
    case 'replaying':
      return !!inputs.workflow?.replaying;
    case 'recording':
      return !!inputs.workflow?.recording;
    case 'recordingStopping':
      return !!inputs.workflow?.recordingStopping;
    default:
      return false;
  }
}

function disabledState(availability, code, text) {
  return Object.freeze({
    visible: true,
    enabled: false,
    availability,
    reason: Object.freeze({ code, text }),
  });
}

function hiddenState(code, text) {
  return Object.freeze({
    visible: false,
    enabled: false,
    availability: 'unsupported',
    reason: Object.freeze({ code, text }),
  });
}

function requiredAccess(command, inputs) {
  if (inputs.workflow?.recording && command.activeAccess)
    return command.activeAccess;
  return command.requiredAccess || null;
}

function accessState(command, inputs) {
  const requirement = requiredAccess(command, inputs);
  if (!requirement) return null;

  const access = inputs.access || {};
  if (requirement === 'connected') {
    if (access.connected !== false) return null;
    return disabledState(
      'disconnected',
      'app-disconnected',
      access.driveMessage || 'App disconnected - waiting for DevFlow to reconnect.');
  }

  const allowed = requirement === 'control' ? access.controlAllowed : access.driveAllowed;
  if (allowed) return null;

  if (requirement !== 'control' && access.connected === false) {
    return disabledState(
      'disconnected',
      'app-disconnected',
      access.driveMessage || 'App disconnected - waiting for DevFlow to reconnect.');
  }

  if (requirement === 'drive' && access.healthy === false) {
    return disabledState(
      'unavailable',
      'preview-not-current',
      access.driveMessage || 'Preview delayed - retry capture before interacting.');
  }

  if (access.isWriter) {
    return disabledState(
      'unavailable',
      'preview-not-current',
      access.driveMessage || 'Preview delayed - retry capture before interacting.');
  }

  const verb = inputs.workflow?.recording && command.activeAccess === 'control'
    ? 'stop recording'
    : (command.permissionVerb || 'use this action');
  if (access.heldByOther) {
    const owner = boundedText(access.owner, 80) || 'Another session';
    return disabledState(
      'readOnly',
      'mutation-lease-held',
      `Read-only - ${owner} is driving. Take control to ${verb}.`);
  }

  if (access.connected === false) {
    return disabledState(
      'disconnected',
      'app-disconnected',
      access.driveMessage || 'App disconnected - waiting for DevFlow to reconnect.');
  }

  return disabledState(
    'readOnly',
    'mutation-lease-required',
    `Read-only - take control to ${verb}.`);
}

export function evaluateCommandStates(inputs, definitions = COMMAND_DEFINITIONS) {
  const result = {};

  for (const command of definitions) {
    let state = null;
    const controlsActiveRecording = !!inputs.workflow?.recording && command.activeAccess === 'control';
    if (!controlsActiveRecording) {
      for (const capability of command.requiredCapabilities || []) {
        const support = capabilitySupport(inputs.agent, capability);
        if (support.supported !== false) continue;
        state = hiddenState(
          'unsupported-capability',
          support.reason || `${command.name} is not supported by this agent.`);
        break;
      }
    }

    if (!state && command.visibilityPolicy === 'when-not-writer' &&
        (!inputs.access?.known || inputs.access?.isWriter)) {
      state = Object.freeze({
        visible: false,
        enabled: false,
        availability: 'not-applicable',
        reason: null,
      });
    }

    if (!state) state = accessState(command, inputs);

    if (!state) {
      for (const blocker of command.blockedBy || []) {
        if (!blocked(blocker, inputs)) continue;
        const reason = BLOCK_REASONS[blocker];
        state = disabledState('unavailable', reason.code, reason.text);
        break;
      }
    }

    if (!state) {
      for (const requirement of command.requirements || []) {
        if (requirementAvailable(requirement, inputs)) continue;
        const reason = REQUIREMENT_REASONS[requirement];
        state = disabledState('unavailable', reason.code, reason.text);
        break;
      }
    }

    result[command.id] = state || Object.freeze({
      visible: true,
      enabled: true,
      availability: 'available',
      reason: null,
    });
  }

  return Object.freeze(result);
}

export function resolveCommandFocus(order, states, focusedId) {
  const index = order.indexOf(focusedId);
  if (index < 0) return null;
  for (let next = index + 1; next < order.length; next++) {
    if (states[order[next]]?.visible) return order[next];
  }
  for (let previous = index - 1; previous >= 0; previous--) {
    if (states[order[previous]]?.visible) return order[previous];
  }
  return null;
}
