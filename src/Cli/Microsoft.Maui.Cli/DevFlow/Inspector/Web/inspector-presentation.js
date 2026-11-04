const BROKER_STATES = new Set(['unknown', 'reachable', 'unreachable', 'browser-offline']);
const AGENT_STATES = new Set(['unknown', 'available', 'unavailable', 'unregistered']);
const CAPTURE_STATES = new Set(['never', 'ok', 'failed']);
const STREAM_STATES = new Set(['unknown', 'connecting', 'live', 'reconnecting', 'unsupported', 'unavailable']);

function enumValue(value, allowed, fallback) {
  return allowed.has(value) ? value : fallback;
}

function boundedText(value, maxLength = 80) {
  if (typeof value !== 'string') return null;
  const text = value.trim().replace(/\s+/g, ' ');
  return text ? text.slice(0, maxLength) : null;
}

function positiveNumber(value) {
  const number = Number(value);
  return Number.isFinite(number) && number > 0 ? number : null;
}

export function createInspectorPresentationState(initial = {}) {
  return updateInspectorPresentationState({
    broker: 'unknown',
    agent: 'unknown',
    capture: {
      status: 'never',
      reason: null,
      hasFrame: false,
      captureScale: null,
      nativeScale: null,
      requestedCaptureScale: null,
      resolutionClamped: null,
    },
    stream: 'unknown',
    retrying: false,
  }, initial);
}

export function updateInspectorPresentationState(current, patch = {}) {
  const state = current || createInspectorPresentationState();
  const capture = patch.capture || {};
  return {
    ...state,
    ...patch,
    broker: enumValue(patch.broker ?? state.broker, BROKER_STATES, 'unknown'),
    agent: enumValue(patch.agent ?? state.agent, AGENT_STATES, 'unknown'),
    capture: {
      ...state.capture,
      ...capture,
      status: enumValue(capture.status ?? state.capture.status, CAPTURE_STATES, 'never'),
      reason: Object.prototype.hasOwnProperty.call(capture, 'reason')
        ? boundedText(capture.reason, 48)
        : state.capture.reason,
      hasFrame: Object.prototype.hasOwnProperty.call(capture, 'hasFrame')
        ? capture.hasFrame === true
        : state.capture.hasFrame,
      captureScale: Object.prototype.hasOwnProperty.call(capture, 'captureScale')
        ? positiveNumber(capture.captureScale)
        : state.capture.captureScale,
      nativeScale: Object.prototype.hasOwnProperty.call(capture, 'nativeScale')
        ? positiveNumber(capture.nativeScale)
        : state.capture.nativeScale,
      requestedCaptureScale: Object.prototype.hasOwnProperty.call(capture, 'requestedCaptureScale')
        ? positiveNumber(capture.requestedCaptureScale)
        : state.capture.requestedCaptureScale,
      resolutionClamped: Object.prototype.hasOwnProperty.call(capture, 'resolutionClamped')
        ? (typeof capture.resolutionClamped === 'boolean' ? capture.resolutionClamped : null)
        : state.capture.resolutionClamped,
    },
    stream: enumValue(patch.stream ?? state.stream, STREAM_STATES, 'unknown'),
    retrying: Object.prototype.hasOwnProperty.call(patch, 'retrying')
      ? patch.retrying === true
      : state.retrying,
  };
}

function disconnectedDetail(state) {
  if (state.broker === 'browser-offline')
    return 'The browser is offline. Showing the last captured frame.';
  if (state.broker === 'unreachable')
    return 'Could not reach the DevFlow broker. Showing the last captured frame.';
  if (state.agent === 'unregistered')
    return 'The selected app is no longer registered. Showing the last captured frame.';
  return 'Showing the last captured frame while DevFlow reconnects.';
}

function captureDetail(reason, hasFrame) {
  const detail = reason === 'capture-timeout'
    ? 'Capture timed out.'
    : reason === 'image-decode' || reason === 'image-load'
      ? 'The latest screenshot could not be decoded.'
      : 'The latest capture failed.';
  return hasFrame ? `${detail} Showing the last captured frame.` : `${detail} Retry the preview.`;
}

function formatScale(value) {
  if (!value) return null;
  return Number(value.toFixed(2)).toString();
}

function resolutionBadge(capture) {
  if (capture.resolutionClamped !== true) return null;
  const detail = [
    capture.captureScale && `Captured at ${formatScale(capture.captureScale)}×`,
    capture.requestedCaptureScale && `requested ${formatScale(capture.requestedCaptureScale)}×`,
    capture.nativeScale && `native ${formatScale(capture.nativeScale)}×`,
  ].filter(Boolean).join('; ');
  return {
    id: 'resolution',
    tone: 'warning',
    label: 'Resolution limited',
    title: detail || 'The screenshot resolution was limited by the target.',
  };
}

function streamBadge(stream) {
  if (stream === 'unsupported') return { id: 'events', tone: 'neutral', label: 'Polling' };
  if (stream === 'reconnecting' || stream === 'unavailable')
    return { id: 'events', tone: 'warning', label: 'Polling' };
  if (stream === 'connecting') return { id: 'events', tone: 'neutral', label: 'Events connecting' };
  return null;
}

function presencePresentation(access, connected, driveAllowed) {
  if (!connected) {
    return {
      icon: 'i-refresh',
      label: 'Disconnected',
      tone: 'disconnected',
      title: 'The running app is disconnected.',
    };
  }
  if (access.isWriter) {
    let title = driveAllowed
      ? 'This Inspector can drive the app.'
      : 'This Inspector holds control, but interaction waits for a current Preview.';
    if (access.authority === 'unsupported') {
      title += ' The connected legacy agent does not enforce mutation leases.';
    } else if (access.authority === 'unknown') {
      title = 'This Inspector last held control, but the current lease status is temporarily unavailable.';
    }
    return { icon: 'i-edit', label: 'Driving', tone: 'writer', title };
  }
  if (access.heldByOther) {
    const owner = boundedText(access.holderLabel) || 'Another session';
    const seconds = Number.isFinite(Number(access.expiresInMs))
      ? Math.max(0, Math.ceil(Number(access.expiresInMs) / 1000))
      : null;
    return {
      icon: 'i-lock',
      label: `Read-only · ${owner}`,
      tone: 'read-only',
      title: `${owner} is driving this app.${seconds == null ? '' : ` Lease refreshes in about ${seconds}s.`}`,
    };
  }
  return null;
}

export function deriveInspectorPresentation(state, access = {}) {
  const disconnected = state.broker === 'browser-offline'
    || state.broker === 'unreachable'
    || state.agent === 'unavailable'
    || state.agent === 'unregistered';
  const connected = !disconnected && state.broker === 'reachable' && state.agent === 'available';
  const captureFailed = state.capture.status === 'failed';
  const captureUnavailable = connected && captureFailed && !state.capture.hasFrame;
  const captureDelayed = connected && captureFailed && state.capture.hasFrame;
  const connecting = !disconnected && !captureFailed
    && state.capture.status === 'never' && !state.capture.hasFrame;

  let baseKind = 'live';
  let tone = 'success';
  let label = 'Live';
  let detail = 'The Preview is synchronized with the running app.';
  if (disconnected) {
    baseKind = 'disconnected';
    tone = 'error';
    label = 'App disconnected';
    detail = disconnectedDetail(state);
  } else if (captureUnavailable) {
    baseKind = 'capture-unavailable';
    tone = 'error';
    label = 'Capture unavailable';
    detail = captureDetail(state.capture.reason, false);
  } else if (captureDelayed) {
    baseKind = 'capture-delayed';
    tone = 'warning';
    label = 'Preview delayed';
    detail = captureDetail(state.capture.reason, true);
  } else if (connecting) {
    baseKind = 'connecting';
    tone = 'neutral';
    label = 'Connecting';
    detail = 'Waiting for the first Preview capture.';
  }

  const driveAllowed = connected && state.capture.status === 'ok' && access.isWriter === true;
  const controlAllowed = connected && access.isWriter === true;
  const blocking = disconnected || captureUnavailable;
  const badges = [];
  if (!blocking) {
    const limited = resolutionBadge(state.capture);
    if (limited) badges.push(limited);
    const events = streamBadge(state.stream);
    if (events) badges.push(events);
  }

  const retryLabel = baseKind === 'disconnected' ? 'Retry now' : 'Retry capture';
  const healthAnnouncement = state.retrying
    ? (baseKind === 'disconnected'
      ? 'Checking for the running app.'
      : 'Retrying the Preview capture.')
    : baseKind === 'disconnected'
      ? `App disconnected. ${detail}`
      : baseKind === 'capture-delayed'
        ? `Preview delayed. ${detail}`
        : baseKind === 'capture-unavailable'
          ? `Capture unavailable. ${detail}`
          : baseKind === 'connecting'
            ? 'Connecting to the running app.'
            : 'Preview live.';

  const statusText = state.retrying
    ? (baseKind === 'disconnected'
      ? 'Checking for the running app…'
      : 'Retrying the Preview capture…')
    : baseKind === 'disconnected'
      ? 'App disconnected — waiting for DevFlow to reconnect.'
      : '';

  let driveMessage = null;
  if (!connected)
    driveMessage = 'App disconnected — waiting for DevFlow to reconnect.';
  else if (state.capture.status !== 'ok')
    driveMessage = 'Preview delayed — retry capture before interacting.';
  else if (!access.isWriter) {
    const owner = boundedText(access.holderLabel) || 'another session';
    driveMessage = access.heldByOther
      ? `Read-only — ${owner} is driving. Take control to interact.`
      : 'Read-only — take control to interact.';
  }

  return {
    kind: state.retrying ? 'retrying' : baseKind,
    baseKind,
    tone,
    label: state.retrying ? 'Retrying' : label,
    detail,
    primaryAction: baseKind === 'live' || baseKind === 'connecting'
      ? null
      : { id: 'retry', label: retryLabel },
    badges,
    ariaAnnouncement: healthAnnouncement,
    announcementKey: `${baseKind}:${state.retrying ? 'retrying' : 'settled'}`,
    statusText,
    connected,
    healthy: connected && state.capture.status === 'ok',
    driveAllowed,
    controlAllowed,
    driveMessage,
    showBlockingOverlay: blocking,
    showHealthBanner: captureDelayed,
    busy: state.retrying,
    presence: presencePresentation(access, connected, driveAllowed),
  };
}

export function createStatusChannel(write) {
  let sequence = 0;
  let feature = null;
  let health = '';
  let renderedText = '';

  const commit = () => {
    const next = feature?.text || health;
    if (next === renderedText) return false;
    renderedText = next;
    write(next);
    return true;
  };

  return {
    set(text) {
      const value = typeof text === 'string' ? text : String(text || '');
      if (!value) {
        feature = null;
        commit();
        return null;
      }
      if (feature?.text === value) return feature.token;
      feature = { text: value, token: ++sequence };
      commit();
      return feature.token;
    },
    setHealth(text) {
      health = typeof text === 'string' ? text : String(text || '');
      commit();
    },
    clear(token) {
      if (!feature || feature.token !== token) return false;
      feature = null;
      commit();
      return true;
    },
    replace(token, text) {
      if (!feature || feature.token !== token) return false;
      const value = typeof text === 'string' ? text : String(text || '');
      if (!value) return this.clear(token);
      feature.text = value;
      commit();
      return true;
    },
    isCurrent(token) {
      return feature?.token === token && renderedText === feature.text;
    },
  };
}

export function createAnnouncementGate(write) {
  let lastKey = null;
  return {
    announce(key, text) {
      if (!key || !text || key === lastKey) return false;
      lastKey = key;
      write(text);
      return true;
    },
  };
}
