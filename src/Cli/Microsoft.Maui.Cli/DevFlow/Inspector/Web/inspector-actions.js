import {
  CoordinateSpace,
  inspectorCssToAppLogical,
  screenshotPixelToAppLogical,
  windowLogicalToAppLogical,
} from './inspector-frame.js';

const LOCAL_ACTION_ACTIVE_TTL_MS = 10 * 60_000;
const LOCAL_ACTION_TERMINAL_TTL_MS = 30_000;
const MIN_VISIBLE_MS = 900;
const CURSOR_IDLE_MS = 1_200;
const FAILURE_GRACE_MS = 300;
const ANNOUNCEMENT_INTERVAL_MS = 1_000;
const MAX_TARGET_LABEL_LENGTH = 60;
const MAX_ACTIVE_ACTIONS = 64;
const MAX_LOCAL_ACTIONS = 256;
const NON_SPATIAL_KINDS = new Set(['back', 'key', 'navigate', 'resize']);

const SOURCE_LABELS = {
  cli: 'CLI',
  mcp: 'MCP',
  replay: 'Replay',
  inspector: 'Another Inspector',
};

function finite(value) {
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

function actionId() {
  return globalThis.crypto?.randomUUID?.()
    ?? `action-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

function correlationOf(data) {
  return data && typeof data.correlation === 'object' && data.correlation
    ? data.correlation
    : {};
}

function sourceLabel(data, local) {
  if (local) return 'Inspector';
  const source = correlationOf(data).source;
  return Object.hasOwn(SOURCE_LABELS, source) ? SOURCE_LABELS[source] : 'DevFlow';
}

function kindLabel(kind) {
  return String(kind || 'action').replaceAll('-', ' ');
}

function actionVerb(data) {
  switch (data.kind) {
    case 'tap': return 'tapped';
    case 'doubletap': return 'double tapped';
    case 'longpress': return 'long pressed';
    case 'swipe': return data.direction ? `swiped ${data.direction}` : 'swiped';
    case 'drag': return 'dragged';
    case 'pan': return 'panned';
    case 'scroll': return 'scrolled';
    case 'fill': return 'filled';
    case 'clear': return 'cleared';
    case 'focus': return 'focused';
    case 'navigate': return 'navigated';
    case 'back': return 'went back';
    default: return `ran ${kindLabel(data.kind)}`;
  }
}

function actionDescription(data) {
  const kind = kindLabel(data.kind);
  return data.kind === 'swipe' && data.direction
    ? `${kind} ${data.direction}`
    : kind;
}

export function createActionOverlayController({
  viewport,
  overlay,
  chrome = overlay,
  announcer,
  getCurrentFrame,
  hasCurrentFrameIdentity = () => true,
  getElementById,
}) {
  const localActions = new Map();
  const pendingFailures = new Map();
  const activeActions = new Map();
  let cursorHideTimer = null;
  let announceToken = 0;
  let announcementTimer = null;
  let lastAnnouncementAt = 0;
  let renderCount = 0;

  const cursor = document.createElement('div');
  cursor.className = 'df-action-cursor df-hidden';
  cursor.setAttribute('aria-hidden', 'true');
  const cursorLabel = document.createElement('span');
  cursorLabel.className = 'df-action-source';
  cursor.appendChild(cursorLabel);
  overlay.appendChild(cursor);

  function reducedMotion() {
    const reduced = document.documentElement.dataset.reducedMotion === 'true'
      || globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true;
    overlay.dataset.reducedMotion = String(reduced);
    return reduced;
  }

  function rememberLocal(id, ttl = LOCAL_ACTION_ACTIVE_TTL_MS) {
    if (!id) return;
    const previous = localActions.get(id);
    if (previous) clearTimeout(previous);
    const timer = setTimeout(() => localActions.delete(id), ttl);
    localActions.set(id, timer);
    while (localActions.size > MAX_LOCAL_ACTIONS) {
      const oldest = localActions.keys().next().value;
      clearTimeout(localActions.get(oldest));
      localActions.delete(oldest);
    }
  }

  function clearLocalActions() {
    for (const timer of localActions.values()) clearTimeout(timer);
    localActions.clear();
    for (const timer of pendingFailures.values()) clearTimeout(timer);
    pendingFailures.clear();
  }

  function frameSnapshot() {
    const frame = getCurrentFrame?.();
    return frame && typeof frame === 'object' ? frame : null;
  }

  function rememberLocalCorrelation(correlation) {
    rememberLocal(correlation?.actionId);
  }

  function prepareLocalRequest(kind, data = {}, { render = false, correlation = null } = {}) {
    const frame = frameSnapshot();
    const resolvedCorrelation = correlation && typeof correlation === 'object'
      ? correlation
      : {
          actionId: actionId(),
          source: 'inspector',
          frameId: frame?.frameId || undefined,
          coordinateSpace: CoordinateSpace.appLogical,
        };
    const id = resolvedCorrelation.actionId || actionId();
    rememberLocal(id);
    const eventData = {
      ...data,
      kind,
      captureEpoch: frame?.captureEpoch ?? undefined,
      registryGeneration: frame?.registryGeneration ?? undefined,
      windowId: frame?.windowId ?? undefined,
      correlation: { ...resolvedCorrelation, actionId: id },
    };
    if (render) renderStarted(eventData, true);
    return { actionId: id, data: eventData };
  }

  function finishLocalAction(id, success, reason = null, outcome = null) {
    rememberLocal(id, LOCAL_ACTION_TERMINAL_TTL_MS);
    const entry = activeActions.get(id);
    if (!entry) return;
    renderCompleted({
      ...entry.data,
      success,
      outcome: outcome || (success ? 'completed' : 'failed'),
      reason,
    }, true);
  }

  function updateLocalAction(id, data) {
    rememberLocal(id);
    const entry = activeActions.get(id);
    if (!entry) return;
    renderStarted({
      ...entry.data,
      ...data,
      correlation: entry.data.correlation,
    }, true);
  }

  function currentFrameStatus(data, local) {
    if (local) return 'current';
    const frame = frameSnapshot();
    if (!frame) return 'unmapped';
    const correlation = correlationOf(data);
    if (hasCoordinates(data) && !supportsCoordinateSpace(correlation.coordinateSpace))
      return 'unmapped';
    if (correlation.frameId) {
      if (!hasCurrentFrameIdentity()) return 'unmapped';
      if (correlation.frameId !== frame.frameId) return 'stale';
      if (correlation.streamId && correlation.streamId !== frame.streamId) return 'stale';
      if (finite(correlation.frameRevision) !== null
          && finite(correlation.frameRevision) !== finite(frame.frameRevision)) {
        return 'stale';
      }
      return 'current';
    }

    const diagnosticInputsRevision = typeof data.diagnosticInputsRevision === 'string'
      ? data.diagnosticInputsRevision
      : null;
    if (diagnosticInputsRevision) {
      if (frame.diagnosticInputsRevision !== diagnosticInputsRevision)
        return 'stale';
      const generation = finite(data.registryGeneration);
      if (generation !== null && finite(frame.registryGeneration) !== generation)
        return 'stale';
      const windowId = finite(data.windowId);
      if (windowId !== null && finite(frame.windowId) !== windowId)
        return 'stale';
      return 'current';
    }

    const epoch = finite(data.captureEpoch);
    if (epoch === null || finite(frame.captureEpoch) === null)
      return 'unmapped';
    if (epoch !== finite(frame.captureEpoch))
      return 'stale';

    const generation = finite(data.registryGeneration);
    if (generation !== null && finite(frame.registryGeneration) !== generation)
      return 'stale';
    const windowId = finite(data.windowId);
    if (windowId !== null && finite(frame.windowId) !== windowId)
      return 'stale';
    return 'current';
  }

  function hasCoordinates(data) {
    return finite(data.x) !== null
      || finite(data.y) !== null
      || finite(data.toX) !== null
      || finite(data.toY) !== null
      || Array.isArray(data.points);
  }

  function supportsCoordinateSpace(space) {
    return space == null
      || space === CoordinateSpace.appLogical
      || space === CoordinateSpace.windowLogical
      || space === CoordinateSpace.screenshotPixel
      || space === CoordinateSpace.inspectorCss;
  }

  function pointToAppLogical(point, correlation, frame) {
    const raw = { x: finite(point?.x), y: finite(point?.y) };
    if (raw.x === null || raw.y === null) return null;
    switch (correlation.coordinateSpace) {
      case CoordinateSpace.windowLogical:
        return windowLogicalToAppLogical(raw, frame);
      case CoordinateSpace.screenshotPixel:
        return screenshotPixelToAppLogical(raw, frame);
      case CoordinateSpace.inspectorCss:
        return inspectorCssToAppLogical(raw);
      case CoordinateSpace.appLogical:
      case undefined:
      case null:
        return raw;
      default:
        return null;
    }
  }

  function elementFor(data, target = false) {
    const id = target ? data.toElementId : data.elementId;
    return id ? getElementById?.(id) : null;
  }

  function elementPoint(element, x, y) {
    if (!element) return null;
    return {
      x: element.offsetLeft + (x ?? element.offsetWidth / 2),
      y: element.offsetTop + (y ?? element.offsetHeight / 2),
    };
  }

  function viewportCenter() {
    const width = finite(viewport.dataset.width) ?? viewport.offsetWidth;
    const height = finite(viewport.dataset.height) ?? viewport.offsetHeight;
    return { x: width / 2, y: height / 2 };
  }

  function resolveStart(data) {
    const correlation = correlationOf(data);
    const frame = frameSnapshot();
    if (Array.isArray(data.points) && data.points.length) {
      const point = pointToAppLogical(data.points[0], correlation, frame);
      if (point) return point;
    }

    const x = finite(data.x);
    const y = finite(data.y);
    if (correlation.coordinateSpace != null) {
      const correlatedPoint = pointToAppLogical({ x, y }, correlation, frame);
      if (correlatedPoint) return correlatedPoint;
    }

    const element = elementFor(data);
    if (element) {
      const originX = finite(data.originX);
      const originY = finite(data.originY);
      return elementPoint(
        element,
        x ?? (originX === null ? null : element.offsetWidth * originX),
        y ?? (originY === null ? null : element.offsetHeight * originY));
    }
    if (x !== null && y !== null) return { x, y };
    return viewportCenter();
  }

  function directionDelta(direction, distance) {
    const amount = finite(distance) ?? 120;
    switch (direction) {
      case 'up': return { x: 0, y: -amount };
      case 'down': return { x: 0, y: amount };
      case 'left': return { x: -amount, y: 0 };
      case 'right': return { x: amount, y: 0 };
      default: return null;
    }
  }

  function resolvePath(data, start) {
    const correlation = correlationOf(data);
    const frame = frameSnapshot();
    if (Array.isArray(data.points) && data.points.length >= 2) {
      return data.points
        .map(point => pointToAppLogical(point, correlation, frame))
        .filter(Boolean);
    }

    const toX = finite(data.toX);
    const toY = finite(data.toY);
    if (toX !== null && toY !== null) {
      if (correlation.coordinateSpace != null) {
        const correlatedEnd = pointToAppLogical(
          { x: toX, y: toY },
          correlation,
          frame);
        if (correlatedEnd) return [start, correlatedEnd];
      }
      const target = elementFor(data, true) || elementFor(data);
      const end = elementPoint(target, toX, toY);
      if (end) return [start, end];
    }

    const deltaX = finite(data.deltaX);
    const deltaY = finite(data.deltaY);
    if (deltaX !== null || deltaY !== null) {
      return [
        start,
        { x: start.x + (deltaX ?? 0), y: start.y + (deltaY ?? 0) },
      ];
    }

    const delta = directionDelta(data.direction, data.distance);
    return delta
      ? [start, { x: start.x + delta.x, y: start.y + delta.y }]
      : null;
  }

  function targetLabel(data) {
    const element = elementFor(data);
    if (!element) return '';
    const sensitive = element.dataset.sensitive === 'true';
    const label = element.dataset.automationid
      || element.dataset.automationId
      || (!sensitive ? element.dataset.text : null)
      || element.dataset.type
      || '';
    const normalized = String(label).replace(/\s+/g, ' ').trim();
    const bounded = normalized.length > MAX_TARGET_LABEL_LENGTH
      ? `${normalized.slice(0, MAX_TARGET_LABEL_LENGTH - 3)}...`
      : normalized;
    return bounded ? ` ${bounded}` : '';
  }

  function announce(message) {
    if (!announcer || !message) return;
    const token = ++announceToken;
    if (announcementTimer) clearTimeout(announcementTimer);
    const publish = () => {
      announcementTimer = null;
      if (token === announceToken) announcer.textContent = message;
      lastAnnouncementAt = Date.now();
    };
    announcer.textContent = '';
    const delay = Math.max(
      0,
      ANNOUNCEMENT_INTERVAL_MS - (Date.now() - lastAnnouncementAt));
    announcementTimer = setTimeout(publish, delay);
  }

  function moveCursor(point, data, local) {
    if (cursorHideTimer) {
      clearTimeout(cursorHideTimer);
      cursorHideTimer = null;
    }
    cursor.classList.remove('df-hidden', 'df-action-failed');
    cursor.dataset.actionId = correlationOf(data).actionId || '';
    cursor.dataset.x = String(Math.round(point.x * 100) / 100);
    cursor.dataset.y = String(Math.round(point.y * 100) / 100);
    cursor.style.left = `${point.x}px`;
    cursor.style.top = `${point.y}px`;
    cursorLabel.textContent = sourceLabel(data, local);
  }

  function renderRipple(point, data) {
    const ripple = document.createElement('span');
    ripple.className = `df-action-ripple${data.kind === 'longpress' ? ' df-action-longpress' : ''}`;
    ripple.dataset.actionId = correlationOf(data).actionId || '';
    ripple.style.left = `${point.x}px`;
    ripple.style.top = `${point.y}px`;
    overlay.appendChild(ripple);
    return ripple;
  }

  function renderPath(points, data) {
    if (!points || points.length < 2) return null;
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.classList.add('df-action-path');
    svg.dataset.actionId = correlationOf(data).actionId || '';
    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute(
      'd',
      points.map((point, index) => `${index ? 'L' : 'M'} ${point.x} ${point.y}`).join(' '));
    svg.appendChild(path);
    overlay.appendChild(svg);
    return svg;
  }

  function renderBadge(data, state) {
    const badge = document.createElement('span');
    badge.className = `df-action-badge df-action-${state}`;
    badge.dataset.actionId = correlationOf(data).actionId || '';
    badge.textContent = `${sourceLabel(data, false)} · ${kindLabel(data.kind)}${
      state === 'stale' || state === 'unmapped'
        ? ' · frame unavailable'
        : state === 'failed' ? ' · failed' : ''
    }`;
    chrome.appendChild(badge);
    return badge;
  }

  function scheduleCleanup(entry, delay = MIN_VISIBLE_MS) {
    if (entry.cleanupTimer) clearTimeout(entry.cleanupTimer);
    const elapsed = Date.now() - entry.startedAt;
    entry.cleanupTimer = setTimeout(
      () => cleanupEntry(entry),
      Math.max(0, delay - elapsed));
  }

  function cleanupEntry(entry) {
    if (entry.cleanupTimer) clearTimeout(entry.cleanupTimer);
    for (const node of entry.nodes) node.remove();
    activeActions.delete(entry.id);
    if (activeActions.size === 0) {
      cursorHideTimer = setTimeout(() => {
        cursor.classList.add('df-hidden');
        cursor.removeAttribute('data-action-id');
      }, CURSOR_IDLE_MS);
    }
  }

  function renderStarted(data, local = false) {
    const correlation = correlationOf(data);
    const id = correlation.actionId || actionId();
    const normalized = { ...data, correlation: { ...correlation, actionId: id } };
    const pendingFailure = pendingFailures.get(id);
    if (pendingFailure) {
      clearTimeout(pendingFailure);
      pendingFailures.delete(id);
    }
    const existing = activeActions.get(id);
    const retrying = !!existing;
    if (existing) cleanupEntry(existing);
    while (activeActions.size >= MAX_ACTIVE_ACTIONS) {
      cleanupEntry(activeActions.values().next().value);
    }

    const frameStatus = currentFrameStatus(normalized, local);
    const entry = {
      id,
      data: normalized,
      local,
      pointerSession: existing?.pointerSession || normalized.kind === 'pointer',
      startedAt: Date.now(),
      nodes: [],
      cleanupTimer: null,
    };
    activeActions.set(id, entry);
    if (!retrying) overlay.dataset.renderCount = String(++renderCount);
    overlay.dataset.lastActionId = id;
    reducedMotion();

    if (NON_SPATIAL_KINDS.has(normalized.kind)) {
      entry.nodes.push(renderBadge(normalized, 'info'));
      if (!local && !retrying) {
        announce(`${sourceLabel(normalized, false)} ${actionVerb(normalized)}.`);
      }
      scheduleCleanup(entry, 1_500);
      return entry;
    }

    if (frameStatus !== 'current') {
      entry.nodes.push(renderBadge(normalized, frameStatus));
      if (!local && !retrying && normalized.kind !== 'pointer') {
        announce(
          `${sourceLabel(normalized, false)} ${actionDescription(normalized)} could not be placed on this Preview.`);
      }
      scheduleCleanup(entry, 1_500);
      return entry;
    }

    const start = resolveStart(normalized);
    moveCursor(start, normalized, local);
    if (['tap', 'doubletap', 'longpress'].includes(normalized.kind)) {
      entry.nodes.push(renderRipple(start, normalized));
    } else if (['swipe', 'pan', 'drag', 'scroll'].includes(normalized.kind)) {
      const path = renderPath(resolvePath(normalized, start), normalized);
      if (path) entry.nodes.push(path);
    }

    if (!local && !retrying && normalized.kind !== 'pointer') {
      announce(
        `${sourceLabel(normalized, false)} started ${actionDescription(normalized)}${targetLabel(normalized)}.`);
    }
    // A wedged action or lost terminal event must not leave the Preview permanently pressed.
    scheduleCleanup(entry, 10_000);
    return entry;
  }

  function renderCompleted(data, local = false) {
    const id = correlationOf(data).actionId;
    const pendingFailure = id ? pendingFailures.get(id) : null;
    if (pendingFailure) {
      clearTimeout(pendingFailure);
      pendingFailures.delete(id);
    }
    const entry = id ? activeActions.get(id) : null;
    if (data.success === false && local) {
      if (entry) markFailedEntry(entry, data, false);
      return;
    }
    if (data.success === false && !local) {
      const timer = setTimeout(() => {
        pendingFailures.delete(id);
        renderFailedCompletion(data, entry);
      }, FAILURE_GRACE_MS);
      if (id) pendingFailures.set(id, timer);
      return;
    }

    if (!entry) {
      return;
    }

    entry.data = { ...entry.data, ...data };
    if (!local && entry.pointerSession) {
      announce(`${sourceLabel(entry.data, false)} ${actionVerb(entry.data)}${targetLabel(entry.data)}.`);
    }
    scheduleCleanup(entry);
  }

  function renderFailedCompletion(data, existingEntry) {
    const id = correlationOf(data).actionId || actionId();
    const entry = existingEntry && activeActions.get(id) === existingEntry
      ? existingEntry
      : null;
    if (!entry) {
      const failure = {
        ...data,
        correlation: { ...correlationOf(data), actionId: id },
      };
      const fallback = {
        id,
        data: failure,
        local: false,
        startedAt: Date.now(),
        nodes: [renderBadge(failure, 'failed')],
        cleanupTimer: null,
      };
      while (activeActions.size >= MAX_ACTIVE_ACTIONS) {
        cleanupEntry(activeActions.values().next().value);
      }
      activeActions.set(id, fallback);
      const outcome = failure.outcome === 'cancelled' ? 'cancelled' : 'failed';
      announce(`${sourceLabel(failure, false)} ${actionDescription(failure)} ${outcome}.`);
      scheduleCleanup(fallback, 1_500);
      return;
    }

    markFailedEntry(entry, data, true);
  }

  function markFailedEntry(entry, data, announceFailure) {
    entry.data = { ...entry.data, ...data };
    if (cursor.dataset.actionId === entry.id) {
      cursor.classList.add('df-action-failed');
      cursorLabel.textContent = `${sourceLabel(entry.data, entry.local)} · failed`;
    }
    for (const node of entry.nodes) node.classList.add('df-action-failed');
    if (announceFailure) {
      const reasonText = data.reason
        ? String(data.reason).replaceAll('-', ' ').slice(0, 80)
        : '';
      const reason = reasonText ? ` (${reasonText})` : '';
      const outcome = data.outcome === 'cancelled' ? 'cancelled' : 'failed';
      announce(
        `${sourceLabel(entry.data, false)} ${actionDescription(entry.data)} ${outcome}${reason}.`);
    }
    scheduleCleanup(entry, 1_500);
  }

  function handleEvent(message) {
    if (!message || typeof message !== 'object') return false;
    if (message.type === 'navigation') {
      for (const entry of activeActions.values()) scheduleCleanup(entry);
      return false;
    }
    if (message.type === 'lifecycle') {
      const state = message.data?.state;
      if (state === 'stopped' || state === 'paused' || state === 'backgrounded')
        clear({ clearLocal: false });
      return false;
    }
    if (message.type !== 'ui-action-started'
        && message.type !== 'ui-action-updated'
        && message.type !== 'ui-action-completed')
      return false;

    const data = message.data;
    if (!data || typeof data !== 'object') return true;
    const id = correlationOf(data).actionId;
    if (id && localActions.has(id)) {
      rememberLocal(
        id,
        message.type === 'ui-action-completed'
          ? LOCAL_ACTION_TERMINAL_TTL_MS
          : LOCAL_ACTION_ACTIVE_TTL_MS);
      return true;
    }

    if (message.type === 'ui-action-started' || message.type === 'ui-action-updated')
      renderStarted(data, false);
    else
      renderCompleted(data, false);
    return true;
  }

  function clear({ clearLocal = false } = {}) {
    if (announcementTimer) clearTimeout(announcementTimer);
    announcementTimer = null;
    for (const timer of pendingFailures.values()) clearTimeout(timer);
    pendingFailures.clear();
    for (const entry of [...activeActions.values()]) cleanupEntry(entry);
    cursor.classList.add('df-hidden');
    cursor.classList.remove('df-action-failed');
    cursor.removeAttribute('data-action-id');
    if (cursorHideTimer) clearTimeout(cursorHideTimer);
    cursorHideTimer = null;
    if (clearLocal) clearLocalActions();
  }

  return {
    rememberLocalCorrelation,
    prepareLocalRequest,
    updateLocalAction,
    finishLocalAction,
    handleEvent,
    clear,
  };
}
