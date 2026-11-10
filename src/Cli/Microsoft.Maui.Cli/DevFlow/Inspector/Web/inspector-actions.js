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
const ANNOUNCEMENT_PRIORITY_SUCCESS = 0;
const ANNOUNCEMENT_PRIORITY_CANCELLED = 1;
const ANNOUNCEMENT_PRIORITY_FAILED = 2;

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

function sourceLabel(data) {
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
  let pendingAnnouncementPriority = ANNOUNCEMENT_PRIORITY_SUCCESS;
  let lastAnnouncementAt = 0;

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
    localActions.set(id, Date.now() + ttl);
    while (localActions.size > MAX_LOCAL_ACTIONS) {
      const oldest = localActions.keys().next().value;
      localActions.delete(oldest);
    }
  }

  function hasLocalAction(id) {
    const expiresAt = localActions.get(id);
    if (expiresAt === undefined) return false;
    if (expiresAt > Date.now()) return true;
    localActions.delete(id);
    return false;
  }

  function clearLocalActions() {
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

  function currentFrameStatus(data) {
    const frame = frameSnapshot();
    if (!frame) return 'unmapped';
    const correlation = correlationOf(data);
    if (hasCoordinates(data) && !supportsCoordinateSpace(correlation.coordinateSpace))
      return 'unmapped';
    if (missingElementTarget(data, false) || missingElementTarget(data, true))
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
      || finite(data.toY) !== null;
  }

  function requiresPlacement(data) {
    if (NON_SPATIAL_KINDS.has(data.kind)) return false;
    return hasCoordinates(data)
      || !!data.elementId
      || !!data.toElementId
      || finite(data.deltaX) !== null
      || finite(data.deltaY) !== null
      || typeof data.direction === 'string';
  }

  function missingElementTarget(data, target) {
    const id = target ? data.toElementId : data.elementId;
    if (!id || elementFor(data, target)) return false;
    const correlation = correlationOf(data);
    if (correlation.coordinateSpace == null) return true;
    const x = finite(target ? data.toX : data.x);
    const y = finite(target ? data.toY : data.y);
    return x === null || y === null;
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
    const target = elementFor(data, true);
    if (target) return [start, elementPoint(target, null, null)];

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
    const label = element.dataset.automationid
      || element.dataset.type
      || '';
    const normalized = String(label).replace(/\s+/g, ' ').trim();
    const bounded = normalized.length > MAX_TARGET_LABEL_LENGTH
      ? `${normalized.slice(0, MAX_TARGET_LABEL_LENGTH - 3)}...`
      : normalized;
    return bounded ? ` ${bounded}` : '';
  }

  function announce(message, priority = ANNOUNCEMENT_PRIORITY_SUCCESS) {
    if (!announcer || !message) return;
    if (announcementTimer && pendingAnnouncementPriority > priority) return;
    const token = ++announceToken;
    if (announcementTimer) clearTimeout(announcementTimer);
    pendingAnnouncementPriority = priority;
    const publish = () => {
      announcementTimer = null;
      pendingAnnouncementPriority = ANNOUNCEMENT_PRIORITY_SUCCESS;
      if (token === announceToken) announcer.textContent = message;
      lastAnnouncementAt = Date.now();
    };
    announcer.textContent = '';
    const delay = Math.max(
      0,
      ANNOUNCEMENT_INTERVAL_MS - (Date.now() - lastAnnouncementAt));
    announcementTimer = setTimeout(publish, delay);
  }

  function moveCursor(point, data) {
    if (cursorHideTimer) {
      clearTimeout(cursorHideTimer);
      cursorHideTimer = null;
    }
    cursor.classList.remove('df-hidden', 'df-action-failed', 'df-action-cancelled');
    cursor.dataset.actionId = correlationOf(data).actionId || '';
    cursor.dataset.x = String(Math.round(point.x * 100) / 100);
    cursor.dataset.y = String(Math.round(point.y * 100) / 100);
    cursor.style.left = `${point.x}px`;
    cursor.style.top = `${point.y}px`;
    cursorLabel.textContent = sourceLabel(data);
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
    if (points.every(point =>
      point.x === points[0].x && point.y === points[0].y)) {
      return null;
    }
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
    badge.dataset.state = state;
    badge.textContent = badgeText(data, state);
    chrome.appendChild(badge);
    return badge;
  }

  function badgeText(data, state, outcome = null) {
    const parts = [sourceLabel(data), kindLabel(data.kind)];
    if (state === 'stale' || state === 'unmapped') parts.push('frame unavailable');
    if (outcome || state === 'failed' || state === 'cancelled')
      parts.push(outcome || state);
    return parts.join(' · ');
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

  function renderPlacedGeometry(entry) {
    for (const node of entry.nodes) node.remove();
    entry.nodes = [];
    const data = entry.data;
    const start = resolveStart(data);
    moveCursor(start, data);
    if (['tap', 'doubletap', 'longpress'].includes(data.kind)) {
      entry.nodes.push(renderRipple(start, data));
    } else if (['pointer', 'swipe', 'pan', 'drag', 'scroll'].includes(data.kind)) {
      const path = renderPath(resolvePath(data, start), data);
      if (path) entry.nodes.push(path);
    }
  }

  function renderStarted(data) {
    const correlation = correlationOf(data);
    const id = correlation.actionId || actionId();
    const normalized = { ...data, correlation: { ...correlation, actionId: id } };
    const pendingFailure = pendingFailures.get(id);
    if (pendingFailure) {
      clearTimeout(pendingFailure);
      pendingFailures.delete(id);
    }
    const existing = activeActions.get(id);
    if (existing) cleanupEntry(existing);
    while (activeActions.size >= MAX_ACTIVE_ACTIONS) {
      cleanupEntry(activeActions.values().next().value);
    }

    const placementRequired = requiresPlacement(normalized);
    const frameStatus = placementRequired ? currentFrameStatus(normalized) : 'not-applicable';
    const entry = {
      id,
      data: normalized,
      continuousPointer: existing?.continuousPointer || normalized.kind === 'pointer',
      placementRequired,
      frameStatus,
      startedAt: Date.now(),
      nodes: [],
      cleanupTimer: null,
    };
    activeActions.set(id, entry);
    reducedMotion();

    if (!placementRequired) {
      entry.nodes.push(renderBadge(normalized, 'info'));
      scheduleCleanup(entry, 1_500);
      return entry;
    }

    if (frameStatus !== 'current') {
      entry.nodes.push(renderBadge(normalized, frameStatus));
      scheduleCleanup(entry, 1_500);
      return entry;
    }

    renderPlacedGeometry(entry);

    // A wedged action or lost terminal event must not leave the Preview permanently pressed.
    scheduleCleanup(entry, 10_000);
    return entry;
  }

  function renderCompleted(data) {
    const id = correlationOf(data).actionId;
    const pendingFailure = id ? pendingFailures.get(id) : null;
    if (pendingFailure) {
      clearTimeout(pendingFailure);
      pendingFailures.delete(id);
    }
    let entry = id ? activeActions.get(id) : null;
    if (data.success === false) {
      const timer = setTimeout(() => {
        pendingFailures.delete(id);
        renderFailedCompletion(data, entry);
      }, FAILURE_GRACE_MS);
      if (id) pendingFailures.set(id, timer);
      return;
    }

    entry ??= renderStarted(data);

    entry.data = { ...entry.data, ...data };
    entry.startedAt = Date.now();
    if (entry.continuousPointer
        && entry.placementRequired
        && entry.frameStatus === 'current') {
      renderPlacedGeometry(entry);
    }
    if (!entry.placementRequired || entry.frameStatus === 'current') {
      announce(`${sourceLabel(entry.data)} ${actionVerb(entry.data)}${targetLabel(entry.data)}.`);
    } else {
      announce(
        `${sourceLabel(entry.data)} ${actionDescription(entry.data)} completed; Preview position unavailable.`);
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
        startedAt: Date.now(),
        nodes: [renderBadge(failure, failure.outcome === 'cancelled' ? 'cancelled' : 'failed')],
        cleanupTimer: null,
      };
      while (activeActions.size >= MAX_ACTIVE_ACTIONS) {
        cleanupEntry(activeActions.values().next().value);
      }
      activeActions.set(id, fallback);
      const outcome = failure.outcome === 'cancelled' ? 'cancelled' : 'failed';
      announce(
        `${sourceLabel(failure)} ${actionDescription(failure)} ${outcome}.`,
        outcome === 'cancelled'
          ? ANNOUNCEMENT_PRIORITY_CANCELLED
          : ANNOUNCEMENT_PRIORITY_FAILED);
      scheduleCleanup(fallback, 1_500);
      return;
    }

    markTerminalEntry(entry, data, true);
  }

  function markTerminalEntry(entry, data, announceFailure) {
    entry.data = { ...entry.data, ...data };
    entry.startedAt = Date.now();
    const outcome = data.outcome === 'cancelled' ? 'cancelled' : 'failed';
    const outcomeClass = outcome === 'cancelled'
      ? 'df-action-cancelled'
      : 'df-action-failed';
    if (cursor.dataset.actionId === entry.id) {
      cursor.classList.add(outcomeClass);
      cursorLabel.textContent = `${sourceLabel(entry.data)} · ${outcome}`;
    }
    for (const node of entry.nodes) {
      node.classList.add(outcomeClass);
      if (node.classList.contains('df-action-badge')) {
        node.textContent = badgeText(
          entry.data,
          node.dataset.state,
          outcome);
      }
    }
    if (announceFailure) {
      const reasonText = data.reason
        ? String(data.reason).replaceAll('-', ' ').slice(0, 80)
        : '';
      const reason = reasonText ? ` (${reasonText})` : '';
      announce(
        `${sourceLabel(entry.data)} ${actionDescription(entry.data)} ${outcome}${reason}.`,
        outcome === 'cancelled'
          ? ANNOUNCEMENT_PRIORITY_CANCELLED
          : ANNOUNCEMENT_PRIORITY_FAILED);
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
    if (!data || typeof data !== 'object') return 'remote';
    const id = correlationOf(data).actionId;
    if (id && hasLocalAction(id)) {
      rememberLocal(
        id,
        message.type === 'ui-action-completed'
          ? LOCAL_ACTION_TERMINAL_TTL_MS
          : LOCAL_ACTION_ACTIVE_TTL_MS);
      return 'local';
    }

    if (message.type === 'ui-action-started' || message.type === 'ui-action-updated')
      renderStarted(data);
    else
      renderCompleted(data);
    return 'remote';
  }

  function clear({ clearLocal = false } = {}) {
    if (announcementTimer) clearTimeout(announcementTimer);
    announcementTimer = null;
    pendingAnnouncementPriority = ANNOUNCEMENT_PRIORITY_SUCCESS;
    for (const timer of pendingFailures.values()) clearTimeout(timer);
    pendingFailures.clear();
    for (const entry of [...activeActions.values()]) cleanupEntry(entry);
    cursor.classList.add('df-hidden');
    cursor.classList.remove('df-action-failed', 'df-action-cancelled');
    cursor.removeAttribute('data-action-id');
    if (cursorHideTimer) clearTimeout(cursorHideTimer);
    cursorHideTimer = null;
    if (clearLocal) clearLocalActions();
  }

  return {
    rememberLocalCorrelation,
    handleEvent,
    clear,
  };
}
