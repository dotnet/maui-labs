// ── The Inspector's single host bridge ──────────────────────────────────────────────────────────
// Every surface (standalone browser, VS Code webview, Copilot Canvas) loads the same page. This
// module is the ONE place that talks to an embedding host, so there is exactly one 'message'
// listener, one host manifest, and one capability vocabulary in the page.
//
// Host identity never selects behaviour. Capabilities do. Identity exists only for presence copy
// and for the broker's source-apply security contract.

// Every operation the page can ask a host to perform. The capability name is the negotiated
// vocabulary; `message` is the wire type, which is deliberately allowed to differ. `mode` fixes the
// completion contract: 'request' operations resolve with a host result, 'notify' operations are
// one-way and can never report completion.
export const HOST_OPERATIONS = Object.freeze({
  // ── One-way notifications. The host acts; the page must not claim it completed. ──
  selection: Object.freeze({
    message: 'devflow:selectionChanged', mode: 'notify', report: false,
    label: 'mirror the selected element',
  }),
  copilot: Object.freeze({
    message: 'devflow:sendToCopilot', mode: 'notify', report: true,
    label: 'send selection to Copilot', fallback: 'clipboard',
  }),
  openSource: Object.freeze({
    message: 'devflow:openSource', mode: 'notify', report: true,
    label: 'open source in the editor', fallback: 'clipboard',
  }),
  saveRecording: Object.freeze({
    message: 'devflow:recordingComplete', mode: 'notify', report: true,
    label: 'save recordings to the workspace', fallback: 'download',
  }),

  // ── Request/response. The host confirms, cancels, or fails. ──
  copilotContext: Object.freeze({
    message: 'devflow:attachCopilot', mode: 'request', timeoutMs: 10000, report: true,
    label: 'attach bounded Inspector context to Copilot', fallback: 'clipboard',
  }),
  attachData: Object.freeze({
    message: 'devflow:attachData', mode: 'request', timeoutMs: 12000, report: true,
    label: 'attach a bounded Data snapshot to Copilot', fallback: 'clipboard',
  }),
  workflowFilePicker: Object.freeze({
    message: 'devflow:pickWorkflow', mode: 'request', timeoutMs: 300000, report: true,
    label: 'pick workflow files', fallback: 'file-input',
  }),
  saveTestBundle: Object.freeze({
    message: 'devflow:saveTestBundle', mode: 'request', timeoutMs: 10000, report: true,
    label: 'save test bundles', fallback: 'download',
  }),
  loadTestBundle: Object.freeze({
    message: 'devflow:loadTestBundle', mode: 'request', timeoutMs: 10000, report: true,
    label: 'load test bundles', fallback: 'file-input',
  }),
  pickTrace: Object.freeze({
    message: 'devflow:pickTrace', mode: 'request', timeoutMs: 60000, report: true,
    label: 'pick trace artifacts', fallback: 'file-input',
  }),
  requestTestProposal: Object.freeze({
    message: 'devflow:requestTestProposal', mode: 'request', timeoutMs: 10000, report: true,
    label: 'send bounded requests to your agent', fallback: 'clipboard',
  }),
  openSourceDiff: Object.freeze({
    message: 'devflow:openSourceDiff', mode: 'request', timeoutMs: 10000, report: true,
    label: 'open a reviewed source diff', fallback: 'in-page',
  }),
  nativeApproval: Object.freeze({
    // Matches the broker's ApprovalRequestLifetime. A human gate cannot be timed out
    // on a shorter budget than the request itself is valid for: the modal stays open,
    // the reviewer's later click arrives against a discarded requestId, and the
    // approval is silently lost. The broker remains the authority on expiry.
    message: 'devflow:nativeApproval', mode: 'request', timeoutMs: 600000, report: true,
    label: 'obtain a human-confirmed native approval', fallback: null,
  }),
});

export const HOST_CAPABILITIES = Object.freeze(Object.keys(HOST_OPERATIONS));

// What the page can still do for itself when a host cannot serve an operation. `download` is
// deliberately absent for embedded hosts: neither the VS Code webview nor the Canvas shell grants
// `allow-downloads` on the iframe, so promising a download there would be a silent no-op.
const FALLBACK_LABELS = Object.freeze({
  clipboard: 'copy it for you instead',
  download: 'download it instead',
  'file-input': 'use its own file picker instead',
  'in-page': 'show it in the Inspector instead',
});

const READY_MESSAGE = 'devflow:ready';
const HOST_MESSAGE = 'devflow:host';
const HOST_RESULT = 'devflow:hostResult';
const THEME_MESSAGE = 'devflow:theme';
const RECORDING_CHANGED_MESSAGE = 'devflow:recordingChanged';
const TEST_PROPOSAL_APPROVAL_RESULT = 'devflow:testProposalApprovalResult';

// Bounded handshake. A framed page that never hears from its host stays 'unavailable' rather than
// silently adopting browser fallbacks it cannot execute inside the sandbox.
const HANDSHAKE_RETRY_MS = 300;
const HANDSHAKE_MAX_TRIES = 12;

export const HOST_BRIDGE_PROTOCOL = Object.freeze({
  currentVersion: 2,
  minimumVersion: 1,
});

function boundedString(value, fallback, maxLength = 128) {
  return typeof value === 'string' && value.length > 0 && value.length <= maxLength
    ? value
    : fallback;
}

function normalizeCapabilityDescriptors(message) {
  const byName = new Map();
  for (const name of Array.isArray(message?.capabilities) ? message.capabilities : []) {
    if (typeof name === 'string' && name.length > 0 && name.length <= 128)
      byName.set(name, Object.freeze({ name, version: 1 }));
  }
  for (const descriptor of Array.isArray(message?.capabilityDescriptors) ? message.capabilityDescriptors : []) {
    const name = boundedString(descriptor?.name, null);
    if (!name) continue;
    const version = Number.isInteger(descriptor.version) && descriptor.version > 0
      ? descriptor.version
      : 1;
    const constraints = {};
    if (descriptor.constraints && typeof descriptor.constraints === 'object') {
      for (const [key, value] of Object.entries(descriptor.constraints).slice(0, 32)) {
        if (!/^[A-Za-z][A-Za-z0-9_.-]{0,63}$/.test(key)) continue;
        if (typeof value === 'boolean' || (typeof value === 'number' && Number.isFinite(value)))
          constraints[key] = value;
        else if (typeof value === 'string' && value.length <= 256)
          constraints[key] = value;
      }
    }
    byName.set(name, Object.freeze({
      name,
      version,
      constraints: Object.keys(constraints).length > 0 ? Object.freeze(constraints) : undefined,
    }));
  }
  return Object.freeze([...byName.values()]);
}

export function normalizeHostManifest(message) {
  if (!message || message.type !== HOST_MESSAGE) return null;
  const protocol = message.protocol && typeof message.protocol === 'object' ? message.protocol : {};
  const minimumVersion = Number.isInteger(protocol.minimumVersion) ? protocol.minimumVersion : 1;
  const maximumVersion = Number.isInteger(protocol.maximumVersion)
    ? protocol.maximumVersion
    : (Number.isInteger(protocol.version) ? protocol.version : 1);
  if (minimumVersion < 1 ||
      maximumVersion < minimumVersion ||
      minimumVersion > HOST_BRIDGE_PROTOCOL.currentVersion ||
      maximumVersion < HOST_BRIDGE_PROTOCOL.minimumVersion) {
    return null;
  }

  const capabilityDescriptors = normalizeCapabilityDescriptors(message);
  const profile = message.profile && typeof message.profile === 'object'
    ? Object.freeze({ ...message.profile })
    : Object.freeze({ surface: 'embedded' });
  return Object.freeze({
    protocolVersion: Math.min(HOST_BRIDGE_PROTOCOL.currentVersion, maximumVersion),
    hostId: boundedString(message.hostId, 'embedded-host'),
    hostLabel: boundedString(message.hostLabel, 'Embedded Inspector host'),
    interactionSessionId: boundedString(message.interactionSessionId, null, 256),
    profile,
    capabilityDescriptors,
    capabilities: Object.freeze(capabilityDescriptors.map((descriptor) => descriptor.name)),
  });
}

// One sentence explaining an operation's availability, honest about what this surface can actually
// do. `canDownload` matters because embedded hosts cannot download at all.
export function describeHostCapability(capabilities, capability, options = {}) {
  const operation = HOST_OPERATIONS[capability];
  const label = operation ? operation.label : capability;
  if (Array.isArray(capabilities) && capabilities.includes(capability))
    return `This host can ${label} through a bounded typed request.`;

  const canDownload = options.canDownload !== false;
  const fallback = operation ? operation.fallback : null;
  if (!fallback)
    return `This host cannot ${label}, and the Inspector has no equivalent it can run here.`;
  if (fallback === 'download' && !canDownload) {
    return `This host cannot ${label}. Downloads are blocked in this embedded surface, so open the Inspector in a browser tab to save it.`;
  }
  return `This host cannot ${label}. The Inspector will ${FALLBACK_LABELS[fallback]}.`;
}

export function createInspectorHostBridge(windowLike = window) {
  const bridgeMatch = String(windowLike.location?.hash || '').match(/devflowBridge=([A-Za-z0-9_-]+)/);
  const bridgeId = bridgeMatch ? bridgeMatch[1] : null;
  const framed = !!(windowLike.parent && windowLike.parent !== windowLike);
  const embedded = framed && !!bridgeId;

  const BROWSER_MANIFEST = Object.freeze({
    protocolVersion: HOST_BRIDGE_PROTOCOL.currentVersion,
    hostId: 'browser',
    hostLabel: 'Standalone browser',
    interactionSessionId: null,
    profile: Object.freeze({ surface: 'browser' }),
    capabilityDescriptors: Object.freeze([]),
    capabilities: Object.freeze([]),
  });

  let manifest = BROWSER_MANIFEST;
  // An embedded page has no capabilities until its host says so. Resolving operations before then
  // would pick fallbacks the sandbox cannot run.
  let handshake = embedded ? 'pending' : 'settled';
  let sequence = 0;
  let handshakeTries = 0;
  let handshakeTimer = null;
  const capabilityListeners = new Set();
  const hostListeners = new Set();
  const readyWaiters = new Set();
  const pending = new Map();

  function emitCapabilities() {
    for (const listener of capabilityListeners) {
      try { listener([...manifest.capabilities]); } catch { /* an observer must never break the bridge */ }
    }
  }

  function emitHost(event) {
    for (const listener of hostListeners) {
      try { listener(event); } catch { /* an observer must never break the bridge */ }
    }
  }

  function settleHandshake() {
    if (handshakeTimer) { windowLike.clearTimeout(handshakeTimer); handshakeTimer = null; }
    handshake = 'settled';
    for (const resolve of readyWaiters) resolve();
    readyWaiters.clear();
  }

  function validMessage(event, message) {
    return !!(embedded &&
      event.source === windowLike.parent &&
      message &&
      message.bridgeId === bridgeId);
  }

  function onMessage(event) {
    const message = event.data;
    if (!validMessage(event, message)) return;

    if (message.type === HOST_MESSAGE) {
      const nextManifest = normalizeHostManifest(message);
      if (!nextManifest) return;
      manifest = nextManifest;
      settleHandshake();
      emitCapabilities();
      emitHost({ type: 'host', manifest, theme: message.theme || null });
      return;
    }
    if (message.type === THEME_MESSAGE) {
      emitHost({ type: 'theme', theme: message, profile: message.profile || null });
      return;
    }
    if (message.type === RECORDING_CHANGED_MESSAGE) {
      emitHost({ type: 'recording' });
      return;
    }
    if ((message.type !== HOST_RESULT && message.type !== TEST_PROPOSAL_APPROVAL_RESULT) ||
        typeof message.requestId !== 'string') return;

    const request = pending.get(message.requestId);
    if (!request) return;
    pending.delete(message.requestId);
    windowLike.clearTimeout(request.timer);
    const approval = message.approval && typeof message.approval === 'object'
      ? {
        state: typeof message.approval.state === 'string' ? message.approval.state : undefined,
        grantId: typeof message.approval.grantId === 'string' ? message.approval.grantId : undefined,
        expiresAt: typeof message.approval.expiresAt === 'string' ? message.approval.expiresAt : undefined,
        reason: typeof message.approval.reason === 'string' ? message.approval.reason : undefined,
      }
      : undefined;
    request.resolve({
      ok: message.ok === true,
      state: message.ok === true
        ? 'completed'
        : (message.cancelled === true ? 'cancelled' : 'failed'),
      message: typeof message.message === 'string' ? message.message : undefined,
      error: typeof message.error === 'string' ? message.error : undefined,
      approval,
      value: message.value ?? (
        typeof message.name === 'string' || typeof message.markdown === 'string' || typeof message.planJson === 'string'
          ? {
            name: typeof message.name === 'string' ? message.name : undefined,
            markdown: typeof message.markdown === 'string' ? message.markdown : undefined,
            planJson: typeof message.planJson === 'string' ? message.planJson : undefined,
          }
          : undefined),
      // Legacy result shapes are flattened onto the result so existing readers keep working.
      ...(message.type === HOST_RESULT || message.type === TEST_PROPOSAL_APPROVAL_RESULT
        ? { name: message.name, markdown: message.markdown, planJson: message.planJson, steps: message.steps }
        : {}),
    });
  }

  windowLike.addEventListener('message', onMessage);

  function post(type, payload) {
    if (!embedded) return false;
    windowLike.parent.postMessage(Object.assign(
      { v: 1, protocolVersion: manifest.protocolVersion, bridgeId, type },
      payload || {},
    ), '*');
    return true;
  }

  // Announce readiness until the host acks. The host also re-announces on iframe load, so either
  // order works.
  function announceReady() {
    if (!embedded || handshake === 'settled') return;
    post(READY_MESSAGE, { version: 1 });
    if (++handshakeTries < HANDSHAKE_MAX_TRIES) {
      handshakeTimer = windowLike.setTimeout(announceReady, HANDSHAKE_RETRY_MS);
    } else {
      // The host never answered. Settle as an embedded host with no capabilities rather than
      // pretending to be a browser tab that can download and use file inputs.
      settleHandshake();
      emitCapabilities();
    }
  }

  // How an operation will behave in this surface, right now.
  function resolve(capability) {
    const operation = HOST_OPERATIONS[capability];
    if (!operation)
      return { state: 'unavailable', reasonCode: 'unknown-operation', message: `Unknown host operation '${capability}'.` };
    if (handshake === 'pending')
      return { state: 'pending' };
    if (manifest.capabilities.includes(capability))
      return { state: 'available', executor: 'host', report: operation.report !== false };

    const fallback = operation.fallback;
    const usable = fallback && (fallback !== 'download' || !embedded);
    if (!usable) {
      return {
        state: 'unavailable',
        reasonCode: fallback === 'download' ? 'downloads-blocked' : 'no-equivalent',
        message: describeHostCapability(manifest.capabilities, capability, { canDownload: !embedded }),
      };
    }
    return {
      state: 'alternative',
      executor: 'page',
      fallback,
      label: FALLBACK_LABELS[fallback],
      report: operation.report !== false,
      reason: describeHostCapability(manifest.capabilities, capability, { canDownload: !embedded }),
    };
  }

  function request(capability, payload = {}, timeoutMs) {
    const operation = HOST_OPERATIONS[capability];
    if (!operation)
      return Promise.resolve({ ok: false, state: 'failed', error: `Unknown host capability '${capability}'.` });
    if (operation.mode !== 'request') {
      return Promise.resolve({
        ok: false,
        state: 'failed',
        error: `Host operation '${capability}' is a one-way notification and cannot be awaited.`,
      });
    }
    if (!embedded || !manifest.capabilities.includes(capability)) {
      return Promise.resolve({
        ok: false,
        state: 'unsupported',
        code: 'capability-missing',
        error: describeHostCapability(manifest.capabilities, capability, { canDownload: !embedded }),
      });
    }

    const requestId = `host-${Date.now().toString(36)}-${(++sequence).toString(36)}`;
    const budget = Number.isFinite(timeoutMs) ? timeoutMs : (operation.timeoutMs || 10000);
    return new Promise((resolveRequest) => {
      const timer = windowLike.setTimeout(() => {
        pending.delete(requestId);
        // The host may still be working. Never let a caller fall back after this, or the side
        // effect can happen twice.
        resolveRequest({
          ok: false,
          state: 'indeterminate',
          reasonCode: 'host-timeout',
          error: `The host did not respond to the ${operation.label} request.`,
        });
      }, budget);
      pending.set(requestId, { resolve: resolveRequest, timer });
      post(operation.message, Object.assign({ requestId }, payload));
    });
  }

  // One-way. Returns whether the message was dispatched, never whether the host completed it.
  function notify(capability, payload = {}) {
    const operation = HOST_OPERATIONS[capability];
    if (!operation || operation.mode !== 'notify') return false;
    if (!embedded || !manifest.capabilities.includes(capability)) return false;
    return post(operation.message, payload);
  }

  return Object.freeze({
    capabilities: () => [...manifest.capabilities],
    capabilityDescriptors: () => manifest.capabilityDescriptors.map((descriptor) => ({ ...descriptor })),
    manifest: () => manifest,
    profile: () => manifest.profile,
    hostId: () => manifest.hostId,
    hostLabel: () => manifest.hostLabel,
    // True only in a standalone browser tab. Embedded iframes are sandboxed without
    // `allow-downloads`, so a download there silently does nothing.
    canDownload: () => !embedded,
    isEmbedded: () => embedded,
    isPending: () => handshake === 'pending',
    has: (capability) => manifest.capabilities.includes(capability),
    resolve,
    request,
    notify,
    start() { announceReady(); },
    whenReady() {
      if (handshake === 'settled') return Promise.resolve();
      return new Promise((resolveReady) => readyWaiters.add(resolveReady));
    },
    onCapabilitiesChanged(listener) {
      if (typeof listener !== 'function') return () => {};
      capabilityListeners.add(listener);
      return () => capabilityListeners.delete(listener);
    },
    onHostMessage(listener) {
      if (typeof listener !== 'function') return () => {};
      hostListeners.add(listener);
      return () => hostListeners.delete(listener);
    },
    dispose() {
      windowLike.removeEventListener('message', onMessage);
      if (handshakeTimer) { windowLike.clearTimeout(handshakeTimer); handshakeTimer = null; }
      for (const request of pending.values()) {
        windowLike.clearTimeout(request.timer);
        request.resolve({ ok: false, state: 'failed', error: 'The Inspector host bridge closed.' });
      }
      pending.clear();
      capabilityListeners.clear();
      hostListeners.clear();
      readyWaiters.clear();
    },
  });
}
