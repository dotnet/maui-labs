// DevFlow Web Inspector — Interaction Script
// Composition root: coordinates browser events, app transport, recording, hosts, and feature modules.
import { createInspectorApi } from './inspector-api.js';
import {
  COMMAND_DEFINITIONS,
  evaluateCommandStates,
  normalizeAgentCapabilities,
  resolveCommandFocus,
} from './inspector-command-policy.js';
import { confirmModal } from './inspector-dialog.js';
import { createDataSnapshot, isSecretContextKey, supportsDataContextScope } from './inspector-data-context.js';
import {
  CoordinateSpace,
  appLogicalToWindowLogical,
  buildActionCorrelation,
  clientToAppLogical,
  hasExplicitFrameIdentity,
  normalizeFrameDescriptor,
  shouldCommitFrame,
} from './inspector-frame.js';
import { createPropertyGridController } from './inspector-properties.js';
import { createElementTreeController } from './inspector-tree.js';
import { createLayoutPanel, createLayoutEventTracker, layoutContextPayload, layoutRootElementId } from './inspector-layout.js';
import {
  SCREENSHOT_SCALE_BUCKETS,
  isDefaultScreenshotScale,
  selectNativeAwareScreenshotScaleBucket,
  selectScreenshotScaleBucket,
} from './inspector-resolution.js';
import {
  createAnnouncementGate,
  createInspectorPresentationState,
  createStatusChannel,
  deriveInspectorPresentation,
  updateInspectorPresentationState,
} from './inspector-presentation.js';
import { createViewportController } from './inspector-viewport-controller.js';

(function () {
  'use strict';

  const viewport = document.getElementById('app-viewport');
  let screenshot = document.getElementById('screenshot');
  const hasInitialScreenshot = !!screenshot?.getAttribute('src');
  // Fit-to-container scaling (responsive). #app-viewport holds the app at its fixed logical size;
  // #df-stage is sized to the scaled box and #app-viewport carries the CSS scale transform.
  const stage = document.getElementById('df-stage');
  const vpWrap = document.getElementById('df-viewport-wrap');
  const disconnectedOverlay = document.getElementById('df-disconnected-overlay');
  const retryConnectionBtn = document.getElementById('df-retry-connection');
  const retryCaptureBtn = document.getElementById('df-retry-capture');
  const previewPending = document.getElementById('df-preview-pending');
  const previewHealth = document.getElementById('df-preview-health');
  const previewHealthLabel = document.getElementById('df-preview-health-label');
  const previewBadges = document.getElementById('df-preview-badges');
  const healthBanner = document.getElementById('df-health-banner');
  const healthBannerHeading = document.getElementById('df-health-banner-heading');
  const healthBannerDetail = document.getElementById('df-health-banner-detail');
  const connectionHeading = document.getElementById('df-connection-heading');
  const connectionDetail = document.getElementById('df-connection-detail');
  const healthLive = document.getElementById('df-health-live');
  const statusLine = document.getElementById('df-status');
  let healthStatusRendered = false;
  const statusChannel = createStatusChannel((text, state, textChanged) => {
    if (textChanged && statusLine) statusLine.textContent = text || '';
    healthStatusRendered = state.healthRendered;
  });
  const announcementGate = createAnnouncementGate(text => {
    if (healthLive) healthLive.textContent = text;
  });
  function setStatus(text) {
    return statusChannel.set(text);
  }
  let viewportController = null;
  let rootOffsetX = parseFloat(viewport.dataset.rootOffsetX) || 0;
  let rootOffsetY = parseFloat(viewport.dataset.rootOffsetY) || 0;

  // Determine base path for API calls (handles being served under /inspector/{id}/)
  const basePath = location.pathname.replace(/\/$/, '');
  // Per-inspector read token for data tabs, injected into the page by InspectorServer. Same-origin
  // only — a cross-origin page can't set this custom header without a preflight the broker refuses.
  const inspectorToken = (document.querySelector('meta[name="devflow-inspector-token"]') || {}).content || '';
  const inspectorApi = createInspectorApi(basePath, inspectorToken);
  const apiPost = inspectorApi.post;
  function metaContent(name) {
    const meta = document.querySelector(`meta[name="${name}"]`);
    return meta && typeof meta.content === 'string' && meta.content ? meta.content : null;
  }
  function initialFrameState() {
    let frame = null;
    try {
      frame = JSON.parse(metaContent('devflow-frame') || 'null');
    } catch {}
    return {
      frame,
      screenshotUrl: screenshot?.getAttribute('src') || null,
      viewportWidth: parseFloat(viewport.dataset.width) || viewport.offsetWidth || 1,
      viewportHeight: parseFloat(viewport.dataset.height) || viewport.offsetHeight || 1,
      rootOffsetX,
      rootOffsetY,
    };
  }
  let committedFrame = normalizeFrameDescriptor(initialFrameState());
  rootOffsetX = committedFrame.rootOffsetX;
  rootOffsetY = committedFrame.rootOffsetY;
  viewport.style.width = committedFrame.logicalWidth + 'px';
  viewport.style.height = committedFrame.logicalHeight + 'px';
  viewport.dataset.width = String(committedFrame.logicalWidth);
  viewport.dataset.height = String(committedFrame.logicalHeight);

  function correlatedPayload(
    payload,
    coordinateSpace = CoordinateSpace.appLogical,
    frame = committedFrame,
  ) {
    const correlation = buildActionCorrelation(frame, coordinateSpace);
    if (correlation) return { ...payload, correlation };
    if (coordinateSpace !== CoordinateSpace.appLogical) return payload;

    const converted = { ...payload };
    if (Number.isFinite(payload.x) && Number.isFinite(payload.y)) {
      Object.assign(converted, appLogicalToWindowLogical(payload, frame));
    }
    if (Array.isArray(payload.points)) {
      converted.points = payload.points.map(point => ({
        ...point,
        ...appLogicalToWindowLogical(point, frame),
      }));
    }
    return converted;
  }

  function isFrameCorrelationError(body) {
    return body && [
      'frame-expired',
      'frame-mismatch',
      'frame-superseded',
      'frame-target-unavailable',
    ].includes(body.error);
  }

  async function responseHasFrameCorrelationError(response) {
    if (response.status !== 409 && response.status !== 410) return false;
    return isFrameCorrelationError(await response.clone().json().catch(() => null));
  }

  const inspectorAgent = Object.freeze({
    id: metaContent('devflow-agent-id'),
    appName: metaContent('devflow-app-name'),
    platform: metaContent('devflow-platform'),
    port: Number(metaContent('devflow-agent-port')) || null,
  });
  let screenshotResolutionSupported = metaContent('devflow-screenshot-resolution') !== 'false';
  let requestedScreenshotScale = committedFrame.requestedCaptureScale || 1;
  let committedScreenshotScale = requestedScreenshotScale;
  let desiredScreenshotScale = requestedScreenshotScale;
  let learnedMaximumScreenshotScale = committedFrame.nativeScale
    || SCREENSHOT_SCALE_BUCKETS.at(-1);
  let screenshotResolutionTimer = null;
  const SCREENSHOT_RESOLUTION_UPGRADE_DELAY_MS = 350;
  const SCREENSHOT_RESOLUTION_DOWNGRADE_DELAY_MS = 1500;
  // A per-tab writer token identifies this session for the single-writer lock. A global fetch
  // wrapper stamps it on every same-origin /api/ call and flips to read-only on a writer 409.
  const writerToken = (window.crypto && crypto.randomUUID) ? crypto.randomUUID() : ('w' + Math.random().toString(36).slice(2) + Date.now());
  let leaseHolderKind = 'web';
  let leaseHolderLabel = 'Browser Inspector';
  let isWriter = false;
  let leaseHeldByOther = false;
  let otherLeaseLabel = null;
  let otherLeaseExpiresInMs = null;
  let leaseAuthority = null;
  let leaseStateInitialized = false;
  let controlRequestVersion = 0;
  let healthState = createInspectorPresentationState({
    broker: 'reachable',
    agent: 'available',
    capture: {
      status: hasInitialScreenshot ? 'never' : 'failed',
      reason: hasInitialScreenshot ? null : 'capture-failed',
      hasFrame: false,
    },
    stream: 'connecting',
  });
  function accessSnapshot() {
    return {
      isWriter,
      heldByOther: leaseHeldByOther,
      holderLabel: otherLeaseLabel,
      expiresInMs: otherLeaseExpiresInMs,
      authority: leaseAuthority,
      leaseReliable: leaseAuthority !== 'unknown',
    };
  }
  let presentation = deriveInspectorPresentation(
    healthState,
    accessSnapshot(),
    committedFrame);
  let presentationReady = false;
  let driveGateStatusToken = null;
  let driveGateStatusText = null;
  let disconnectReturnFocus = null;
  let agentCapabilities = normalizeAgentCapabilities({ capabilities: {} });
  let capabilityRefreshPromise = null;
  let capabilityRefreshController = null;
  let capabilityRefreshGeneration = 0;
  const CAPABILITY_FETCH_TIMEOUT_MS = 10000;
  const CAPABILITY_REFRESH_MS = 60000;
  // Layout scans are explicit or opt-in live operations, independent of screenshot polling.
  let layoutPanel = null;
  const layoutEventTracker = createLayoutEventTracker();
  const diagnosticsWanted = () => false;
  const _origFetch = window.fetch.bind(window);
  window.fetch = async (url, opts) => {
    if (typeof url === 'string' && url.indexOf(basePath + '/api/') === 0) {
      opts = opts || {};
      const h = new Headers(opts.headers || {});
      h.set('X-DevFlow-Lease', writerToken);
      h.set('X-DevFlow-Writer', writerToken);
      h.set('X-DevFlow-Holder', leaseHolderKind);
      h.set('X-DevFlow-Label', leaseHolderLabel);
      opts = Object.assign({}, opts, { headers: h });
      const resp = await _origFetch(url, opts);
      // A writer 409 means we tried to drive the app while read-only. Flip to read-only AND hint how
      // to recover, so a silently-ignored tap/edit/gesture doesn't look broken (the drive-BUTTONS are
      // disabled in read-only, but interact-mode taps aren't, so this closes that loop).
      if (resp.status === 409) { resp.clone().json().then((j) => { if (j && j.reason === 'writer') { controlRequestVersion++; setWriterUi(false, true, j.label, j.expiresInMs, j.authority); setStatus('Read-only — another session is driving. Take control to interact.'); } }).catch(() => {}); }
      return resp;
    }
    return _origFetch(url, opts);
  };
  window.addEventListener('pagehide', () => {
    try {
      _origFetch(`${basePath}/api/control`, {
        method: 'POST',
        keepalive: true,
        headers: {
          'Content-Type': 'application/json',
          'X-DevFlow-Lease': writerToken,
          'X-DevFlow-Writer': writerToken,
          'X-DevFlow-Holder': leaseHolderKind,
          'X-DevFlow-Label': leaseHolderLabel,
        },
        body: JSON.stringify({ action: 'release' }),
      }).catch(() => {});
    } catch {}
  });

  let healthEvidenceVersion = 0;
  let committedScreenshotUrl = screenshot?.currentSrc || screenshot?.src || null;

  function setBusyAttribute(element, busy) {
    if (!element) return;
    if (busy) element.setAttribute('aria-busy', 'true');
    else element.removeAttribute('aria-busy');
  }

  function renderRetryButton(button, label, busy) {
    if (!button) return;
    const text = button.querySelector('span');
    if (text) text.textContent = busy ? 'Checking…' : label;
    button.setAttribute('aria-disabled', String(busy));
    setBusyAttribute(button, busy);
  }

  function renderHealthBadges(badges) {
    if (!previewBadges) return;
    const fragment = document.createDocumentFragment();
    for (const badge of badges) {
      const element = document.createElement('span');
      element.className = 'df-preview-badge';
      element.dataset.badge = badge.id;
      element.dataset.tone = badge.tone;
      element.textContent = badge.label;
      element.title = badge.title || badge.label;
      fragment.append(element);
    }
    previewBadges.replaceChildren(fragment);
  }

  function badgesEqual(left = [], right = []) {
    return left.length === right.length && left.every((badge, index) => {
      const other = right[index];
      return badge.id === other.id
        && badge.tone === other.tone
        && badge.label === other.label
        && badge.title === other.title;
    });
  }

  function presenceEqual(left, right) {
    if (!left || !right) return left === right;
    return left.icon === right.icon
      && left.label === right.label
      && left.tone === right.tone
      && left.title === right.title;
  }

  function renderHealthPresentation(previous = null, announce = true) {
    presentation = deriveInspectorPresentation(
      healthState,
      accessSnapshot(),
      committedFrame);
    const blocking = presentation.showBlockingOverlay;
    const bannerHadFocus = !!(previous?.showHealthBanner && healthBanner?.contains(document.activeElement));
    const chromeChanged = !previous
      || previous.tone !== presentation.tone
      || previous.label !== presentation.label
      || previous.detail !== presentation.detail
      || previous.showBlockingOverlay !== blocking
      || previous.showHealthBanner !== presentation.showHealthBanner
      || previous.busy !== presentation.busy
      || previous.primaryAction?.label !== presentation.primaryAction?.label
      || !badgesEqual(previous.badges, presentation.badges);
    const flowChanged = !previous
      || previous.driveAllowed !== presentation.driveAllowed
      || previous.controlAllowed !== presentation.controlAllowed
      || previous.healthy !== presentation.healthy;
    const presenceChanged = !previous || !presenceEqual(previous.presence, presentation.presence);

    if (chromeChanged) {
      document.body.classList.toggle('df-disconnected', blocking);
      if (previewHealth) previewHealth.dataset.tone = presentation.tone;
      if (previewHealthLabel) previewHealthLabel.textContent = presentation.label;
      renderHealthBadges(presentation.badges);
      if (connectionHeading) connectionHeading.textContent = presentation.label;
      if (connectionDetail) connectionDetail.textContent = presentation.detail;
      if (healthBannerHeading) healthBannerHeading.textContent = presentation.label;
      if (healthBannerDetail) healthBannerDetail.textContent = presentation.detail;

      if (disconnectedOverlay) {
        disconnectedOverlay.classList.toggle('df-hidden', !blocking);
        disconnectedOverlay.setAttribute('aria-hidden', String(!blocking));
        setBusyAttribute(disconnectedOverlay, blocking && presentation.busy);
      }
      if (healthBanner) {
        healthBanner.classList.toggle('df-hidden', !presentation.showHealthBanner);
        setBusyAttribute(healthBanner, presentation.showHealthBanner && presentation.busy);
      }

      const recoveryLabel = presentation.primaryAction?.label || 'Retry now';
      renderRetryButton(retryConnectionBtn, recoveryLabel, presentation.busy);
      renderRetryButton(retryCaptureBtn, recoveryLabel, presentation.busy);
    }

    const previousStatusText = previous?.statusText || '';
    statusChannel.setHealth(presentation.statusText);
    if (announce) {
      const healthChanged = !previous
        || presentation.healthAnnouncementKey !== previous.healthAnnouncementKey;
      const accessChanged = !previous
        || presentation.accessAnnouncementKey !== previous.accessAnnouncementKey;
      const announcement = [
        healthChanged
          && (!healthStatusRendered || presentation.statusText === previousStatusText)
          ? presentation.healthAnnouncement
          : '',
        accessChanged ? presentation.accessAnnouncement : '',
      ].filter(Boolean).join(' ');
      const key = [
        healthChanged ? `health:${presentation.healthAnnouncementKey}` : '',
        accessChanged ? `access:${presentation.accessAnnouncementKey}` : '',
      ].filter(Boolean).join('|');
      announcementGate.announce(key, announcement);
    }

    if (previous?.healthy && !presentation.healthy)
      layoutPanel?.invalidate('Preview evidence changed; the previous Layout snapshot is stale.');

    if (previous && !previous.showBlockingOverlay && blocking) {
      disconnectReturnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    } else if (previous?.showBlockingOverlay && !blocking && disconnectReturnFocus?.isConnected) {
      const active = document.activeElement;
      if (!active || active === document.body || active === document.documentElement
          || disconnectedOverlay?.contains(active)) {
        disconnectReturnFocus.focus({ preventScroll: true });
      }
      disconnectReturnFocus = null;
    }
    if (previous?.showHealthBanner && !presentation.showHealthBanner && bannerHadFocus)
      document.getElementById('df-view-pane')?.focus({ preventScroll: true });
    if (driveGateStatusToken) {
      if (!statusChannel.isCurrent(driveGateStatusToken)) {
        driveGateStatusToken = null;
        driveGateStatusText = null;
      } else if (!presentation.driveMessage) {
        statusChannel.clear(driveGateStatusToken);
        driveGateStatusToken = null;
        driveGateStatusText = null;
      } else if (driveGateStatusText !== presentation.driveMessage) {
        statusChannel.replace(driveGateStatusToken, presentation.driveMessage);
        driveGateStatusText = presentation.driveMessage;
      }
    }

    if (presenceChanged) renderWriterPresence(presentation.presence);
    if (flowChanged) updateFlowButtons();
    else updateCommandStates();
    if (previous && !previous.connected && presentation.connected)
      refreshCapabilities(true);
  }

  function updateHealth(patch, announce = true) {
    const previous = presentation;
    healthState = updateInspectorPresentationState(healthState, patch);
    if (presentationReady) renderHealthPresentation(previous, announce);
  }

  function reportCaptureSuccess(announce = true) {
    healthEvidenceVersion++;
    updateHealth({
      broker: 'reachable',
      agent: 'available',
      capture: {
        status: 'ok',
        reason: null,
        hasFrame: true,
      },
    }, announce);
  }

  function reportCaptureFailure(reason) {
    healthEvidenceVersion++;
    updateHealth({
      broker: 'reachable',
      agent: 'available',
      capture: {
        status: 'failed',
        reason,
        hasFrame: healthState.capture.hasFrame,
      },
    });
  }

  function reportDisconnected(reason) {
    healthEvidenceVersion++;
    if (reason === 'browser-offline') {
      updateHealth({ broker: 'browser-offline', agent: 'unknown' });
      return;
    }
    if (reason === 'broker-unreachable') {
      updateHealth({ broker: 'unreachable', agent: 'unknown' });
      return;
    }
    if (reason === 'request-timeout') {
      updateHealth({ broker: 'timed-out', agent: 'unknown' });
      return;
    }
    updateHealth({
      broker: 'reachable',
      agent: reason === 'agent-unregistered' ? 'unregistered' : 'unavailable',
    });
  }

  async function responseFailureReason(response) {
    try {
      const body = await response.clone().json();
      return typeof body?.reason === 'string' ? body.reason : null;
    } catch {
      return null;
    }
  }

  function settleInitialScreenshot() {
    if (!hasInitialScreenshot || !screenshot) return;
    const expectedSource = screenshot.currentSrc || screenshot.src;
    const startingEvidenceVersion = healthEvidenceVersion;
    const settle = (ok) => {
      if ((screenshot.currentSrc || screenshot.src) !== expectedSource) return;
      if (ok) {
        if (healthEvidenceVersion === startingEvidenceVersion && healthState.capture.status === 'never')
          reportCaptureSuccess(false);
        else if (!healthState.capture.hasFrame)
          updateHealth({ capture: { hasFrame: true } }, false);
      } else if (healthEvidenceVersion === startingEvidenceVersion && healthState.capture.status === 'never') {
        reportCaptureFailure('image-load');
      }
    };
    if (screenshot.complete) {
      settle(screenshot.naturalWidth > 0);
      return;
    }
    screenshot.decode().then(() => settle(true), () => settle(false));
  }

  let gesturePoints = [];
  let gestureFrame = null;
  let gestureViewportRect = null;
  let gesturePointerId = null;
  let isGesturing = false;
  let isDragging = false;
  let dragResetTimer = null;
  let refreshGeneration = 0;
  let refreshRequestVersion = 0;
  let activeRefresh = null;
  const STATE_FETCH_TIMEOUT_MS = 10000;
  const STATE_REFRESH_RETRY_MS = 3000;
  const REFRESH_INTENT_RETRY = 0;
  const REFRESH_INTENT_EVIDENCE = 1;
  const REFRESH_INTENT_TAP = 2;
  const REFRESH_INTENT_MUTATION = 3;

  // Inspector selection and tree state. Declared early so the event
  // handlers registered below can read them (they only run after init has set them).
  let mode = 'interact';        // 'interact' (click drives app) | 'inspect' (click selects)
  let selectedId = null;
  let hoveredEl = null;
  let badgeEl = null;
  // Workflow recording state.
  let recordingId = null;
  let recStepCount = 0;
  let recName = null;
  let recordingStopping = false;
  // Replay + checkpoint (return-to-start-route) state.
  let lastMarkdown = null;
  let lastMarkdownName = null;
  let lastMarkdownSource = null;
  let checkpointRoute = null;
  let checkpointLabel = null;
  let replaying = false;

  // Convert browser client coordinates to the captured root's app-logical coordinate space. The
  // server resolves the correlated frame and adds its root/window offset exactly once.
  function toAppCoords(
    clientX,
    clientY,
    frame = committedFrame,
    renderedViewport = viewport.getBoundingClientRect(),
  ) {
    return clientToAppLogical(
      clientX,
      clientY,
      renderedViewport,
      frame);
  }

  function getElementCaptureMetadata(el) {
    const metadata = {};
    if (!el || typeof el.getAttribute !== 'function') return metadata;

    const captureEpochValue = el.getAttribute('data-captureEpoch');
    if (captureEpochValue !== null) {
      const captureEpoch = Number(captureEpochValue);
      if (Number.isSafeInteger(captureEpoch) && captureEpoch > 0) {
        metadata.captureEpoch = captureEpoch;
      }
    }

    const registryGenerationValue = el.getAttribute('data-registryGeneration');
    if (registryGenerationValue !== null) {
      const registryGeneration = Number(registryGenerationValue);
      if (Number.isSafeInteger(registryGeneration) && registryGeneration >= 0) {
        metadata.registryGeneration = registryGeneration;
      }
    }

    return metadata;
  }

  function copyElementCaptureMetadata(source, target) {
    target.removeAttribute('data-captureEpoch');
    target.removeAttribute('data-registryGeneration');
    const metadata = getElementCaptureMetadata(source);
    if (metadata.captureEpoch) {
      target.setAttribute('data-captureEpoch', String(metadata.captureEpoch));
    }
    if (metadata.registryGeneration !== undefined) {
      target.setAttribute('data-registryGeneration', String(metadata.registryGeneration));
    }
  }

  const EDITOR_TARGET_ATTRIBUTES = [
    'data-type',
    'data-origin',
    'data-ownerId',
    'data-automationId',
    'data-discriminator',
  ];

  function copyEditorTargetIdentity(source, target) {
    for (const attribute of EDITOR_TARGET_ATTRIBUTES) {
      const value = source.getAttribute(attribute);
      if (value !== null) {
        target.setAttribute(`data-target-${attribute.substring(5)}`, value);
      }
    }
  }

  function matchesEditorTarget(editor, target) {
    return EDITOR_TARGET_ATTRIBUTES.every(attribute => {
      const expected = editor.getAttribute(`data-target-${attribute.substring(5)}`);
      return expected === null || target.getAttribute(attribute) === expected;
    });
  }

  function findElementById(elementId) {
    for (const element of viewport.querySelectorAll('.devflow-element')) {
      if (element.getAttribute('data-id') === elementId) return element;
    }
    return null;
  }

  function previewStateSignature() {
    return JSON.stringify([...viewport.querySelectorAll('.devflow-element')].map(element =>
      ['data-id', 'style', 'data-isVisible', 'data-isEnabled', 'data-opacity', 'data-text', 'data-value']
        .map(attribute => element.getAttribute(attribute))));
  }

  function loadDecodedImage(url, signal) {
    return new Promise((resolve, reject) => {
      const image = new Image();
      const finish = (callback, value) => {
        signal.removeEventListener('abort', onAbort);
        callback(value);
      };
      const onAbort = () => {
        image.removeAttribute('src');
        finish(reject, signal.reason || new DOMException('Image load aborted.', 'AbortError'));
      };
      signal.addEventListener('abort', onAbort, { once: true });
      if (signal.aborted) {
        onAbort();
        return;
      }
      image.src = url;
      image.decode().then(
        () => finish(resolve, image),
        error => finish(reject, error));
    });
  }

  function publishDecodedScreenshot(image, replace = false) {
    if (screenshot && !replace) {
      screenshot.src = image.src;
      return;
    }

    const previous = screenshot;
    screenshot = image;
    screenshot.id = 'screenshot';
    screenshot.alt = 'App screenshot';
    screenshot.style.width = '100%';
    screenshot.style.height = '100%';
    screenshot.addEventListener('error', onScreenshotError);
    if (previous) previous.replaceWith(screenshot);
    else viewport.prepend(screenshot);
  }

  function commitFrame(state, frame, image) {
    if (hasExplicitFrameIdentity(state) && !shouldCommitFrame(committedFrame, frame)) {
      const unchanged = frame.streamId === committedFrame.streamId
        && frame.frameId === committedFrame.frameId
        && frame.frameRevision === committedFrame.frameRevision;
      if (unchanged && image && (!screenshot || screenshot.naturalWidth <= 0))
        publishDecodedScreenshot(image, true);
      return unchanged ? 'unchanged' : 'rejected';
    }

    // All frame-owned DOM changes are synchronous. The browser cannot paint or dispatch another
    // event between them, so screenshot, geometry, offsets, overlays, and descriptor become visible
    // as one complete committed frame.
    if (image) publishDecodedScreenshot(image);
    viewport.style.width = frame.logicalWidth + 'px';
    viewport.style.height = frame.logicalHeight + 'px';
    viewport.dataset.width = String(frame.logicalWidth);
    viewport.dataset.height = String(frame.logicalHeight);
    rootOffsetX = frame.rootOffsetX;
    rootOffsetY = frame.rootOffsetY;
    viewport.dataset.rootOffsetX = String(rootOffsetX);
    viewport.dataset.rootOffsetY = String(rootOffsetY);
    if (state.elements) patchElements(state.elements);
    committedFrame = frame;
    if (state.elements) onElementsUpdated();
    latestRootOffsetX = rootOffsetX;
    latestRootOffsetY = rootOffsetY;
    viewportController?.applyLayout();
    return 'committed';
  }

  function stateRequestUrl(force = false) {
    const query = new URLSearchParams();
    if (diagnosticsWanted()) query.set('diagnostics', '1');
    if (force) query.set('fresh', '1');
    if (screenshotResolutionSupported && !isDefaultScreenshotScale(requestedScreenshotScale)) {
      query.set('targetScale', String(requestedScreenshotScale));
    }
    const suffix = query.toString();
    return `${basePath}/api/state${suffix ? `?${suffix}` : ''}`;
  }

  // Refresh state via AJAX (no full page reload — avoids flash)
  async function refreshState(force = false) {
    const tap = latestTap;
    if (activeRefresh) {
      if (!force) return activeRefresh.promise;
      activeRefresh.controller.abort();
    }

    const refresh = {
      generation: ++refreshGeneration,
      requestVersion: refreshRequestVersion,
      healthEvidenceVersion,
      tap,
      controller: new AbortController(),
      followUpPromise: null,
      promise: null,
    };
    const isSuperseded = () =>
      activeRefresh !== refresh || refresh.generation !== refreshGeneration;
    const followReplacement = () =>
      activeRefresh && activeRefresh !== refresh ? activeRefresh.promise : null;
    const canPublishHealth = () =>
      refresh.requestVersion === refreshRequestVersion
      && refresh.healthEvidenceVersion === healthEvidenceVersion;
    const ensureCurrent = () => {
      if (isSuperseded()) return false;
      if (refresh.controller.signal.aborted) {
        throw refresh.controller.signal.reason
          || new DOMException('State refresh aborted.', 'AbortError');
      }
      return true;
    };
    activeRefresh = refresh;
    refresh.promise = (async () => {
      let phase = 'request';
      let timedOut = false;
      const timeout = setTimeout(() => {
        if (isSuperseded()) return;
        timedOut = true;
        refresh.controller.abort();
      }, STATE_FETCH_TIMEOUT_MS);
      try {
        const requestedScale = requestedScreenshotScale;
        const resp = await fetch(stateRequestUrl(force), { signal: refresh.controller.signal });
        if (!ensureCurrent()) return followReplacement();
        if (!resp.ok) {
          const reason = await responseFailureReason(resp);
          if (!ensureCurrent()) return followReplacement();
          if (canPublishHealth()) {
            if (!navigator.onLine) {
              reportDisconnected('browser-offline');
            } else if (reason === 'capture-failed') {
              reportCaptureFailure(reason);
            } else if (resp.status === 404) {
              reportDisconnected('agent-unregistered');
            } else if (reason === 'agent-unavailable'
                || (!reason && [502, 503, 504].includes(resp.status))) {
              reportDisconnected('agent-unavailable');
            } else {
              reportCaptureFailure(reason || 'request-failed');
            }
          }
          return null;
        }
        phase = 'response-body';
        const state = await resp.json();
        if (!ensureCurrent()) return followReplacement();
        phase = 'state';
        const frame = normalizeFrameDescriptor(state, committedFrame);
        if (state.screenshotResolutionSupported === false) {
          screenshotResolutionSupported = false;
          learnedMaximumScreenshotScale = 1;
          desiredScreenshotScale = requestedScreenshotScale = 1;
          if (screenshotResolutionTimer) clearTimeout(screenshotResolutionTimer);
          screenshotResolutionTimer = null;
        } else if (state.screenshotResolutionSupported === true) {
          screenshotResolutionSupported = true;
        }

        // Keep the previous frame intact until its replacement image is ready.
        let image = null;
        if (state.screenshotUrl) {
          phase = 'decode';
          image = await loadDecodedImage(state.screenshotUrl, refresh.controller.signal);
          if (!ensureCurrent()) return followReplacement();
          if (!navigator.onLine) {
            reportDisconnected('browser-offline');
            return null;
          }
        }
        if (!ensureCurrent()) return followReplacement();
        phase = 'apply';
        const commitResult = commitFrame(state, frame, image);
        if (commitResult === 'rejected') {
          const replacement = followReplacement();
          if (replacement) return replacement;
          scheduleRefresh(300, REFRESH_INTENT_RETRY);
          return null;
        }
        if (image && (commitResult === 'committed' || !committedScreenshotUrl))
          committedScreenshotUrl = image.src;
        if (canPublishHealth()) reportCaptureSuccess();

        committedScreenshotScale = Number(frame.requestedCaptureScale) || requestedScale;
        const actualCaptureScale = Number(frame.captureScale);
        const reportedNativeScale = Number(frame.nativeScale);
        const previousMaximum = learnedMaximumScreenshotScale;
        if (Number.isFinite(reportedNativeScale) && reportedNativeScale > 0) {
          learnedMaximumScreenshotScale = reportedNativeScale;
        } else if (Number.isFinite(actualCaptureScale)
          && actualCaptureScale > 0
          && actualCaptureScale + 0.01 < requestedScale) {
          learnedMaximumScreenshotScale = Math.min(
            learnedMaximumScreenshotScale,
            actualCaptureScale);
        }
        if (learnedMaximumScreenshotScale !== previousMaximum)
          viewportController?.scheduleLayout();
        const treeRevision = committedFrame.treeRevision;
        const diagnosticInputsRevision = committedFrame.diagnosticInputsRevision;
        const renderedState = [...viewport.querySelectorAll('.devflow-element')].map(element =>
          ['data-id', 'style', 'data-isVisible', 'data-isEnabled', 'data-opacity', 'data-text', 'data-value']
            .map(attribute => element.getAttribute(attribute)));
        layoutPanel?.frameChanged(JSON.stringify([treeRevision, diagnosticInputsRevision, renderedState]));
        if (tap !== null && tap === latestTap && refresh.requestVersion === refreshRequestVersion) {
          const renderedStateSignature = JSON.stringify(renderedState);
          if (renderedStateSignature !== tap.previewStateSignature || tap.unchangedCaptures > 0) {
            latestTap = null;
            if (refreshTimer
                && refreshTimerDelay === STATE_REFRESH_RETRY_MS
                && refreshTimerIntent === REFRESH_INTENT_RETRY) {
              clearTimeout(refreshTimer);
              refreshTimer = null;
              refreshTimerDelay = null;
              refreshTimerIntent = REFRESH_INTENT_RETRY;
            }
            renderPreviewPending();
          } else {
            tap.unchangedCaptures++;
            scheduleRefresh(300, REFRESH_INTENT_RETRY);
          }
        }
        return state;
      } catch (err) {
        if (isSuperseded()) {
          return followReplacement();
        }
        if (canPublishHealth()) {
          if (!navigator.onLine) {
            reportDisconnected('browser-offline');
          } else if (phase === 'decode') {
            reportCaptureFailure(timedOut ? 'capture-timeout' : 'image-decode');
          } else if (phase === 'state' || phase === 'apply') {
            reportCaptureFailure('capture-failed');
          } else if (phase === 'response-body' && !timedOut) {
            reportCaptureFailure('invalid-response');
          } else if (timedOut) {
            reportDisconnected('request-timeout');
          } else {
            reportDisconnected('broker-unreachable');
          }
        }
        console.error('State refresh failed:', err);
        return null;
      } finally {
        clearTimeout(timeout);
      }
    })();
    try {
      return await refresh.promise;
    } finally {
      if (activeRefresh === refresh) {
        activeRefresh = null;
      }
    }
  }

  async function retryPreview() {
    if (healthState.retrying) return;
    if (driveGateStatusToken) {
      statusChannel.clear(driveGateStatusToken);
      driveGateStatusToken = null;
      driveGateStatusText = null;
    }
    updateHealth({ retrying: true });
    try {
      await refreshState(true);
    } finally {
      updateHealth({ retrying: false });
    }
  }

  async function refreshCapabilities(force = false) {
    if (capabilityRefreshPromise && !force) return capabilityRefreshPromise;
    if (force) capabilityRefreshController?.abort();
    const generation = ++capabilityRefreshGeneration;
    const controller = new AbortController();
    capabilityRefreshController = controller;
    const timeout = setTimeout(() => controller.abort(), CAPABILITY_FETCH_TIMEOUT_MS);
    const request = (async () => {
      try {
        const response = await fetch(`${basePath}/api/capabilities`, {
          cache: 'no-store',
          signal: controller.signal,
        });
        if (!response.ok) return;
        const payload = await response.json().catch(() => null);
        if (generation !== capabilityRefreshGeneration ||
            !payload || typeof payload.capabilities !== 'object' || Array.isArray(payload.capabilities)) return;
        const normalized = normalizeAgentCapabilities(payload);
        if (!normalized) return;
        agentCapabilities = normalized;
        updateCommandStates();
      } catch {
        // Keep the last valid document; Preview health owns transport presentation.
      } finally {
        clearTimeout(timeout);
      }
    })();
    capabilityRefreshPromise = request;
    try {
      await request;
    } finally {
      if (capabilityRefreshPromise === request) capabilityRefreshPromise = null;
      if (capabilityRefreshController === controller) capabilityRefreshController = null;
    }
  }
  for (const button of [retryConnectionBtn, retryCaptureBtn]) {
    if (!button) continue;
    button.addEventListener('click', () => {
      if (button.getAttribute('aria-disabled') === 'true') return;
      retryPreview();
    });
  }

  // Keyed DOM diff: match elements by data-id, update in-place if changed
  function patchElements(newHtml) {
    // ─────────────────────────────────────────────────────────────────────────
    // XSS / trust boundary contract with the server.
    //
    // `newHtml` is parsed into the live DOM via `innerHTML`, so any HTML it
    // contains is executed (attributes, <script>, event handlers, etc.).
    // This is only safe because the server side (HtmlRenderer) is the SOLE
    // producer of `newHtml` and guarantees:
    //
    //   1. Element identifiers, types, and any user-controlled text reach this
    //      function only via `HttpUtility.HtmlAttributeEncode` (in attribute
    //      positions) or `HttpUtility.HtmlEncode` (in text positions), which
    //      neutralise `"`, `'`, `&`, `<`, `>`.
    //   2. No URL/JS context substitution happens server-side (no href/src/
    //      onclick built from app-provided strings), so attribute-escaping is
    //      sufficient — there is no executable context to escape into.
    //   3. The response is fetched same-origin from this very inspector page,
    //      gated by the broker's loopback + Origin-port check, so an attacker
    //      cannot substitute their own HTML at the network layer.
    //
    // If any of those invariants change (raw HTML pass-through, JSON-string
    // interpolation, cross-origin fetch, etc.), replace `innerHTML` here with
    // explicit DOM construction (`createElement` + `setAttribute`) before the
    // change ships — `innerHTML` parsing of server-controlled HTML is fragile
    // and silently turns from safe into XSS-vulnerable.
    // ─────────────────────────────────────────────────────────────────────────

    // Parse new elements into a temp container
    const temp = document.createElement('div');
    temp.innerHTML = newHtml;

    // Build map of new elements by data-id
    const newEls = temp.querySelectorAll('.devflow-element');
    const newMap = new Map();
    const newOrder = [];
    newEls.forEach(el => {
      const id = el.getAttribute('data-id');
      if (id) {
        newMap.set(id, el);
        newOrder.push(id);
      }
    });

    // Build map of existing elements
    const oldEls = viewport.querySelectorAll('.devflow-element');
    const oldMap = new Map();
    oldEls.forEach(el => {
      const id = el.getAttribute('data-id');
      if (id) oldMap.set(id, el);
    });

    // Remove elements that no longer exist
    oldMap.forEach((el, id) => {
      if (!newMap.has(id)) {
        el.remove();
      }
    });

    // Update existing elements in-place or insert new ones
    let prevEl = screenshot; // insert after screenshot
    for (const id of newOrder) {
      const newEl = newMap.get(id);
      const oldEl = oldMap.get(id);

      if (oldEl) {
        // Update only if style or attributes changed
        if (oldEl.getAttribute('style') !== newEl.getAttribute('style')) {
          oldEl.setAttribute('style', newEl.getAttribute('style'));
        }
        // Sync data attributes
        syncDataAttrs(oldEl, newEl);
        // Ensure correct order
        if (prevEl && prevEl.nextSibling !== oldEl) {
          prevEl.after(oldEl);
        }
        prevEl = oldEl;
      } else {
        // New element — insert after previous
        const clone = newEl.cloneNode(true);
        if (prevEl) {
          prevEl.after(clone);
        } else {
          viewport.appendChild(clone);
        }
        prevEl = clone;
      }
    }
  }

  // Sync data-* attributes from src to dst without replacing the element
  function syncDataAttrs(dst, src) {
    // Remove old data attrs not in src
    for (const attr of [...dst.attributes]) {
      if (attr.name.startsWith('data-') && !src.hasAttribute(attr.name)) {
        dst.removeAttribute(attr.name);
      }
    }
    // Set/update from src
    for (const attr of src.attributes) {
      if (attr.name.startsWith('data-') && dst.getAttribute(attr.name) !== attr.value) {
        dst.setAttribute(attr.name, attr.value);
      }
    }
  }

  // Debounced refresh — coalesce rapid calls
  let refreshTimer = null;
  let refreshTimerDelay = null;
  let refreshTimerIntent = REFRESH_INTENT_RETRY;
  let refreshTimerRequiresFresh = false;
  let latestTap = null; // newest accepted tap the Preview does not reflect yet
  let pendingTapRequests = 0;
  function renderPreviewPending() {
    previewPending.classList.toggle('df-hidden', pendingTapRequests === 0 && latestTap === null);
  }
  function queueRefreshAfter(refresh) {
    refresh.followUpPromise ??= (async () => {
      await refresh.promise;
      const current = activeRefresh;
      if (current && current !== refresh && current.requestVersion === refreshRequestVersion)
        return current.promise;
      return refreshState(true);
    })();
    return refresh.followUpPromise;
  }
  function scheduleRefresh(
    delayMs = 300,
    intent = REFRESH_INTENT_EVIDENCE,
    requiresFresh = false) {
    if (intent === REFRESH_INTENT_EVIDENCE || intent === REFRESH_INTENT_MUTATION)
      refreshRequestVersion++;
    refreshTimerRequiresFresh = refreshTimerRequiresFresh || requiresFresh;
    if (refreshTimer) {
      const pendingIntent = refreshTimerIntent;
      refreshTimerIntent = Math.max(refreshTimerIntent, intent);
      if (refreshTimerDelay === 0 && delayMs > 0) return;
      if (pendingIntent > intent && delayMs > 0) return;
      clearTimeout(refreshTimer);
    } else {
      refreshTimerIntent = intent;
    }
    refreshTimerDelay = delayMs;
    refreshTimer = setTimeout(async () => {
      refreshTimer = null;
      refreshTimerDelay = null;
      const scheduledIntent = refreshTimerIntent;
      const scheduledRequiresFresh = refreshTimerRequiresFresh;
      refreshTimerIntent = REFRESH_INTENT_RETRY;
      refreshTimerRequiresFresh = false;
      const tap = latestTap;
      let state;
      if (scheduledIntent === REFRESH_INTENT_MUTATION) {
        state = await refreshState(true);
      } else if (scheduledIntent === REFRESH_INTENT_TAP) {
        if (tap !== null && activeRefresh?.tap === tap) {
          const sharedRefresh = activeRefresh;
          state = await sharedRefresh.promise;
          if (sharedRefresh.requestVersion !== refreshRequestVersion)
            state = await queueRefreshAfter(sharedRefresh);
        } else {
          state = await refreshState(tap !== null || scheduledRequiresFresh);
        }
      } else if (scheduledIntent === REFRESH_INTENT_RETRY) {
        state = activeRefresh
          ? await activeRefresh.promise
          : await refreshState(scheduledRequiresFresh);
      } else if (activeRefresh) {
        state = await queueRefreshAfter(activeRefresh);
      } else {
        state = await refreshState(scheduledRequiresFresh);
      }
      if (!state && latestTap !== null && !activeRefresh)
        scheduleRefresh(STATE_REFRESH_RETRY_MS, REFRESH_INTENT_RETRY);


    }, delayMs);
  }
  function onScreenshotError(event) {
    const currentSource = event.currentTarget.currentSrc || event.currentTarget.src;
    if (!activeRefresh && presentation.connected && currentSource === committedScreenshotUrl)
      reportCaptureFailure('image-load');
    scheduleRefresh(STATE_REFRESH_RETRY_MS, REFRESH_INTENT_RETRY);
  }
  if (screenshot) screenshot.addEventListener('error', onScreenshotError);

  // ── Click → Tap (with text-input awareness) ──
  // Element types that should open a text editor instead of just tapping.
  const TEXT_INPUT_TYPES = new Set([
    'Entry', 'Editor', 'SearchBar', 'SearchHandler',
    'TextField', 'TextBox', 'TextArea', 'TextView',
    'UITextField', 'UITextView',
    'EditText', 'NSTextField',
  ]);

  function elementCapabilities(el) {
    return (el?.dataset.capabilities || '').toLowerCase().split(',').map(value => value.trim()).filter(Boolean);
  }

  function isTextInput(el) {
    if (!el || !el.classList || !el.classList.contains('devflow-element')) return false;
    const type = el.dataset.type || '';
    if (TEXT_INPUT_TYPES.has(type)) return true;
    if (elementCapabilities(el).includes('set-value')) return true;
    // Heuristic: traits often expose "TextInput" / "Editable"
    const traits = (el.dataset.traits || '').toLowerCase();
    return traits.includes('textinput') || traits.includes('editable');
  }

  function canEditTextInput(el) {
    if (!isTextInput(el) || el.getAttribute('data-isEnabled') === 'false' ||
        el.getAttribute('data-isReadOnly') === 'true') return false;
    // A tap recognizer may open a picker or navigate away. Drive that action, not an editor.
    const gestures = (el.dataset.gestures || '').toLowerCase().split(',');
    if (gestures.includes('tap')) return false;
    const capabilities = elementCapabilities(el);
    return capabilities.length === 0 || capabilities.includes('set-value');
  }

  function showDriveGateStatus(message) {
    if (driveGateStatusToken && statusChannel.isCurrent(driveGateStatusToken)) {
      statusChannel.replace(driveGateStatusToken, message);
    } else {
      driveGateStatusToken = setStatus(message);
    }
    driveGateStatusText = message;
  }

  function ensureCanDrive() {
    if (presentation.driveAllowed) return true;
    showDriveGateStatus(presentation.driveMessage || 'This action is currently unavailable.');
    if (presentation.healthy) drawAttentionToTakeControl(true);
    return false;
  }

  function ensureCanInteract() {
    const state = commandStates['df-mode-interact'];
    if (state?.visible === false) {
      setStatus(state.reason?.text || 'App interaction is not supported by this agent.');
      return false;
    }
    return ensureCanDrive();
  }

  function ensureCanInspect() {
    const state = commandStates['df-mode-inspect'];
    if (state?.visible !== false) return true;
    setStatus(state.reason?.text || 'App inspection is not supported by this agent.');
    return false;
  }

  // Overlay editor that we float on top of the clicked text element.
  let activeEditor = null;
  const editorFrames = new WeakMap();
  let inspectHitToken = 0;
  const hitCandidates = document.getElementById('df-hit-candidates');

  function candidateLabel(candidate) {
    const type = candidate.type || 'Element';
    const name = candidate.automationId || candidate.text || '';
    return name ? `${type} · ${name}` : type;
  }

  function hideHitCandidates() {
    if (!hitCandidates) return;
    hitCandidates.replaceChildren();
    hitCandidates.classList.add('df-hidden');
  }

  function showHitCandidates(candidates, selectedId) {
    if (!hitCandidates || !Array.isArray(candidates) || candidates.length < 2) {
      hideHitCandidates();
      return;
    }
    hitCandidates.replaceChildren();
    for (const candidate of candidates) {
      if (!candidate || !candidate.id) continue;
      const button = document.createElement('button');
      button.type = 'button';
      button.setAttribute('role', 'option');
      button.setAttribute('aria-selected', String(candidate.id === selectedId));
      button.textContent = candidateLabel(candidate);
      button.addEventListener('click', () => {
        selectElement(candidate.id);
        hideHitCandidates();
      });
      hitCandidates.appendChild(button);
    }
    hitCandidates.classList.toggle('df-hidden', hitCandidates.childElementCount < 2);
  }

  async function selectAtPoint(clientX, clientY, fallbackElement) {
    const token = ++inspectHitToken;
    const { x, y } = toAppCoords(clientX, clientY);
    try {
      const response = await fetch(`${basePath}/api/hitTest`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(correlatedPayload({ x, y })),
      });
      if (await responseHasFrameCorrelationError(response))
        scheduleRefresh(0, REFRESH_INTENT_EVIDENCE);
      const result = response.ok ? await response.json().catch(() => null) : null;
      if (token !== inspectHitToken) return;
      if (result && result.ok && result.elementId) {
        selectElement(result.elementId);
        showHitCandidates(result.candidates, result.elementId);
        return;
      }
    } catch {
      // Older or temporarily unavailable agents fall back to the rendered overlay below.
    }
    if (token !== inspectHitToken) return;
    hideHitCandidates();
    selectElement(fallbackElement ? fallbackElement.getAttribute('data-id') : null);
  }

  function removeEditor(editor) {
    if (activeEditor === editor) activeEditor = null;
    editorFrames.delete(editor);
    editor.remove();
  }

  function preserveEditorAfterFailure(editor, status, refocus = true) {
    delete editor.dataset.commitInProgress;
    editor.readOnly = false;
    editor.setAttribute('aria-invalid', 'true');
    editor.style.borderColor = '#d13438';
    editor.title = status
      ? `Unable to update the app (${status}). Your text has been preserved.`
      : 'Unable to update the app. Your text has been preserved.';
    if (refocus && activeEditor === editor) {
      setTimeout(() => editor.focus({ preventScroll: true }), 0);
    }
  }

  async function commitEditor(editor, retryStaleCapture) {
    try {
      const response = await fetch(`${basePath}/api/fill`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(correlatedPayload(
          {
            elementId: editor.dataset.elementId,
            text: editor.value,
            ...getElementCaptureMetadata(editor),
          },
          CoordinateSpace.appLogical,
          editorFrames.get(editor) || committedFrame)),
      });

      if (response.ok) {
        if (recordingId) {
          recordStepById('fill', editor.dataset.elementId, { value: editor.value });
        }
        removeEditor(editor);
        scheduleRefresh(300, REFRESH_INTENT_MUTATION);
        return;
      }

      if (retryStaleCapture && (response.status === 409 || response.status === 410)) {
        await refreshState(true);
        const currentTarget = findElementById(editor.dataset.elementId);
        if (currentTarget
            && isTextInput(currentTarget)
            && matchesEditorTarget(editor, currentTarget)) {
          copyElementCaptureMetadata(currentTarget, editor);
          editorFrames.set(editor, committedFrame);
          await commitEditor(editor, false);
          return;
        }
      }

      preserveEditorAfterFailure(editor, response.status);
    } catch (err) {
      console.error('Fill failed:', err);
      preserveEditorAfterFailure(editor);
    }
  }

  function closeEditor(commit) {
    if (!activeEditor) return;
    const editor = activeEditor;
    if (commit) {
      if (editor.dataset.dirty !== 'true') {
        removeEditor(editor);
        return;
      }
      // Keep unsent text visible until the Preview and writer lease recover.
      if (!ensureCanInteract()) {
        preserveEditorAfterFailure(editor, null, false);
        return;
      }
      if (editor.dataset.commitInProgress === 'true') return;
      editor.dataset.commitInProgress = 'true';
      editor.readOnly = true;
      commitEditor(editor, true);
      return;
    }
    removeEditor(editor);
  }

  function openEditor(targetEl) {
    if (!canEditTextInput(targetEl)) return;
    if (activeEditor?.dataset.commitInProgress === 'true') return;
    if (activeEditor?.dataset.dirty === 'true') {
      setStatus('Finish or discard the preserved edit before editing another field.');
      return;
    }
    closeEditor(false);
    const elementId = targetEl.getAttribute('data-id');
    if (!elementId) return;

    // Position with UNSCALED layout coords (offset*) relative to #app-viewport (the offsetParent).
    // The editor is appended inside #app-viewport, which may carry a CSS scale transform; using the
    // scaled getBoundingClientRect() here would double-apply the scale (~s^2). offsetLeft/Top/Width/
    // Height are in the app's own pre-transform pixel space, so the parent transform scales them.
    const left = targetEl.offsetLeft, top = targetEl.offsetTop;
    const w = targetEl.offsetWidth, h = targetEl.offsetHeight;
    const isMultiline = ['Editor', 'TextArea', 'TextView', 'UITextView'].includes(targetEl.dataset.type || '');
    const isSensitive = targetEl.dataset.sensitive === 'true';
    const editor = document.createElement(isMultiline ? 'textarea' : 'input');
    if (!isMultiline) editor.type = isSensitive ? 'password' : 'text';
    editor.value = isSensitive ? '' : targetEl.dataset.text || targetEl.dataset.value || '';
    editor.dataset.elementId = elementId;
    editor.dataset.dirty = 'false';
    copyElementCaptureMetadata(targetEl, editor);
    copyEditorTargetIdentity(targetEl, editor);
    Object.assign(editor.style, {
      position: 'absolute',
      left: left + 'px',
      top: top + 'px',
      width: w + 'px',
      height: h + 'px',
      zIndex: '10000',
      background: 'rgba(255,255,255,0.97)',
      color: '#000',
      border: '2px solid #4ec9b0',
      borderRadius: '2px',
      padding: '2px 4px',
      font: 'inherit',
      fontSize: Math.max(11, Math.min(20, h * 0.5)) + 'px',
      outline: 'none',
      boxSizing: 'border-box',
      resize: 'none',
    });

    editor.addEventListener('input', () => {
      editor.dataset.dirty = 'true';
    });
    editor.addEventListener('keydown', (ev) => {
      if (ev.key === 'Escape') {
        ev.preventDefault();
        closeEditor(false);
      } else if (ev.key === 'Enter' && !isMultiline) {
        ev.preventDefault();
        closeEditor(true);
      }
    });
    editor.addEventListener('blur', () => closeEditor(true));

    viewport.appendChild(editor);
    editorFrames.set(editor, committedFrame);
    activeEditor = editor;
    // Use a microtask so the click that opened us doesn't immediately blur it.
    setTimeout(() => { editor.focus({ preventScroll: true }); editor.select(); }, 0);
  }

  function pulseActionTarget(el) {
    if (!el || !el.classList) return;
    el.classList.remove('df-action-target');
    void el.offsetWidth;
    el.classList.add('df-action-target');
    setTimeout(() => el.classList.remove('df-action-target'), 700);
  }

  async function sendInspectorTap(x, y, targetEl) {
    const targetId = isTapTargetOverlay(targetEl) ? targetEl.getAttribute('data-id') : null;
    const targetLabel = targetEl ? elementLabel(targetEl) : null;
    const payload = correlatedPayload({ x, y, ...getElementCaptureMetadata(targetEl) });
    if (targetId) payload.elementId = targetId;
    const previewSignature = previewStateSignature();

    pendingTapRequests++;
    renderPreviewPending();
    let refreshIntent = REFRESH_INTENT_TAP;
    try {
      const response = await inspectorApi.postDetailed('/api/tap', payload);
      const result = response.body;
      if (!response.ok || !result || result.ok !== true) {
        if (isFrameCorrelationError(result))
          refreshIntent = REFRESH_INTENT_EVIDENCE;
        const reason = result && typeof result.reason === 'string'
          ? result.reason
          : 'The app did not accept the tap.';
        setStatus(`Tap did not run — ${reason}`);
        return { ok: false, result, target: targetEl };
      }

      const actual = result.elementId ? (elById(result.elementId) || targetEl) : targetEl;
      pulseActionTarget(actual);
      const label = actual ? elementLabel(actual) : targetLabel;
      setStatus(label ? `Tap sent to ${label}.` : 'Tap sent to the app.');
      const tap = {
        ok: true,
        result,
        target: actual || targetEl,
        previewStateSignature: previewSignature,
        unchangedCaptures: 0,
      };
      latestTap = tap;
      return tap;
    } finally {
      pendingTapRequests--;
      renderPreviewPending();
      scheduleRefresh(0, refreshIntent);
    }
  }

  viewport.addEventListener('click', async (e) => {
    if (isDragging) return;
    // If the user clicks back into the active editor, ignore.
    if (activeEditor && (e.target === activeEditor || activeEditor.contains(e.target))) return;

    // setPointerCapture(viewport) makes e.target be the viewport itself for real
    // mouse clicks, so use elementFromPoint to find the actual element under the
    // cursor. Temporarily hide any active editor so it doesn't shadow the click.
    let underCursor = document.elementFromPoint(e.clientX, e.clientY);
    if (underCursor === viewport || underCursor === screenshot) {
      // Both are pointer-events:none / non-interactive overlays; fall back to e.target.
      underCursor = e.target;
    }

    // Inspect mode (or Alt/Shift+Click while in Interact) SELECTS the element under the
    // cursor instead of tapping the app — the entry point for the tree/bounds features (A + B).
    if (mode === 'inspect' || e.altKey || e.shiftKey) {
      if (!ensureCanInspect()) return;
      const picked = underCursor && underCursor.closest ? underCursor.closest('.devflow-element') : null;
      await selectAtPoint(e.clientX, e.clientY, picked);
      return;
    }
    if (!ensureCanInteract()) return;

    let textEl = underCursor;
    while (textEl && textEl !== viewport && !isTextInput(textEl)) textEl = textEl.parentElement;
    if (textEl && textEl !== viewport && canEditTextInput(textEl)) {
      const { x: tx, y: ty } = toAppCoords(e.clientX, e.clientY);
      sendInspectorTap(tx, ty, textEl).catch(err => console.error('Tap failed:', err));
      openEditor(textEl);
      return;
    }

    // Capture the tap target BEFORE driving (a navigation can destroy it) so a recorded step
    // carries the durable selector of the element the user targeted. We drive by coordinate (works
    // for any element, interactive or not) and record the hit element — replay resolves by selector.
    const tapEl = hoverHitTest(e.clientX, e.clientY) ||
      (underCursor && underCursor.closest ? underCursor.closest('.devflow-element') : null);
    const { x, y } = toAppCoords(e.clientX, e.clientY);

    try {
      const tap = await sendInspectorTap(x, y, tapEl);
      if (recordingId) {
        if (tap.ok && tap.target) await recordStep('tap', tap.target);
        else if (!tap.target) setStatus('Tap not recorded — no element under the cursor.');
      }
    } catch (err) {
      console.error('Tap failed:', err);
      setStatus('Tap did not run — the Inspector could not reach the app.');
    }
  });

  // ── Wheel → Scroll ──
  let scrollAccumX = 0, scrollAccumY = 0;
  let scrollFlushTimer = null;
  let lastScrollPoint = null;
  let scrollFrame = null;

  async function flushScroll() {
    if (!scrollFrame || !lastScrollPoint || (scrollAccumX === 0 && scrollAccumY === 0)) {
      scrollFlushTimer = null;
      return;
    }

    const frame = scrollFrame;
    const { x, y } = lastScrollPoint;
    const dx = scrollAccumX, dy = scrollAccumY;
    scrollAccumX = 0;
    scrollAccumY = 0;
    scrollFlushTimer = null;
    scrollFrame = null;
    lastScrollPoint = null;

    if (!ensureCanInteract()) return;

    try {
      const resp = await fetch(`${basePath}/api/scroll`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(correlatedPayload(
          { x, y, deltaX: dx, deltaY: dy },
          CoordinateSpace.appLogical,
          frame))
      });
      if (await responseHasFrameCorrelationError(resp))
        scheduleRefresh(0, REFRESH_INTENT_EVIDENCE);
      if (recordingId && resp.ok) recordStep('scroll', null, { dx, dy });
      scheduleRefresh(300, resp.ok ? REFRESH_INTENT_MUTATION : REFRESH_INTENT_RETRY);
    } catch (err) {
      console.error('Scroll failed:', err);
    }
  }

  viewport.addEventListener('wheel', (e) => {
    if (e.defaultPrevented) return;
    e.preventDefault();
    if (!ensureCanInteract()) return;
    if (scrollFrame && scrollFrame !== committedFrame) {
      if (scrollFlushTimer) clearTimeout(scrollFlushTimer);
      void flushScroll();
    }
    scrollFrame = committedFrame;
    scrollAccumX += e.deltaX;
    scrollAccumY += e.deltaY;
    lastScrollPoint = toAppCoords(
      e.clientX,
      e.clientY,
      scrollFrame,
      viewport.getBoundingClientRect());

    if (scrollFlushTimer) clearTimeout(scrollFlushTimer);
    scrollFlushTimer = setTimeout(() => { void flushScroll(); }, 100);
  }, { passive: false });

  // ── Pointer Drag → Gesture ──
  viewport.addEventListener('pointerdown', (e) => {
    if (e.defaultPrevented) return;
    if (e.button !== 0 || !e.isPrimary || isGesturing) return;
    if (!ensureCanInteract()) return;
    if (dragResetTimer) {
      clearTimeout(dragResetTimer);
      dragResetTimer = null;
    }
    gesturePointerId = e.pointerId;
    gestureFrame = committedFrame;
    gestureViewportRect = viewport.getBoundingClientRect();
    const { x, y } = toAppCoords(
      e.clientX,
      e.clientY,
      gestureFrame,
      gestureViewportRect);
    gesturePoints = [{ x, y, sx: e.clientX, sy: e.clientY, t: Date.now() }];
    isGesturing = true;
    isDragging = false;
    viewport.setPointerCapture(e.pointerId);
  });

  viewport.addEventListener('pointermove', (e) => {
    if (!isGesturing || e.pointerId !== gesturePointerId) return;
    const { x, y } = toAppCoords(
      e.clientX,
      e.clientY,
      gestureFrame || committedFrame,
      gestureViewportRect || viewport.getBoundingClientRect());
    gesturePoints.push({ x, y, sx: e.clientX, sy: e.clientY, t: Date.now() });
    // Drag-vs-tap decision uses on-screen (CSS px) distance so it behaves the same at any fit scale.
    const s0 = gesturePoints[0];
    if (Math.hypot(e.clientX - s0.sx, e.clientY - s0.sy) > 8) isDragging = true;
  });

  viewport.addEventListener('pointerup', async (e) => {
    if (!isGesturing || e.pointerId !== gesturePointerId) return;
    const points = gesturePoints;
    const frame = gestureFrame || committedFrame;
    isGesturing = false;
    gesturePoints = [];
    gestureFrame = null;
    gestureViewportRect = null;
    gesturePointerId = null;
    viewportController?.scheduleLayout();
    dragResetTimer = setTimeout(() => {
      dragResetTimer = null;
      if (!isGesturing) isDragging = false;
    }, 50);

    if (points.length >= 2) {
      const first = points[0];
      const last = points[points.length - 1];
      // Threshold on screen distance (scale-independent); send app-coordinate points to the app.
      const screenDist = Math.hypot(last.sx - first.sx, last.sy - first.sy);

      if (mode === 'interact' && screenDist > 12) {
        if (!ensureCanInteract()) return;
        try {
          const response = await fetch(`${basePath}/api/gesture`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(correlatedPayload(
              {
                points: points.map((p) => ({ x: p.x, y: p.y, t: p.t })),
              },
              CoordinateSpace.appLogical,
              frame))
          });
          if (await responseHasFrameCorrelationError(response))
            scheduleRefresh(0, REFRESH_INTENT_EVIDENCE);
          scheduleRefresh(300, response.ok ? REFRESH_INTENT_MUTATION : REFRESH_INTENT_RETRY);
          if (recordingId) setStatus('Gesture performed but not recorded (flow tests support tap/fill/scroll).');
        } catch (err) {
          console.error('Gesture failed:', err);
        }
      }
    }

  });
  function cancelAppGesture(event) {
    if (!isGesturing || event.pointerId !== gesturePointerId) return;
    isGesturing = false;
    isDragging = false;
    gesturePoints = [];
    gestureFrame = null;
    gestureViewportRect = null;
    gesturePointerId = null;
    viewportController?.scheduleLayout();
    if (dragResetTimer) clearTimeout(dragResetTimer);
    dragResetTimer = setTimeout(() => {
      dragResetTimer = null;
      if (!isGesturing) isDragging = false;
    }, 50);
  }
  viewport.addEventListener('pointercancel', cancelAppGesture);
  viewport.addEventListener('lostpointercapture', cancelAppGesture);

  // ── Live updates: a WebSocket to the broker-proxied /ws/events makes the mirror react instantly
  // to app-side changes. The 3s poll below stays as a zero-regression fallback and only refreshes
  // when the socket is NOT live (wsLive), health is degraded, or Layout is visible: an open
  // event stream does not guarantee app reachability or every native layout change emits an event.
  // This poll never analyzes layout.
  let wsLive = false;
  let eventsWs = null;
  let eventConnectTimer = null;
  let eventSupportCheckInFlight = false;
  const EVENT_RETRY_MS = 3000;
  const EVENT_UNSUPPORTED_RECHECK_MS = 60000;

  function reportEventStatus(status) {
    updateHealth({ stream: status }, false);
  }

  function scheduleEventConnect(delay, status = 'reconnecting') {
    wsLive = false;
    reportEventStatus(status);
    if (eventConnectTimer) return;
    eventConnectTimer = setTimeout(() => {
      eventConnectTimer = null;
      connectEvents();
    }, delay);
  }

  async function connectEvents() {
    if (eventsWs && (eventsWs.readyState === WebSocket.CONNECTING || eventsWs.readyState === WebSocket.OPEN)) return;
    if (eventSupportCheckInFlight) return;
    if (eventConnectTimer) {
      clearTimeout(eventConnectTimer);
      eventConnectTimer = null;
    }
    eventSupportCheckInFlight = true;
    try {
      const capabilityResponse = await fetch(`${basePath}/api/eventSupport`, { cache: 'no-store' });
      const capability = capabilityResponse.ok
        ? await capabilityResponse.json().catch(() => null)
        : null;
      if (!capability || capability.supported == null) {
        scheduleEventConnect(EVENT_RETRY_MS);
        return;
      }
      if (capability.supported !== true) {
        scheduleEventConnect(EVENT_UNSUPPORTED_RECHECK_MS, 'unsupported');
        return;
      }

      const wsUrl = (location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + basePath + '/ws/events';
      const layoutEventChanged = layoutEventTracker.beginConnection();
      reportEventStatus('connecting');
      const socket = new WebSocket(wsUrl);
      eventsWs = socket;
      socket.onopen = () => {
        if (eventsWs !== socket) return;
        wsLive = true;
        reportEventStatus('live');
        if (!document.hidden && !replaying) scheduleRefresh(0, REFRESH_INTENT_EVIDENCE);
      };
      socket.onmessage = (event) => {
        if (eventsWs !== socket) return;
        if (typeof event.data === 'string') {
          try {
            const message = JSON.parse(event.data);
            if (message && message.type === 'layout-diagnostics-delta') {
              applyDiagnosticsDelta(message);
              return;
            }
            if (layoutEventChanged(message))
              layoutPanel?.invalidate('App evidence changed; rescan to review the current layout.');
          } catch (e) { /* not JSON we understand — fall through to a refresh */ }
        }
        if (!document.hidden && !replaying) scheduleRefresh(150, REFRESH_INTENT_EVIDENCE);
      };
      socket.onclose = () => {
        if (eventsWs !== socket) return;
        wsLive = false;
        eventsWs = null;
        scheduleEventConnect(EVENT_RETRY_MS);
      };
      socket.onerror = () => { try { socket.close(); } catch (e) { /* onclose reconnects */ } };
    } catch (e) {
      scheduleEventConnect(EVENT_RETRY_MS, 'unavailable');
    } finally {
      eventSupportCheckInFlight = false;
    }
  }
  connectEvents();
  window.addEventListener('offline', () => {
    wsLive = false;
    reportEventStatus('reconnecting');
    reportDisconnected('browser-offline');
  });
  window.addEventListener('online', () => {
    if (!replaying) scheduleRefresh(0, REFRESH_INTENT_EVIDENCE);
    if (eventsWs?.readyState === WebSocket.OPEN) {
      wsLive = true;
      reportEventStatus('live');
    } else if (!eventsWs || eventsWs.readyState >= WebSocket.CLOSING) {
      connectEvents();
    }
    refreshCapabilities(true);
  });

  // ── Periodic safety refresh for app-side changes (AJAX, no flash) ──
  function pollState() {
    if (!document.hidden && !replaying && !refreshTimer && (!wsLive || !presentation.healthy || isDiagnosticsPaneVisible())) {
      refreshState();
    }
  }
  let pollInterval = setInterval(pollState, 3000);

  document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
      clearInterval(pollInterval);
      pollInterval = null;
    } else if (!pollInterval) {
      if (!replaying) scheduleRefresh(0, REFRESH_INTENT_EVIDENCE);
      pollInterval = setInterval(pollState, 3000);
    }
  });

  // ── Rich property grid ──
  // Right-click an element to open an editable property panel. Values are read via /api/getProperty
  // and live-edited via /api/setProperty — the shared endpoints the canvas and VS Code shells reuse.
  // Curated editable properties per type with bool/number/text/color/enum editors.
  // Enum choices are stable MAUI framework enums, and the agent already converts hex colors
  // (Color.FromArgb) and enum names (Enum.Parse) on setProperty, so these apply with no protocol
  // change.
  const propsPaneEl = document.getElementById('df-props-pane');
  const propsBodyEl = document.getElementById('df-props');
  const propsElLabel = document.getElementById('df-props-el');
  const propsCloseBtn = document.getElementById('df-props-close');
  let propsReturnFocus = null;
  const propertyApi = Object.freeze({
    post: (path, body) => inspectorApi.post(
      path,
      path === '/api/setProperty' ? correlatedPayload(body) : body),
    postDetailed: async (path, body) => {
      const response = await inspectorApi.postDetailed(
        path,
        path === '/api/setProperty' ? correlatedPayload(body) : body);
      if (isFrameCorrelationError(response.body))
        scheduleRefresh(0, REFRESH_INTENT_EVIDENCE);
      return response;
    },
  });
  const propertyGrid = createPropertyGridController({
    pane: propsPaneEl,
    body: propsBodyEl,
    labelElement: propsElLabel,
    closeButton: propsCloseBtn,
    api: propertyApi,
    getIsWriter: () => presentation.driveAllowed,
    prepareOpen: () => {
      const active = document.activeElement;
      if (active instanceof HTMLElement && active !== document.body && !propsPaneEl.contains(active))
        propsReturnFocus = active;
      if (!isTransientPaneLayout()) return;
      if (isTreeDrawerLayout()) setTreeVisible(false);
      closeDock();
    },
    syncPaneChrome,
    setStatus,
    labelFor: elementLabel,
    onOpen: () => {
      if (isTreeDrawerLayout() && propsReturnFocus && propsReturnFocus.classList.contains('df-tree-node'))
        propsPaneEl.focus({ preventScroll: true });
    },
    onClose: () => restoreFocus(propsReturnFocus, tb && tb.tree),
    onRuntimeChange: ({ elementId, name, value }) => {
      if (recordingId) recordStep('setProperty', elById(elementId), { name, value });
      layoutPanel?.invalidate('An app property changed; the previous Layout snapshot is stale.');
      scheduleRefresh(200, REFRESH_INTENT_MUTATION);
    },
  });

  viewport.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    let el = document.elementFromPoint(e.clientX, e.clientY);
    while (el && el !== viewport && !(el.getAttribute && el.getAttribute('data-id'))) el = el.parentElement;
    if (el && el.getAttribute && el.getAttribute('data-id')) propertyGrid.open(el);
  });

  // ── Inspector chrome: interaction mode, hover highlight + badge, element tree (A + B) ──
  // Everything here lives in the SHARED bundle, so the browser, canvas, and VS Code hosts
  // all inherit it. Nothing host-specific belongs in this file.
  const tb = {
    toolbar: document.getElementById('df-toolbar'),
    modes: document.querySelector('#df-toolbar > .df-modes'),
    separator: document.querySelector('#df-toolbar > .df-sep'),
    interact: document.getElementById('df-mode-interact'),
    inspect: document.getElementById('df-mode-inspect'),
    tree: document.getElementById('df-toggle-tree'),
    bounds: document.getElementById('df-toggle-bounds'),
    more: document.getElementById('df-more'),
    secondary: document.getElementById('df-toolbar-secondary'),
    overflow: document.getElementById('df-toolbar-overflow'),
    status: document.getElementById('df-status'),
    takeControl: document.getElementById('df-take-control'),
  };
  const toolbarCommands = COMMAND_DEFINITIONS
    .map(command => document.getElementById(command.id))
    .filter(button => button instanceof HTMLButtonElement);
  const toolbarCommandOrder = COMMAND_DEFINITIONS.map(command => command.id);
  const toolbarActions = tb.secondary ? [...tb.secondary.querySelectorAll(':scope > button')] : [];
  const toolbarPriorities = new Map([
    ['df-goto-checkpoint', 10],
    ['df-assert', 20],
    ['df-toggle-bounds', 30],
    ['df-open-source', 50],
    ['df-toggle-replay', 60],
    ['df-load-flow', 70],
    ['df-send-copilot', 80],
    ['df-toggle-dock', 90],
  ]);
  toolbarActions.forEach((button, index) => {
    button.dataset.toolbarOrder = String(index);
    button.dataset.toolbarPriority = String(toolbarPriorities.get(button.id) || 40);
  });
  for (const button of toolbarCommands) {
    const label = button.getAttribute('aria-label') ||
      button.querySelector('.df-btn-label')?.textContent?.trim() ||
      button.textContent?.trim() ||
      button.id;
    button.dataset.dfCommandName = label;
    button.dataset.dfBaseTitle = button.title || label;
    if (!button.hasAttribute('aria-label')) button.setAttribute('aria-label', label);
  }
  let commandStates = {};
  let commandLayoutSignature = '';
  let toolbarLayoutFrame = 0;
  const treePanel = document.getElementById('df-tree');
  const paneScrim = document.getElementById('df-pane-scrim');
  let hostKind = 'browser';
  let hostLayout = 'wide';
  let dockReturnFocus = null;

  function isFocusableVisible(element) {
    return element instanceof HTMLElement && element.isConnected && element.getClientRects().length > 0;
  }

  function restoreFocus(preferred, fallback) {
    const target = isFocusableVisible(preferred) ? preferred : (isFocusableVisible(fallback) ? fallback : null);
    if (target) target.focus({ preventScroll: true });
  }

  function classifyHostLayout() {
    const width = document.documentElement.clientWidth || window.innerWidth || 1;
    const height = document.documentElement.clientHeight || window.innerHeight || 1;
    if (height < 560) return 'short';
    if (hostKind === 'canvas' && width < 860) return 'narrow';
    if (width < 720) return 'narrow';
    if (width < 1400) return 'compact';
    return 'wide';
  }

  function isTransientPaneLayout() {
    return hostLayout === 'compact' || hostLayout === 'narrow' || hostLayout === 'short';
  }

  function isTreeDrawerLayout() {
    return hostLayout === 'narrow' || hostLayout === 'short';
  }

  function setTreeVisible(visible, restore = false) {
    if (visible && commandStates['df-toggle-tree']?.visible === false)
      visible = false;
    if (visible && isTransientPaneLayout()) {
      setMoreOpen(false);
      if (document.body.classList.contains('df-dock-open')) closeDock();
    }
    document.body.classList.toggle('df-tree-hidden', !visible);
    if (tb.tree) {
      tb.tree.classList.toggle('df-active', visible);
      tb.tree.setAttribute('aria-expanded', String(visible));
    }
    if (visible && isTreeDrawerLayout())
      propertyGrid.close();
    syncPaneChrome();
    if (!visible && restore) restoreFocus(tb.tree);
  }

  function overflowButtons() {
    return tb.overflow
      ? [...tb.overflow.querySelectorAll(':scope > button')].filter(button => !button.hidden)
      : [];
  }

  function moveToolbarAction(container, button) {
    const order = Number(button.dataset.toolbarOrder);
    const before = [...container.children].find(child =>
      Number(child.dataset.toolbarOrder) > order);
    container.insertBefore(button, before || null);
    if (container === tb.overflow) button.setAttribute('role', 'menuitem');
    else button.removeAttribute('role');
  }

  function toolbarFits() {
    return !!tb.toolbar && tb.toolbar.scrollWidth <= tb.toolbar.clientWidth + 1;
  }

  function layoutToolbarActions() {
    toolbarLayoutFrame = 0;
    if (!tb.secondary || !tb.overflow || !tb.more) return;
    const focused = document.activeElement;
    const wasOpen = document.body.classList.contains('df-more-open');
    for (const button of toolbarActions)
      moveToolbarAction(tb.secondary, button);
    tb.secondary.hidden = toolbarActions.every(button => button.hidden);
    tb.more.classList.add('df-hidden');

    if (!toolbarFits()) {
      tb.more.classList.remove('df-hidden');
      while (!toolbarFits()) {
        const candidate = [...tb.secondary.querySelectorAll(':scope > button')]
          .filter(button => !button.hidden)
          .sort((a, b) =>
            Number(a.dataset.toolbarPriority) - Number(b.dataset.toolbarPriority) ||
            Number(b.dataset.toolbarOrder) - Number(a.dataset.toolbarOrder))[0];
        if (!candidate) break;
        moveToolbarAction(tb.overflow, candidate);
      }
    }

    const visibleOverflow = overflowButtons();
    tb.secondary.hidden = ![...tb.secondary.querySelectorAll(':scope > button')]
      .some(button => !button.hidden);
    if (visibleOverflow.length === 0)
      tb.more.classList.add('df-hidden');
    if (wasOpen && visibleOverflow.length > 0)
      setMoreOpen(true);
    else if (visibleOverflow.length === 0)
      setMoreOpen(false);
    if (focused instanceof HTMLElement && toolbarActions.includes(focused)) {
      if (focused.hidden) {
        const fallbackId = resolveCommandFocus(toolbarCommandOrder, commandStates, focused.id);
        const fallback = fallbackId ? document.getElementById(fallbackId) : null;
        restoreFocus(fallback, tb.more);
      } else if (tb.overflow.contains(focused) && !wasOpen) {
        tb.more.focus({ preventScroll: true });
      } else if (focused.isConnected) {
        focused.focus({ preventScroll: true });
      }
    } else if (focused === tb.more && tb.more.classList.contains('df-hidden')) {
      restoreFocus(toolbarCommands.find(button => !button.hidden), null);
    }
    tb.more.classList.toggle('df-active', !!tb.overflow.querySelector('.df-tool-btn.df-active'));
  }

  function scheduleToolbarLayout() {
    if (toolbarLayoutFrame) cancelAnimationFrame(toolbarLayoutFrame);
    toolbarLayoutFrame = requestAnimationFrame(layoutToolbarActions);
  }

  function setMoreOpen(open, focusMenu = false, restore = false) {
    if (!tb.overflow || !tb.more || overflowButtons().length === 0)
      open = false;
    if (!open) {
      closeCopilotMenu();
      tb.overflow?.classList.add('df-hidden');
    } else {
      tb.overflow.classList.remove('df-hidden');
      tb.overflow.style.visibility = 'hidden';
      const anchor = tb.more.getBoundingClientRect();
      const menu = tb.overflow.getBoundingClientRect();
      const left = Math.max(8, Math.min(window.innerWidth - menu.width - 8, anchor.left));
      const below = anchor.bottom + 4;
      const top = below + menu.height <= window.innerHeight - 8
        ? below
        : Math.max(8, anchor.top - menu.height - 4);
      tb.overflow.style.left = `${left}px`;
      tb.overflow.style.top = `${top}px`;
      tb.overflow.style.visibility = '';
    }
    document.body.classList.toggle('df-more-open', !!open);
    if (tb.more) tb.more.setAttribute('aria-expanded', String(!!open));
    if (open && focusMenu) overflowButtons()[0]?.focus({ preventScroll: true });
    if (!open && restore) restoreFocus(tb.more);
  }

  function setDescribedByToken(element, token, present) {
    const values = new Set((element.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean));
    if (present) values.add(token);
    else values.delete(token);
    if (values.size > 0) element.setAttribute('aria-describedby', [...values].join(' '));
    else element.removeAttribute('aria-describedby');
  }

  function commandReasonNode(button) {
    const id = `${button.id}-availability`;
    let node = document.getElementById(id);
    if (!node) {
      node = document.createElement('span');
      node.id = id;
      node.hidden = true;
      document.body.appendChild(node);
    }
    return node;
  }

  function currentCommandInputs() {
    const selected = selectedId ? elById(selectedId) : null;
    return {
      agent: agentCapabilities,
      access: {
        known: leaseStateInitialized,
        connected: presentation.connected,
        healthy: presentation.healthy,
        driveAllowed: presentation.driveAllowed,
        controlAllowed: presentation.controlAllowed,
        driveMessage: presentation.driveMessage,
        isWriter,
        heldByOther: leaseHeldByOther,
        owner: otherLeaseLabel,
      },
      element: {
        selected: !!selected,
        hasSource: selected?.getAttribute('data-hasSource') === 'true',
      },
      workflow: {
        recording: !!recordingId,
        recordingStopping,
        replaying,
        loaded: !!lastMarkdown,
        checkpoint: !!checkpointRoute,
      },
      data: {
        available: !!dockSnapshot && supportsDataContextScope(dockSnapshot.scope),
      },
    };
  }

  function visibleDockTabs() {
    return dockTabButtons.filter(button => !button.hidden);
  }

  function syncCapabilitySurfaces(previousStates) {
    const active = document.activeElement;
    const treeVisible = commandStates['df-toggle-tree']?.visible !== false;
    const treeHadFocus = !!treePanel?.contains(active);
    if (!treeVisible && !document.body.classList.contains('df-tree-hidden')) {
      setTreeVisible(false);
      if (treeHadFocus)
        restoreFocus(toolbarCommands.find(button => !button.hidden), null);
    }

    const boundsVisible = commandStates['df-toggle-bounds']?.visible !== false;
    if (!boundsVisible && document.body.classList.contains('df-show-all')) {
      document.body.classList.remove('df-show-all');
      tb.bounds?.classList.remove('df-active');
      tb.bounds?.setAttribute('aria-pressed', 'false');
    }

    const layoutVisible = commandStates['df-toggle-diagnostics']?.visible !== false;
    const layoutWasVisible = previousStates?.['df-toggle-diagnostics']?.visible === true;
    const layoutTab = document.getElementById('df-tab-layout');
    const layoutHadFocus = active === layoutTab ||
      document.getElementById('df-dock-body')?.contains(active);
    const layoutNeedsFocusRecovery = layoutHadFocus || !active ||
      active === document.body || active === document.documentElement;
    if (layoutTab) layoutTab.hidden = !layoutVisible;
    if (layoutWasVisible && !layoutVisible)
      layoutPanel?.invalidate('Layout diagnostics are no longer supported; the previous scan is stale.');
    if (!layoutVisible && dockActiveTab === 'layout') {
      layoutPanel?.deactivate();
      const fallback = visibleDockTabs()[0];
      if (fallback) {
        loadTab(fallback.getAttribute('data-tab'));
        if (layoutNeedsFocusRecovery) fallback.focus({ preventScroll: true });
      }
    }
  }

  function applyCommandStates(nextStates) {
    const focused = document.activeElement;
    const previousStates = commandStates;
    commandStates = nextStates;

    for (const command of COMMAND_DEFINITIONS) {
      const button = document.getElementById(command.id);
      const state = nextStates[command.id];
      if (!(button instanceof HTMLButtonElement) || !state) continue;

      button.classList.remove('df-hidden');
      button.hidden = !state.visible;
      button.disabled = false;
      button.setAttribute('aria-disabled', String(!state.enabled));
      button.dataset.dfAvailability = state.availability;
      if (state.reason) button.dataset.dfReason = state.reason.code;
      else delete button.dataset.dfReason;

      const reasonNode = commandReasonNode(button);
      reasonNode.textContent = state.visible && state.reason ? state.reason.text : '';
      setDescribedByToken(button, reasonNode.id, !!reasonNode.textContent);
      const baseTitle = button.dataset.dfBaseTitle || button.dataset.dfCommandName || command.name;
      button.title = state.reason ? `${baseTitle} - ${state.reason.text}` : baseTitle;
      for (const mirrorId of command.mirrors || []) {
        const mirror = document.getElementById(mirrorId);
        if (!(mirror instanceof HTMLButtonElement)) continue;
        mirror.hidden = !state.visible;
        mirror.disabled = false;
        mirror.setAttribute('aria-disabled', String(!state.enabled));
        setDescribedByToken(mirror, reasonNode.id, !!reasonNode.textContent);
        mirror.dataset.dfAvailability = state.availability;
        if (state.reason) mirror.dataset.dfReason = state.reason.code;
        else delete mirror.dataset.dfReason;
        mirror.title = state.reason?.text || '';
      }
    }

    const groups = new Set(COMMAND_DEFINITIONS
      .filter(command => nextStates[command.id]?.visible)
      .map(command => command.group));
    if (tb.modes) tb.modes.hidden = !groups.has('modes');
    if (tb.separator) tb.separator.hidden = !groups.has('modes') ||
      !['primary', 'secondary', 'lease'].some(group => groups.has(group));

    const activeMode = mode === 'interact' ? 'df-mode-interact' : 'df-mode-inspect';
    if (nextStates[activeMode]?.visible === false) {
      const fallbackMode = ['df-mode-interact', 'df-mode-inspect']
        .find(id => nextStates[id]?.visible !== false);
      if (fallbackMode)
        setMode(fallbackMode === 'df-mode-interact' ? 'interact' : 'inspect', false);
    }

    syncCapabilitySurfaces(previousStates);

    if (focused instanceof HTMLButtonElement && focused.hidden) {
      const focusedCommand = COMMAND_DEFINITIONS.find(command =>
        command.id === focused.id || command.mirrors?.includes(focused.id));
      if (focusedCommand) {
        const fallbackId = resolveCommandFocus(toolbarCommandOrder, nextStates, focusedCommand.id);
        const fallback = fallbackId ? document.getElementById(fallbackId) : null;
        if (toolbarCommands.includes(focused) && tb.overflow?.contains(focused)
            && !document.body.classList.contains('df-more-open')) {
          restoreFocus(tb.more, fallback);
        } else {
          restoreFocus(fallback, tb.more);
        }
      }
    }

    const signature = JSON.stringify(toolbarCommands.map(button => [
      button.id,
      !button.hidden,
      button.querySelector('.df-btn-label')?.textContent || '',
    ]));
    if (signature !== commandLayoutSignature) {
      commandLayoutSignature = signature;
      scheduleToolbarLayout();
    }
    const viewportControls = document.getElementById('df-viewport-controls');
    if (viewportControls) {
      const hideViewportControls = nextStates['df-toggle-fit']?.visible === false;
      if (hideViewportControls && viewportControls.contains(focused))
        restoreFocus(vpWrap, document.getElementById('df-toggle-fit'));
      viewportControls.hidden = hideViewportControls;
    }
    viewportController?.syncControls();
  }

  function updateCommandStates() {
    applyCommandStates(evaluateCommandStates(currentCommandInputs()));
  }

  function drawAttentionToTakeControl(focus) {
    const takeControl = tb.takeControl;
    if (!takeControl || takeControl.hidden) return;
    takeControl.classList.add('df-attention');
    if (focus) takeControl.focus({ preventScroll: true });
    setTimeout(() => takeControl.classList.remove('df-attention'), 1200);
  }

  tb.toolbar?.addEventListener('click', (event) => {
    const button = event.target.closest('button');
    const state = button ? commandStates[button.id] : null;
    if (!state || state.enabled) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    const message = state.reason?.text || 'This action is currently unavailable.';
    if (state.availability === 'readOnly' || state.availability === 'disconnected' ||
        state.reason?.code === 'preview-not-current') {
      showDriveGateStatus(message);
    } else {
      setStatus(message);
    }
    if (state.availability === 'readOnly') drawAttentionToTakeControl(true);
  }, true);

  function isDiagnosticsPaneVisible() {
    return document.body.classList.contains('df-dock-open') &&
      !document.body.classList.contains('df-dock-collapsed') &&
      document.getElementById('df-tab-layout')?.getAttribute('aria-selected') === 'true';
  }

  function syncPaneChrome() {
    const propsOpen = !!propsPaneEl && !propsPaneEl.classList.contains('df-hidden');
    const treeOpen = !document.body.classList.contains('df-tree-hidden');
    const dockOpen = document.body.classList.contains('df-dock-open');
    // Compact docks Properties beside the screenshot, so only the drawer layouts dim it for them.
    const showScrim =
      (isTreeDrawerLayout() && (treeOpen || propsOpen)) ||
      (hostLayout !== 'wide' && dockOpen);
    document.body.classList.toggle('df-props-open', propsOpen);
    if (paneScrim) paneScrim.classList.toggle('df-hidden', !showScrim);
    const layoutVisible = isDiagnosticsPaneVisible();
    const layoutToggle = document.getElementById('df-toggle-diagnostics');
    layoutToggle?.setAttribute('aria-expanded', String(layoutVisible));
    layoutToggle?.classList.toggle('df-active', layoutVisible);
  }

  function updateHostLayout() {
    const next = classifyHostLayout();
    if (next !== hostLayout || !document.body.dataset.hostLayout) {
      hostLayout = next;
      document.body.dataset.hostLayout = next;
      document.documentElement.dataset.hostLayout = next;
      setMoreOpen(false);
      setTreeVisible(next === 'wide' || next === 'compact');
      scheduleToolbarLayout();
    }
    syncPaneChrome();
  }

  function sanitizeFontFamily(value) {
    if (typeof value !== 'string') return null;
    const font = value.trim();
    if (!font || font.length > 160 || /[;{}<>]|url\(|expression|javascript:/i.test(font))
      return null;
    return font;
  }

  function applyHostProfile(profile) {
    if (!profile || typeof profile !== 'object') return;
    const root = document.documentElement;
    if (typeof profile.surface === 'string' && /^[a-z-]{2,32}$/.test(profile.surface))
      root.dataset.hostSurface = profile.surface;
    root.dataset.hostContrast = profile.contrast === 'high' ? 'high' : 'normal';
    root.dataset.reducedMotion = profile.reducedMotion ? 'true' : 'false';
    const fontProfile = profile.font && typeof profile.font === 'object' ? profile.font : {};
    const font = sanitizeFontFamily(profile.fontFamily || fontProfile.family);
    if (font) root.style.setProperty('--df-font', font);
    const rawFontSize = profile.fontSize != null ? profile.fontSize : fontProfile.size;
    const fontSize = typeof rawFontSize === 'string'
      ? Number(rawFontSize.replace(/px$/i, ''))
      : Number(rawFontSize);
    if (Number.isFinite(fontSize) && fontSize >= 10 && fontSize <= 16)
      root.style.setProperty('--df-font-size', fontSize + 'px');
    const weight = String(profile.fontWeight || fontProfile.weight || '').trim();
    if (/^(normal|bold|[1-9]00)$/.test(weight))
      root.style.setProperty('--df-font-weight', weight);
    scheduleToolbarLayout();
  }

  if (paneScrim) {
    paneScrim.addEventListener('click', () => {
      setMoreOpen(false);
      if (document.body.classList.contains('df-dock-open')) closeDock(true);
      if (propsPaneEl && !propsPaneEl.classList.contains('df-hidden')) propertyGrid.close();
      if (isTreeDrawerLayout()) setTreeVisible(false, true);
    });
  }
  if (tb.more) tb.more.addEventListener('click', (e) => {
    e.stopPropagation();
    setMoreOpen(!document.body.classList.contains('df-more-open'));
  });
  if (tb.more) tb.more.addEventListener('keydown', (e) => {
    if (!['ArrowDown', 'Enter', ' '].includes(e.key)) return;
    e.preventDefault();
    setMoreOpen(true, true);
  });
  if (tb.overflow) {
    tb.overflow.addEventListener('click', (e) => {
      const button = e.target.closest('button');
      if (!button) return;
      if (button === copilotBtn) return;
      setMoreOpen(false, false, true);
    }, true);
    tb.overflow.addEventListener('keydown', (e) => {
      const buttons = overflowButtons();
      const index = buttons.indexOf(document.activeElement);
      let target = null;
      if (e.key === 'ArrowDown') target = buttons[(index + 1 + buttons.length) % buttons.length];
      else if (e.key === 'ArrowUp') target = buttons[(index - 1 + buttons.length) % buttons.length];
      else if (e.key === 'Home') target = buttons[0];
      else if (e.key === 'End') target = buttons[buttons.length - 1];
      else if (e.key === 'Escape') {
        e.preventDefault();
        setMoreOpen(false, false, true);
        return;
      } else return;
      if (!target) return;
      e.preventDefault();
      target.focus({ preventScroll: true });
    });
    if (window.MutationObserver && tb.more) {
      new MutationObserver(() => {
        tb.more.classList.toggle('df-active', !!tb.overflow.querySelector('.df-tool-btn.df-active'));
      }).observe(document.getElementById('df-toolbar'), { subtree: true, attributes: true, attributeFilter: ['class'] });
    }
  }
  document.addEventListener('pointerdown', (e) => {
    if (!document.body.classList.contains('df-more-open')) return;
    if (tb.overflow && tb.overflow.contains(e.target)) return;
    if (copilotMenu && copilotMenu.contains(e.target)) return;
    if (tb.more && tb.more.contains(e.target)) return;
    setMoreOpen(false);
  });

  function elById(id) {
    return id
      ? [...viewport.querySelectorAll('.devflow-element')]
        .find((element) => element.getAttribute('data-id') === id)
      : null;
  }
  function repeatedElementContext(el, automationId) {
    if (!automationId) return '';
    const matching = [...viewport.querySelectorAll('.devflow-element')]
      .filter((candidate) => candidate.getAttribute('data-automationId') === automationId);
    if (matching.length < 2) return '';
    const ownText = el.getAttribute('data-text') || '';
    if (/label/i.test(el.getAttribute('data-type') || '') && ownText && ownText !== automationId)
      return ownText;
    const parentId = el.getAttribute('data-parentId');
    const siblingLabel = [...viewport.querySelectorAll('.devflow-element')].find((candidate) =>
      candidate.getAttribute('data-parentId') === parentId &&
      /label/i.test(candidate.getAttribute('data-type') || '') &&
      candidate.getAttribute('data-text'));
    return siblingLabel?.getAttribute('data-text') || (ownText !== automationId ? ownText : '');
  }
  function elementLabel(el) {
    const type = el.getAttribute('data-type') || 'Element';
    const automationId = el.getAttribute('data-automationId') || '';
    const name = automationId || el.getAttribute('data-text') || '';
    const context = repeatedElementContext(el, automationId);
    return name ? (type + ' · ' + name + (context ? ` · “${context}”` : '')) : type;
  }
  const elementTree = createElementTreeController({
    treePanel,
    viewport,
    countElement: document.getElementById('df-tree-count'),
    getSelectedId: () => selectedId,
    onSelect: (id) => selectElement(id),
    onHover: (id) => setHover(id ? elById(id) : null),
  });

  function setMode(next, announce = true) {
    setHover(null);
    mode = next;
    tb.interact.classList.toggle('df-active', next === 'interact');
    tb.inspect.classList.toggle('df-active', next === 'inspect');
    tb.interact.setAttribute('aria-checked', String(next === 'interact'));
    tb.inspect.setAttribute('aria-checked', String(next === 'inspect'));
    tb.interact.tabIndex = next === 'interact' ? 0 : -1;
    tb.inspect.tabIndex = next === 'inspect' ? 0 : -1;
    document.body.classList.toggle('df-mode-inspect', next === 'inspect');
    if (!announce) return;
    if (next === 'inspect') {
      setStatus('Inspect mode — click selects an element');
    } else {
      hideHitCandidates();
      const selected = selectedId ? elById(selectedId) : null;
      setStatus(selected ? `Selected: ${elementLabel(selected)}. Alt or Shift click to inspect.` : '');
    }
  }

  // ── Hover highlight + size badge ──
  function ensureBadge() {
    if (badgeEl) return badgeEl;
    badgeEl = document.createElement('div');
    badgeEl.id = 'df-badge';
    viewport.appendChild(badgeEl);
    return badgeEl;
  }
  function setHover(el) {
    // Off-screen overlays aren't drawn, so a hover box/badge for one would only mislead.
    if (el?.dataset.offscreen === 'true') el = null;
    if (hoveredEl === el) return;
    if (hoveredEl) hoveredEl.classList.remove('df-hover', 'df-hover-noninteractive', 'df-hover-interactive');
    hoveredEl = el;
    if (!el) { if (badgeEl) badgeEl.style.display = 'none'; return; }
    el.classList.add('df-hover');
    const interactable = isInteractableOverlay(el);
    const tapTarget = isTapTargetOverlay(el);
    el.classList.toggle('df-hover-noninteractive', !interactable);
    el.classList.toggle('df-hover-interactive', tapTarget);
    const b = ensureBadge();
    b.classList.toggle('df-badge-noninteractive', !interactable);
    const kind = tapTarget
      ? 'Interactive element'
      : (interactable ? 'Scrollable element' : 'Visual element');
    b.dataset.kind = tapTarget ? 'interactive' : (interactable ? 'scrollable' : 'visual');
    const w = Math.round(parseFloat(el.style.width) || el.offsetWidth);
    const h = Math.round(parseFloat(el.style.height) || el.offsetHeight);
    b.textContent = `${kind}: ${el.getAttribute('data-type') || 'Element'} ${w}×${h}`;
    b.style.display = 'block';
    const viewportWidth = parseFloat(viewport.dataset.width) || viewport.offsetWidth || 0;
    const viewportHeight = parseFloat(viewport.dataset.height) || viewport.offsetHeight || 0;
    const maxLeft = Math.max(0, viewportWidth - b.offsetWidth);
    const maxTop = Math.max(0, viewportHeight - b.offsetHeight);
    b.style.left = Math.min(Math.max(0, el.offsetLeft), maxLeft) + 'px';
    b.style.top = Math.min(Math.max(0, el.offsetTop - b.offsetHeight - 2), maxTop) + 'px';
  }
  function isInteractableOverlay(el) {
    if (!el || !el.classList || !el.classList.contains('devflow-element')) return false;
    return el.dataset.interactable === 'true';
  }
  function isTapTargetOverlay(el) {
    if (!isInteractableOverlay(el)) return false;
    return (el.dataset.traits || '').split(',')
      .some((trait) => trait.trim().toLowerCase() === 'interactive');
  }
  function hoverHitTest(clientX, clientY) {
    const node = document.elementFromPoint(clientX, clientY);
    return node && node.closest ? node.closest('.devflow-element') : null;
  }
  viewport.addEventListener('pointermove', (e) => {
    if (isGesturing || activeEditor) return;
    setHover(hoverHitTest(e.clientX, e.clientY));
  });
  viewport.addEventListener('pointerleave', () => setHover(null));

  // ── Selection: screenshot ↔ tree ↔ property grid ──
  // Status last written for the selection. A refresh rewrites the line only while it still shows
  // this, so messages from other actions (tap, recording, mode) are never overwritten.
  let selectionStatus = '';
  let selectionStatusToken = null;
  function selectionStatusFor(el) {
    return elementLabel(el) + (el.dataset.offscreen === 'true' ? ' (off-screen)' : '');
  }
  function selectElement(id) {
    viewport.querySelectorAll('.devflow-element.df-selected').forEach((el) => el.classList.remove('df-selected'));
    const previousId = selectedId;
    selectedId = id || null;
    if (previousId !== selectedId) layoutPanel?.selectionChanged();
    if (!id) {
      propertyGrid.close();
      elementTree.updateSelection();
      if (selectionStatusToken) statusChannel.clear(selectionStatusToken);
      selectionStatus = '';
      selectionStatusToken = null;
      updateHostButtons();
      updateFlowButtons();
      postSelectionToHost(null);
      return;
    }
    const el = elById(id);
    if (el) el.classList.add('df-selected');
    elementTree.updateSelection();
    elementTree.reveal(id);
    if (el) {
      propertyGrid.open(el);
      selectionStatus = selectionStatusFor(el);
      selectionStatusToken = setStatus(selectionStatus);
    }
    updateHostButtons();
    updateFlowButtons();
    postSelectionToHost(el);
  }

  // Called after every patchElements(): rebuild the tree only on structural change,
  // and re-apply the selection highlight to the (possibly replaced) overlay div.
  function onElementsUpdated() {
    elementTree.syncStructure();
    if (selectedId) {
      const el = elById(selectedId);
      if (el) {
        el.classList.add('df-selected');
        const status = selectionStatusFor(el);
        if (status !== selectionStatus && selectionStatusToken && statusChannel.isCurrent(selectionStatusToken)) {
          selectionStatus = status;
          statusChannel.replace(selectionStatusToken, status);
        }
      } else selectElement(null);
    }
    if (hoveredEl && (!hoveredEl.isConnected || hoveredEl.dataset.offscreen === 'true')) setHover(null);
  }

  // ── Workflow recording: capture interactions into the shared Flow format, then
  // download a replayable Markdown test. Reuses the broker's /api/flows/record/* endpoints, which
  // feed the same FlowRecorder/FlowMarkdown engine as the MCP maui_flow_record_* tools. ──
  const recordBtn = document.getElementById('df-toggle-record');
  const assertBtn = document.getElementById('df-assert');

  // ── Timeline: a live strip of recorded step chips (shown while recording; each chip reselects its element) ──
  const timelineEl = document.getElementById('df-timeline');
  const timelineStepsEl = document.getElementById('df-timeline-steps');
  const timelineMetaEl = document.getElementById('df-timeline-meta');
  const timelineTitleText = document.getElementById('df-timeline-title-text');
  const cancelRecordingBtn = document.getElementById('df-record-cancel');
  const timelineCloseBtn = document.getElementById('df-timeline-close');
  const loadFlowBtn = document.getElementById('df-load-flow');
  const workflowSelect = document.getElementById('df-workflow-select');
  const workflowFileBtn = document.getElementById('df-workflow-file');
  const workflowFileInput = document.getElementById('df-workflow-file-input');
  const workflowReplayBtn = document.getElementById('df-workflow-replay');
  const workflowPanelClasses = ['df-tl-recording', 'df-tl-done', 'df-tl-replay-ok', 'df-tl-replay-failed'];
  function showWorkflowPanel() {
    if (!timelineEl) return;
    if (isTransientPaneLayout()) {
      propertyGrid.close();
      if (isTreeDrawerLayout() && !document.body.classList.contains('df-tree-hidden'))
        setTreeVisible(false);
      if (document.body.classList.contains('df-dock-open'))
        closeDock();
    }
    timelineEl.classList.remove('df-hidden');
    document.body.classList.add('df-timeline-open');
  }
  function setWorkflowPanelState(state) {
    if (!timelineEl) return;
    timelineEl.classList.remove(...workflowPanelClasses);
    if (state) timelineEl.classList.add(state);
  }
  function dismissTimeline() {
    if (timelineEl) timelineEl.classList.add('df-hidden');
    document.body.classList.remove('df-timeline-open');
  }
  if (timelineCloseBtn) timelineCloseBtn.addEventListener('click', dismissTimeline);
  function timelineStart() {
    if (!timelineEl) return;
    if (timelineStepsEl) timelineStepsEl.replaceChildren();
    if (timelineStepsEl) timelineStepsEl.dataset.emptyMessage = 'Interact with the app — recorded steps appear here.';
    if (timelineMetaEl) timelineMetaEl.textContent = '';
    if (timelineTitleText) timelineTitleText.textContent = 'Workflow · Recording';
    setWorkflowPanelState('df-tl-recording');
    showWorkflowPanel();
  }
  function timelineStepLabel(action, el, extra) {
    if (action === 'assert' && extra && extra.assertsJson) {
      try {
        const a = JSON.parse(extra.assertsJson)[0]; const s = a.selector || {};
        const name = s.automationId || s.text || s.id || 'element';
        return a.kind === 'propEquals' ? (name + '.' + a.name + ' = ' + String(a.expected).slice(0, 18)) : (name + ' exists');
      } catch (e) { return ''; }
    }
    const base = el ? elementLabel(el) : '';
    if (action === 'scroll') { const d = extra || {}; return (d.dx || d.dy) ? ('\u0394 ' + Math.round(d.dx || 0) + ', ' + Math.round(d.dy || 0)) : ''; }
    if (extra && extra.value != null) return base + ' = ' + String(extra.value).slice(0, 18);
    if (extra && extra.text != null) return base + ' = ' + String(extra.text).slice(0, 18);
    return base;
  }
  function timelineAdd(seq, action, el, extra) {
    if (!timelineEl || timelineEl.classList.contains('df-hidden') || !timelineStepsEl) return;
    const chip = document.createElement('button');
    chip.className = 'df-tl-step';
    const id = el && el.getAttribute && el.getAttribute('data-id');
    const seqEl = document.createElement('span'); seqEl.className = 'df-tl-seq'; seqEl.textContent = String(seq || (timelineStepsEl.children.length + 1));
    const actEl = document.createElement('span'); actEl.className = 'df-tl-act'; actEl.textContent = action;
    const lblEl = document.createElement('span'); lblEl.className = 'df-tl-label'; lblEl.textContent = timelineStepLabel(action, el, extra);
    chip.append(seqEl, actEl, lblEl);
    if (id) chip.addEventListener('click', () => selectElement(id));
    timelineStepsEl.appendChild(chip);
    chip.scrollIntoView({ inline: 'end', block: 'nearest' });
  }
  function timelineStop(steps) {
    if (!timelineEl) return;
    setWorkflowPanelState('df-tl-done');
    if (timelineTitleText) timelineTitleText.textContent = 'Workflow';
    if (timelineMetaEl) {
      const count = steps ? `${steps} step${steps === 1 ? '' : 's'}` : '';
      timelineMetaEl.textContent = [lastMarkdownName, count].filter(Boolean).join(' · ');
    }
    showWorkflowPanel();
  }

  function updateRecordButton() {
    if (!recordBtn) return;
    const wasRecording = recordBtn.classList.contains('df-recording');
    const previousLabel = recordBtn.querySelector('.df-btn-label')?.textContent
      || recordBtn.textContent;
    const cancelWasHidden = cancelRecordingBtn?.classList.contains('df-hidden');
    recordBtn.classList.toggle('df-recording', !!recordingId);
    recordBtn.setAttribute('aria-pressed', String(!!recordingId));
    // Update only the label span so the leading icon and the responsive icon-collapse survive
    // (setting textContent here would wipe the .df-btn-label wrapper the toolbar relies on).
    const lbl = recordBtn.querySelector('.df-btn-label');
    const label = recordingStopping ? 'Stopping…' : (recordingId ? `Rec (${recStepCount})` : 'Record');
    if (lbl) lbl.textContent = label;
    else recordBtn.textContent = `\u25CF ${label}`;
    recordBtn.setAttribute('aria-label', 'Record workflow');
    recordBtn.dataset.dfStartTitle ||= recordBtn.dataset.dfBaseTitle || recordBtn.title;
    recordBtn.dataset.dfBaseTitle = recordingId
      ? 'Stop the active recording and save its replayable workflow'
      : recordBtn.dataset.dfStartTitle;
    if (cancelRecordingBtn) cancelRecordingBtn.classList.toggle('df-hidden', !recordingId);
    if (wasRecording !== !!recordingId
        || previousLabel !== (lbl?.textContent || recordBtn.textContent)
        || cancelWasHidden !== cancelRecordingBtn?.classList.contains('df-hidden')) {
      scheduleToolbarLayout();
    }
  }

  // Highest-precedence DURABLE selector for the element (automationId > text > id). We never send a
  // bare type (a type-only selector would need a real, server-ordered index to replay).
  function selectorPayload(el) {
    if (!el) return {};
    const automationId = el.getAttribute('data-automationId');
    if (automationId) return { automationId };
    const text = el.getAttribute('data-text');
    if (text) return { text };
    const id = el.getAttribute('data-id');
    return id ? { id } : {};
  }

  async function recordStep(action, el, extra) {
    if (!recordingId) return;
    const body = Object.assign({ recordingId, action }, selectorPayload(el), extra || {});
    try {
      const r = await fetch(`${basePath}/api/flows/record/step`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
      });
      const j = r.ok ? await r.json().catch(() => null) : null;
      if (j && j.ok) { recStepCount = j.stepCount; updateRecordButton(); timelineAdd(j.stepCount, action, el, extra); }
    } catch (err) { console.error('record step failed:', err); }
  }

  function recordStepById(action, elementId, extra) {
    recordStep(action, elById(elementId), extra);
  }

  async function startRecording() {
    const name = 'recording-' + new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    try {
      const r = await fetch(`${basePath}/api/flows/record/start`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name }),
      });
      const j = r.ok ? await r.json().catch(() => null) : null;
      if (j && j.ok) {
        recordingId = j.recordingId; recStepCount = 0; recName = j.name;
        if (j.route) { checkpointRoute = j.route; checkpointLabel = 'recording start'; }
        updateFlowButtons();
        timelineStart();
        setStatus('Recording — interact with the app; each action is captured.');
      } else {
        setStatus('Could not start recording.');
      }
    } catch (err) { setStatus('Could not start recording.'); }
  }

  async function stopRecording(reason) {
    if (!recordingId || recordingStopping) return;
    const id = recordingId;
    const pre = reason ? reason + ' ' : '';   // optional prefix, e.g. when a lost writer lease forces the stop
    recordingStopping = true;
    updateFlowButtons();
    setStatus(pre + 'Stopping recording…');
    try {
      const r = await fetch(`${basePath}/api/flows/record/stop`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ recordingId: id }),
      });
      const j = await r.json().catch(() => null);
      if (!r.ok || !j || !j.ok) {
        const detail = (j && j.error) || `request failed (${r.status})`;
        if (/no recording is active|active recording no longer exists|unknown recordingid/i.test(detail)) {
          recordingId = null;
          recStepCount = 0;
          recName = null;
          dismissTimeline();
          setStatus(`${pre}Recording already ended in another session.`);
          updateHostButtons();
          return;
        }
        setStatus(`${pre}Could not stop recording: ${detail} Recording is still active; retry Stop.`);
        return;
      }

      recordingId = null;
      if (j.markdown) {
        lastMarkdown = j.markdown;
        const fname = (j.name || recName || 'recording') + '.md';
        lastMarkdownName = fname;
        lastMarkdownSource = 'recording';
        timelineStop(j.steps);
        // Host-side save when a host advertises it (VS Code / canvas know workspace conventions);
        // otherwise download in the browser. Gated by the authenticated host bridge.
        const completedRecording = { name: j.name, steps: j.steps, markdown: j.markdown, prefix: pre };
        if (hostHas('saveRecording') && postToHost('devflow:recordingComplete', completedRecording)) {
          setStatus(`${pre}Recorded ${j.steps} step(s) — handed to the host to save. Replay is now available.`);
        } else if (framed && hostCaps === null && !hostHandshakeExpired) {
          pendingHostRecordings.push(completedRecording);
          setStatus(`${pre}Recorded ${j.steps} step(s) — waiting briefly for the host to save it. Replay is now available.`);
        } else {
          downloadText(fname, j.markdown);
          setStatus(`${pre}Recorded ${j.steps} step(s) → ${fname}. Replay is now available.`);
        }
      } else {
        dismissTimeline();
        setStatus(pre + 'Recording stopped: no replayable steps.');
      }
      updateHostButtons();   // a completed recording enables Send-to-Copilot even with no selection
    } catch (err) {
      setStatus(`${pre}Could not stop recording. Recording is still active; retry Stop.`);
    } finally {
      recordingStopping = false;
      updateFlowButtons();
    }
  }

  async function syncRecordingStatus() {
    try {
      const response = await fetch(`${basePath}/api/flows/record/status`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}',
      });
      const status = response.ok ? await response.json().catch(() => null) : null;
      if (!status || !status.ok) return;

      if (status.recording) {
        const discovered = !recordingId;
        recordingId = status.recordingId || recordingId;
        recStepCount = Number(status.steps) || 0;
        recName = status.name || recName;
        if (discovered) timelineStart();
      } else if (recordingId) {
        recordingId = null;
        recordingStopping = false;
        dismissTimeline();
        setStatus('Recording ended in another session.');
      }
      updateFlowButtons();
      updateHostButtons();
    } catch {
      // The regular connection state handles transport failures; keep the local draft visible.
    }
  }

  async function cancelRecording() {
    if (!recordingId || recordingStopping) return;
    const id = recordingId;
    const confirmed = await confirmModal(
      'Discard this recording? Recorded steps will be removed without saving or replaying them.',
      'Discard');
    if (!confirmed) return;

    recordingStopping = true;
    updateFlowButtons();
    setStatus('Discarding recording…');
    try {
      const response = await fetch(`${basePath}/api/flows/record/cancel`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ recordingId: id }),
      });
      const result = await response.json().catch(() => null);
      if (!response.ok || !result || !result.ok) {
        const detail = (result && result.error) || `request failed (${response.status})`;
        setStatus(`Could not discard recording: ${detail} Recording is still active.`);
        return;
      }

      recordingId = null;
      recStepCount = 0;
      recName = null;
      dismissTimeline();
      setStatus('Recording discarded.');
      updateHostButtons();
    } catch {
      setStatus('Could not discard recording. Recording is still active.');
    } finally {
      recordingStopping = false;
      updateFlowButtons();
    }
  }

  function downloadText(filename, text) {
    try {
      const blob = new Blob([text], { type: 'text/markdown' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url; a.download = filename;
      document.body.appendChild(a); a.click(); a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (err) { console.error('download failed:', err); }
  }

  function setLoadedWorkflow(markdown, name, source, steps) {
    if (typeof markdown !== 'string' || !markdown.trim()) return false;
    lastMarkdown = markdown;
    lastMarkdownName = String(name || 'workflow.md');
    lastMarkdownSource = source || 'file';
    if (timelineStepsEl) {
      timelineStepsEl.replaceChildren();
      timelineStepsEl.dataset.emptyMessage = `Ready to replay ${lastMarkdownName}.`;
    }
    if (timelineTitleText) timelineTitleText.textContent = 'Workflow';
    if (timelineMetaEl) {
      const count = Number.isFinite(Number(steps))
        ? `${Number(steps)} step${Number(steps) === 1 ? '' : 's'}`
        : null;
      timelineMetaEl.textContent = [lastMarkdownName, count].filter(Boolean).join(' · ');
    }
    setWorkflowPanelState('df-tl-done');
    showWorkflowPanel();
    updateFlowButtons();
    updateHostButtons();
    return true;
  }

  async function refreshProjectWorkflows() {
    if (!workflowSelect) return;
    workflowSelect.disabled = true;
    workflowSelect.replaceChildren(new Option('Loading project tests…', ''));
    const result = await apiPost('/api/flows/files/list', {});
    workflowSelect.replaceChildren();
    workflowSelect.append(new Option('Project tests…', ''));
    if (!result || result.ok !== true) {
      workflowSelect.append(new Option('Could not list project tests', ''));
      workflowSelect.disabled = commandStates['df-load-flow']?.enabled === false;
      return;
    }
    if (result.supported === false) {
      workflowSelect.append(new Option('Project unavailable — choose a file', ''));
      workflowSelect.disabled = commandStates['df-load-flow']?.enabled === false;
      return;
    }
    for (const test of (result.tests || [])) {
      if (!test || typeof test.name !== 'string') continue;
      const option = new Option(test.name, test.name);
      option.title = test.modifiedAt ? `Modified ${new Date(test.modifiedAt).toLocaleString()}` : test.name;
      workflowSelect.append(option);
    }
    if (workflowSelect.options.length === 1)
      workflowSelect.append(new Option('No tests in maui-tests', ''));
    workflowSelect.disabled = commandStates['df-load-flow']?.enabled === false;
  }

  async function loadProjectWorkflow(name) {
    if (!name) return;
    setStatus(`Loading ${name}…`);
    const result = await apiPost('/api/flows/files/load', { name });
    if (!result || result.ok !== true || typeof result.markdown !== 'string') {
      setStatus((result && result.error) || 'Could not load the selected project workflow.');
      return;
    }
    setLoadedWorkflow(result.markdown, result.name || name, 'project', result.steps);
    setStatus(`Loaded ${result.name || name} from the project.`);
  }

  async function loadWorkflowFile(file) {
    if (!file) return;
    if (!/\.md$/i.test(file.name || '') || file.size > 1024 * 1024) {
      setStatus('Choose a Markdown workflow file smaller than 1 MB.');
      return;
    }
    try {
      const markdown = await file.text();
      if (!setLoadedWorkflow(markdown, file.name, 'file', null)) {
        setStatus('The selected workflow file is empty.');
        return;
      }
      setStatus(`Loaded ${file.name}. Replay validates it before driving the app.`);
    } catch {
      setStatus('Could not read the selected workflow file.');
    }
  }

  async function chooseWorkflowFile() {
    if (hostHas('workflowFilePicker')) {
      const result = await requestHost('devflow:pickWorkflow', {}, 300000);
      if (result && result.ok && typeof result.markdown === 'string') {
        setLoadedWorkflow(result.markdown, result.name || 'workflow.md', 'file', result.steps);
        setStatus(`Loaded ${result.name || 'workflow.md'}.`);
      } else if (result && result.error) {
        setStatus(result.error);
      }
      return;
    }
    workflowFileInput?.click();
  }

  async function openWorkflowPicker() {
    showWorkflowPanel();
    if (timelineTitleText) timelineTitleText.textContent = 'Workflow';
    if (timelineStepsEl && !lastMarkdown) {
      timelineStepsEl.replaceChildren();
      timelineStepsEl.dataset.emptyMessage = 'Choose a project test or Markdown file to replay.';
    }
    await refreshProjectWorkflows();
  }

  if (recordBtn) {
    recordBtn.addEventListener('click', () => { if (recordingId) stopRecording(); else startRecording(); });
  }
  if (cancelRecordingBtn) cancelRecordingBtn.addEventListener('click', cancelRecording);
  if (loadFlowBtn) loadFlowBtn.addEventListener('click', openWorkflowPicker);
  if (workflowSelect) workflowSelect.addEventListener('change', () => loadProjectWorkflow(workflowSelect.value));
  if (workflowFileBtn) workflowFileBtn.addEventListener('click', chooseWorkflowFile);
  if (workflowFileInput) workflowFileInput.addEventListener('change', async () => {
    const file = workflowFileInput.files && workflowFileInput.files[0];
    workflowFileInput.value = '';
    await loadWorkflowFile(file);
  });

  // ── Replay the recorded test + return-to-start-route (checkpoint) ──
  const replayBtn = document.getElementById('df-toggle-replay');
  const checkpointBtn = document.getElementById('df-goto-checkpoint');

  function updateFlowButtons() {
    updateRecordButton();
    updateCommandStates();
    const enabled = id => commandStates[id]?.enabled !== false;
    if (cancelRecordingBtn) {
      cancelRecordingBtn.disabled =
        !recordingId || recordingStopping || replaying || !presentation.controlAllowed;
    }
    if (workflowSelect) workflowSelect.disabled = !enabled('df-load-flow');
    if (workflowFileBtn) workflowFileBtn.disabled = !enabled('df-load-flow');
    propertyGrid.updateWriterState();
    document.querySelectorAll('#df-dock .df-alert-actions button').forEach((button) => {
      button.disabled = !presentation.driveAllowed || button.dataset.pending === 'true';
    });
  }

  // While recording, capture an assertion on the selected element so the .md test VALIDATES (not
  // just reproduces): prefer "Text == <current value>" (propEquals), else "element exists". The
  // assertion carries its own durable selector and rides on a validation-only "assert" step.
  async function recordAssert() {
    if (!recordingId || !selectedId) return;
    const el = elById(selectedId);
    const sel = selectorPayload(el);
    if (!sel.automationId && !sel.text && !sel.id) { setStatus('Cannot assert: element has no durable selector (add an AutomationId).'); return; }
    let assert;
    const res = await apiPost('/api/getProperty', { elementId: selectedId, name: 'Text' });
    const text = res && res.value;
    if (text != null && String(text).length > 0) assert = { kind: 'propEquals', selector: sel, name: 'Text', expected: String(text), verify: true };
    else assert = { kind: 'exists', selector: sel, verify: true };
    await recordStep('assert', null, { assertsJson: JSON.stringify([assert]) });
    setStatus(assert.kind === 'propEquals' ? `Asserted Text == "${assert.expected}"` : 'Asserted element is present');
  }
  if (assertBtn) assertBtn.addEventListener('click', recordAssert);

  function setReplayUi(on) {
    replaying = on;
    document.body.classList.toggle('df-replaying', on);
    updateFlowButtons();
    // Pause the 3s poll while replaying so the screenshot doesn't churn under the driven app.
    if (on && pollInterval) { clearInterval(pollInterval); pollInterval = null; }
    else if (!on && !pollInterval) { pollInterval = setInterval(pollState, 3000); }
  }

  async function captureCheckpoint(label) {
    try {
      const r = await fetch(`${basePath}/api/checkpoint`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}',
      });
      const j = r.ok ? await r.json().catch(() => null) : null;
      if (j && j.ok && j.route) { checkpointRoute = j.route; checkpointLabel = label; updateFlowButtons(); }
    } catch (err) { /* best-effort */ }
  }

  async function gotoCheckpoint() {
    if (!checkpointRoute || replaying) return;
    try {
      const r = await fetch(`${basePath}/api/navigate`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ route: checkpointRoute }),
      });
      setStatus(r.ok
        ? `Returned to ${checkpointLabel || 'start'} (${checkpointRoute}). Navigation only — app data is not reset.`
        : 'Could not navigate to the checkpoint route.');
      scheduleRefresh(500, r.ok ? REFRESH_INTENT_MUTATION : REFRESH_INTENT_RETRY);
    } catch (err) { setStatus('Navigate failed.'); }
  }

  async function replay() {
    const canReplay = () => commandStates['df-toggle-replay']?.enabled !== false;
    const explainReplay = () => setStatus(
      commandStates['df-toggle-replay']?.reason?.text || 'Replay is currently unavailable.');
    if (!canReplay()) {
      explainReplay();
      return;
    }
    if (!lastMarkdown || recordingId || replaying) return;
    const workflowLabel = lastMarkdownName ? ` “${lastMarkdownName}”` : '';
    if (!(await confirmModal(`Replay${workflowLabel} will drive the LIVE app and may change its data. Continue?`, 'Replay'))) return;
    updateCommandStates();
    if (!canReplay()) {
      explainReplay();
      return;
    }
    // Auto-capture a "before replay" checkpoint so you can return to where you were.
    await captureCheckpoint('before replay');
    updateCommandStates();
    if (!canReplay()) {
      explainReplay();
      return;
    }
    setReplayUi(true);
    setStatus('Replaying…');
    try {
      const r = await fetch(`${basePath}/api/flows/replay`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ markdown: lastMarkdown }),
      });
      const rep = await r.json().catch(() => null);
      if (r.ok && rep) {
        showReplayReport(rep);
      } else {
        const validation = Array.isArray(rep && rep.errors) ? rep.errors.join('; ') : null;
        const error = validation || (rep && rep.error);
        setStatus(error || (r.status === 409
          ? 'Replay is busy — try again shortly.'
          : `Replay failed (HTTP ${r.status}).`));
      }
    } catch (err) { setStatus('Replay failed.'); }
    finally { setReplayUi(false); scheduleRefresh(400, REFRESH_INTENT_MUTATION); }
  }

  function showReplayReport(rep) {
    showWorkflowPanel();
    setWorkflowPanelState(rep.ok ? 'df-tl-replay-ok' : 'df-tl-replay-failed');
    if (timelineTitleText)
      timelineTitleText.textContent = rep.ok ? 'Workflow · Replay passed' : 'Workflow · Replay failed';
    if (timelineMetaEl)
      timelineMetaEl.textContent = `${rep.passed || 0}/${rep.total || 0} passed`;
    if (timelineStepsEl) {
      timelineStepsEl.replaceChildren();
      timelineStepsEl.dataset.emptyMessage = rep.error || 'Replay returned no step results.';
    }
    if (rep.error) {
      setStatus(rep.error);
    }
    for (const s of (rep.results || [])) {
      const chip = document.createElement('div');
      chip.className = `df-tl-step ${s.ok ? 'df-step-passed' : 'df-step-failed'}`;
      const seq = document.createElement('span');
      seq.className = 'df-tl-seq';
      seq.textContent = String(s.seq || '?');
      const action = document.createElement('span');
      action.className = 'df-tl-act';
      action.textContent = s.ok ? 'Passed' : 'Failed';
      const label = document.createElement('span');
      label.className = 'df-tl-label';
      label.textContent = s.label || s.action || 'step';
      chip.append(seq, action, label);
      if (s.error) chip.title = s.error;
      timelineStepsEl?.append(chip);
    }
    setStatus(rep.ok ? `Replay passed ${rep.passed}/${rep.total}.` : `Replay: ${rep.failed} step(s) did not pass.`);
  }

  if (replayBtn) replayBtn.addEventListener('click', replay);
  if (workflowReplayBtn) workflowReplayBtn.addEventListener('click', replay);
  if (checkpointBtn) checkpointBtn.addEventListener('click', gotoCheckpoint);

  // ── Host bridge: send-to-Copilot + open-XAML-source over a nonce-authenticated
  // iframe->host channel. The host (VS Code webview / canvas shell) advertises capabilities; a plain
  // browser tab has no host and uses clipboard fallbacks. The bridge nonce arrives in the URL
  // fragment (never sent to the broker) and gates every message in both directions. ──
  const framed = window.parent && window.parent !== window;
  const bridgeId = (location.hash.match(/devflowBridge=([A-Za-z0-9_-]+)/) || [])[1] || null;
  let hostCaps = null;
  const copilotBtn = document.getElementById('df-send-copilot');
  const copilotMenu = document.getElementById('df-copilot-menu');
  const copilotMenuItems = copilotMenu
    ? [...copilotMenu.querySelectorAll('[data-copilot-context]')]
    : [];
  const sourceBtn = document.getElementById('df-open-source');
  const attachDataBtn = document.getElementById('df-attach-data');
  const pendingHostRecordings = [];
  let hostHandshakeExpired = false;
  let dockSnapshot = null;
  let dockActiveTab = null;
  const pendingHostRequests = new Map();
  let hostRequestSequence = 0;

  function postToHost(type, data) {
    if (!framed || !bridgeId) return false;
    window.parent.postMessage(Object.assign({ v: 1, bridgeId, type }, data || {}), '*');
    return true;
  }

  function downloadPendingHostRecordings() {
    while (pendingHostRecordings.length > 0) {
      const recording = pendingHostRecordings.shift();
      const name = `${recording.name || 'recording'}.md`;
      downloadText(name, recording.markdown);
      setStatus(`${recording.prefix || ''}Recorded ${recording.steps} step(s) → ${name}. Replay is now available.`);
    }
  }
  function requestHost(type, data, timeoutMs) {
    if (!framed || !bridgeId) return Promise.resolve({ ok: false, error: 'No compatible host is available.' });
    const requestId = `h${Date.now().toString(36)}-${(++hostRequestSequence).toString(36)}`;
    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        pendingHostRequests.delete(requestId);
        resolve({ ok: false, error: 'The host did not confirm the context attachment.' });
      }, timeoutMs || 10000);
      pendingHostRequests.set(requestId, { resolve, timer });
      postToHost(type, Object.assign({ requestId }, data || {}));
    });
  }
  function hostHas(cap) { return !!hostCaps && hostCaps.indexOf(cap) !== -1; }

  // Compact, durable element context shared with the host (Copilot). Everything the agent needs to
  // resolve "the selected element" without a screenshot.
  function elementInfo(el) {
    if (!el) return null;
    return {
      id: el.getAttribute('data-id') || null,
      type: el.getAttribute('data-type') || 'Element',
      automationId: el.getAttribute('data-automationId') || null,
      text: el.getAttribute('data-text') || null,
      hasSource: el.getAttribute('data-hasSource') === 'true',
    };
  }
  // Tell the host which element is selected so the agent can answer about "the selected element"
  // (canvas: updates the extension's selection store; VS Code: feeds a language-model tool).
  // Debounced: rapid tree-browsing must not flood the host (the canvas relays each to /control select).
  let _selHostTimer = null, _selHostPending = null;
  function postSelectionToHost(el) {
    _selHostPending = elementInfo(el);
    if (_selHostTimer) clearTimeout(_selHostTimer);
    _selHostTimer = setTimeout(() => { _selHostTimer = null; postToHost('devflow:selectionChanged', { element: _selHostPending }); }, 120);
  }

  // Reliable handshake: announce readiness (with the bridge nonce) and retry until the host acks
  // with its capabilities. The host also re-announces on iframe load, so either order works.
  let hsTries = 0, hsTimer = null;
  function announceReady() {
    if (hostCaps || !framed || !bridgeId) return;
    postToHost('devflow:ready', { version: 1 });
    if (++hsTries < 12) {
      hsTimer = setTimeout(announceReady, 300);
    } else {
      hostHandshakeExpired = true;
      downloadPendingHostRecordings();
    }
  }
  window.addEventListener('message', (e) => {
    if (e.source !== window.parent) return;              // only our embedding host
    const d = e.data;
    if (!d || d.bridgeId !== bridgeId) return;            // authenticated by the per-session bridge nonce
    if (d.type === 'devflow:hostResult') {
      const pending = typeof d.requestId === 'string' ? pendingHostRequests.get(d.requestId) : null;
      if (!pending) return;
      clearTimeout(pending.timer);
      pendingHostRequests.delete(d.requestId);
      pending.resolve(Object.assign({}, d, {
        ok: d.ok === true,
        message: typeof d.message === 'string' ? d.message : null,
        error: typeof d.error === 'string' ? d.error : null,
      }));
    } else if (d.type === 'devflow:host') {
      hostCaps = Array.isArray(d.capabilities) ? d.capabilities : [];
      if (typeof d.hostKind === 'string' && d.hostKind) {
        leaseHolderKind = d.hostKind;
        hostKind = d.hostKind.includes('canvas') ? 'canvas' : (d.hostKind.includes('vscode') ? 'vscode' : d.hostKind);
        document.body.dataset.hostKind = hostKind;
        document.documentElement.dataset.hostKind = hostKind;
      }
      if (typeof d.hostLabel === 'string' && d.hostLabel) leaseHolderLabel = d.hostLabel;
      if (d.profile) applyHostProfile(d.profile);
      updateHostLayout();
      if (hsTimer) { clearTimeout(hsTimer); hsTimer = null; }
      updateHostButtons();
      if (pendingHostRecordings.length > 0 && hostHas('saveRecording')) {
        while (pendingHostRecordings.length > 0) {
          const recording = pendingHostRecordings[0];
          if (!postToHost('devflow:recordingComplete', recording)) break;
          pendingHostRecordings.shift();
          setStatus(`${recording.prefix || ''}Recorded ${recording.steps} step(s) — handed to the host to save. Replay is now available.`);
        }
      } else if (pendingHostRecordings.length > 0) {
        downloadPendingHostRecordings();
      }
      if (d.theme) applyTheme(d.theme);                  // host may bundle its theme with the capability ack
    } else if (d.type === 'devflow:theme') {
      if (d.profile) applyHostProfile(d.profile);
      applyTheme(d);                                     // host reports/updates its color scheme + palette
    }
  });

  function selectedElement() { return selectedId ? elById(selectedId) : null; }

  function updateHostButtons() {
    updateCommandStates();
    const el = selectedElement();
    const dataAvailable = !!dockSnapshot && supportsDataContextScope(dockSnapshot.scope);
    for (const item of copilotMenuItems) {
      const kind = item.getAttribute('data-copilot-context');
      item.disabled =
        (kind === 'selection' && !el) ||
        (kind === 'workflow' && !lastMarkdown) ||
        (kind === 'combined' && !(el && lastMarkdown)) ||
        (kind === 'data' && !dataAvailable);
    }
    updateDataAttachButton();
  }

  async function copyText(text) {
    try { await navigator.clipboard.writeText(text); return true; } catch { return false; }
  }
  function shortFile(f) { const p = String(f).split(/[\\/]/); return p[p.length - 1] || f; }

  async function openSource() {
    const el = selectedElement();
    if (!el || el.getAttribute('data-hasSource') !== 'true') return;
    const id = el.getAttribute('data-id');
    try {
      const r = await fetch(`${basePath}/api/source`, {
        method: 'POST',
        headers: inspectorToken
          ? { 'Content-Type': 'application/json', 'X-DevFlow-Inspector-Token': inspectorToken }
          : { 'Content-Type': 'application/json' },
        body: JSON.stringify({ elementId: id }),
      });
      const j = r.ok ? await r.json().catch(() => null) : null;
      if (!j || !j.ok || !j.file) {
        setStatus((j && j.error) || 'No source available for this element.');
        return;
      }
      if (hostHas('openSource') && postToHost('devflow:openSource', {
        file: j.file,
        line: j.line || 1,
        column: j.column || 1,
        sourceHash: j.sourceHash || null,
      })) {
        setStatus(`Opening ${shortFile(j.file)}:${j.line || 1}…`);
      } else {
        const loc = `${j.file}:${j.line || 1}`;
        const copied = await copyText(loc);
        setStatus(copied ? `Source: ${loc} (copied)` : `Source: ${loc}. Could not copy from this host.`);
      }
    } catch (err) { setStatus('Could not resolve source.'); }
  }

  let _copilotBusy = false;
  function buildCopilotPayload(kind) {
    const el = selectedElement();
    const includeSelection = kind === 'selection' || kind === 'combined';
    const includeWorkflow = kind === 'workflow' || kind === 'combined';
    const element = includeSelection && el ? {
      type: el.getAttribute('data-type') || 'Element',
      automationId: el.getAttribute('data-automationId') || null,
      text: el.getAttribute('data-text') || null,
      id: el.getAttribute('data-id') || null,
    } : null;
    let markdown = includeWorkflow ? lastMarkdown : null;
    let markdownTruncated = false;
    if (markdown && markdown.length > 6000) {
      markdown = markdown.slice(0, 6000);
      markdownTruncated = true;
    }
    return {
      element,
      markdown,
      markdownTruncated,
      workflowName: includeWorkflow ? lastMarkdownName : null,
      workflowSource: includeWorkflow ? lastMarkdownSource : null,
      appName: document.title || null,
    };
  }

  async function copyCopilotContext(kind, payload) {
    const lines = [];
    if (payload.element) {
      const element = payload.element;
      lines.push(`Element: ${element.type}${element.automationId ? ' #' + element.automationId : ''}${element.text ? ' "' + element.text + '"' : ''}`);
    }
    if (payload.markdown) {
      lines.push('', `Workflow${payload.workflowName ? ` (${payload.workflowName})` : ''}:`, payload.markdown);
      if (payload.markdownTruncated) lines.push('…(truncated)');
    }
    const ok = lines.length > 0 && await copyText(lines.join('\n'));
    setStatus(ok
      ? `Copied ${kind === 'combined' ? 'selection and workflow' : kind} context for Copilot.`
      : 'Copy failed — choose available Inspector context and try again.');
  }

  async function sendCopilotContext(kind) {
    if (kind === 'data') {
      await attachDockDataToCopilot();
      return;
    }
    if (_copilotBusy) return;
    _copilotBusy = true;
    try {
      const payload = buildCopilotPayload(kind);
      if ((kind === 'selection' && !payload.element) || (kind === 'workflow' && !payload.markdown) ||
          (kind === 'combined' && !(payload.element && payload.markdown))) {
        setStatus('The selected Copilot context is not available yet.');
        return;
      }
      if (hostHas('copilotContext')) {
        setStatus(`Adding ${kind === 'combined' ? 'selection and workflow' : kind} context to Copilot…`);
        const result = await requestHost('devflow:attachCopilot', { context: kind, payload }, 10000);
        const successMessage = result && result.ok
          ? (result.message || 'Added Inspector context to Copilot.')
          : null;
        setStatus(result && result.ok
          ? `${successMessage} Copilot can request mutation control with maui_take_control.`
          : ((result && result.error) || 'The host could not add Inspector context to Copilot.'));
        return;
      }
      if (kind === 'selection' && hostHas('copilot') && postToHost('devflow:sendToCopilot', { payload })) {
        setStatus('Sent selected-element context to Copilot.');
        return;
      }
      await copyCopilotContext(kind, payload);
    } finally {
      _copilotBusy = false;
    }
  }

  function closeCopilotMenu(restore = false) {
    if (!copilotMenu || copilotMenu.classList.contains('df-hidden')) return;
    copilotMenu.classList.add('df-hidden');
    copilotBtn?.setAttribute('aria-expanded', 'false');
    if (restore) copilotBtn?.focus({ preventScroll: true });
  }

  function openCopilotMenu() {
    if (!copilotMenu || !copilotBtn || copilotBtn.getAttribute('aria-disabled') === 'true') {
      setStatus('Select an element, load a workflow, or open a Data view first.');
      return;
    }
    const anchor = copilotBtn.getBoundingClientRect();
    const nested = !!tb.overflow?.contains(copilotBtn) &&
      document.body.classList.contains('df-more-open');
    copilotMenu.style.visibility = 'hidden';
    copilotMenu.classList.remove('df-hidden');
    const menu = copilotMenu.getBoundingClientRect();
    let left;
    let top;
    if (nested) {
      const right = anchor.right + 4;
      left = right + menu.width <= window.innerWidth - 8
        ? right
        : Math.max(8, anchor.left - menu.width - 4);
      top = Math.max(8, Math.min(window.innerHeight - menu.height - 8, anchor.top));
    } else {
      left = Math.max(8, Math.min(window.innerWidth - menu.width - 8, anchor.left));
      const below = anchor.bottom + 4;
      top = below + menu.height <= window.innerHeight - 8
        ? below
        : Math.max(8, anchor.top - menu.height - 4);
    }
    copilotMenu.style.left = `${left}px`;
    copilotMenu.style.top = `${top}px`;
    copilotMenu.style.visibility = '';
    copilotBtn.setAttribute('aria-expanded', 'true');
    copilotMenuItems.find(item => !item.disabled)?.focus({ preventScroll: true });
  }

  if (sourceBtn) sourceBtn.addEventListener('click', openSource);
  if (copilotBtn) copilotBtn.addEventListener('click', () => {
    if (copilotMenu && !copilotMenu.classList.contains('df-hidden')) closeCopilotMenu(true);
    else openCopilotMenu();
  });
  for (const item of copilotMenuItems) {
    item.addEventListener('click', async () => {
      if (item.disabled) return;
      const kind = item.getAttribute('data-copilot-context');
      closeCopilotMenu();
      setMoreOpen(false);
      await sendCopilotContext(kind);
    });
  }
  if (copilotMenu) copilotMenu.addEventListener('keydown', (event) => {
    const enabled = copilotMenuItems.filter(item => !item.disabled);
    const index = enabled.indexOf(document.activeElement);
    let target = null;
    if (event.key === 'ArrowDown') target = enabled[(index + 1 + enabled.length) % enabled.length];
    else if (event.key === 'ArrowUp') target = enabled[(index - 1 + enabled.length) % enabled.length];
    else if (event.key === 'Home') target = enabled[0];
    else if (event.key === 'End') target = enabled[enabled.length - 1];
    else return;
    if (!target) return;
    event.preventDefault();
    target.focus({ preventScroll: true });
  });
  document.addEventListener('pointerdown', (event) => {
    if (!copilotMenu || copilotMenu.classList.contains('df-hidden')) return;
    if (copilotMenu.contains(event.target) || copilotBtn?.contains(event.target)) return;
    closeCopilotMenu();
  });
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && copilotMenu && !copilotMenu.classList.contains('df-hidden')) {
      event.preventDefault();
      event.stopImmediatePropagation();
      closeCopilotMenu(true);
    }
  });
  if (framed && bridgeId) announceReady();
  updateHostButtons();

  // ── Data dock: Logs / Network / Preferences / Device / Sensors / Files ──
  // Lazy-loaded read-only tabs over the token-gated broker proxies. All app-controlled data is
  // rendered with textContent / DOM nodes (never innerHTML) so a malicious log line, URL, header,
  // filename, or preference value can't inject markup. Inherited by every host (browser/VS Code/canvas).
  const dockEl = document.getElementById('df-dock');
  const dockTabsEl = document.getElementById('df-dock-tabs');
  const dockBodyEl = document.getElementById('df-dock-body');
  const dockMetaEl = document.getElementById('df-dock-meta');
  const dockCollapseBtn = document.getElementById('df-dock-collapse');
  const dockRefreshBtn = document.getElementById('df-dock-refresh');
  const dockCloseBtn = document.getElementById('df-dock-close');
  const toggleDockBtn = document.getElementById('df-toggle-dock');
  dockActiveTab = 'logs';
  let dockLoaded = false;
  let filesRoot = null, filesPath = '';
  let filesRoots = [];
  let filesLoadGeneration = 0;
  let cdpWebviewId = null;
  let dockViewGeneration = 0;
  let networkDetailId = null;
  let networkListSignature = '';
  let networkPollInFlight = false;
  let networkRequestEpoch = 0;
  const NETWORK_AUTO_REFRESH_MS = 2000;

  function elh(tag, attrs, ...children) {
    const e = document.createElement(tag);
    if (attrs) for (const k in attrs) {
      if (k === 'text') e.textContent = attrs[k];
      else if (k === 'class') e.className = attrs[k];
      else if (k === 'onclick') e.addEventListener('click', attrs[k]);
      else e.setAttribute(k, attrs[k]);
    }
    for (const c of children) if (c != null) e.append(c);
    return e;
  }
  function svgIcon(id, className) {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('class', className || 'df-ic-xs');
    svg.setAttribute('aria-hidden', 'true');
    const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
    use.setAttribute('href', '#' + id);
    svg.append(use);
    return svg;
  }
  function dockEmpty(msg) { dockBodyEl.replaceChildren(elh('div', { class: 'df-empty', text: msg })); }
  function setDockMeta(text) { if (dockMetaEl) dockMetaEl.textContent = text || ''; }
  function clearDockSnapshot() {
    dockSnapshot = null;
    updateDataAttachButton();
    updateHostButtons();
  }

  function recordDockSnapshot(scope, title, payload, itemCount, metadata) {
    if (!supportsDataContextScope(scope)) { clearDockSnapshot(); return; }
    dockSnapshot = createDataSnapshot({
      scope,
      title,
      payload,
      itemCount,
      metadata,
      agent: inspectorAgent,
    });
    updateDataAttachButton();
    updateHostButtons();
  }

  function updateDataAttachButton() {
    if (!attachDataBtn) return;
    if (!dockSnapshot) {
      attachDataBtn.disabled = true;
      attachDataBtn.title = 'Load a supported Data tab before adding it to Copilot';
      return;
    }
    attachDataBtn.disabled = false;
    attachDataBtn.title = `Add ${dockSnapshot.title} to Copilot`;
  }

  async function attachDockDataToCopilot() {
    if (!dockSnapshot) {
      setStatus('Load a supported Data tab before adding it to Copilot.');
      return;
    }
    if (hostHas('attachData')) {
      setStatus(`Adding ${dockSnapshot.title} to Copilot…`);
      const result = await requestHost('devflow:attachData', { snapshot: dockSnapshot }, 12000);
      setStatus(result.ok
        ? `${result.message || `Added ${dockSnapshot.title} to Copilot.`} ` +
          'Copilot can request mutation control with maui_take_control.'
        : (result.error || `Could not add ${dockSnapshot.title} to Copilot.`));
      return;
    }
    const text = [
      'MAUI DevFlow Data snapshot:',
      JSON.stringify(dockSnapshot, null, 2),
    ].join('\n');
    const ok = await copyText(text);
    setStatus(ok ? 'Copied Data context for Copilot.' : 'Could not copy Data context.');
  }

  // Safe generic JSON renderer (objects -> key/value tables, arrays -> lists, primitives -> text),
  // masking values whose key looks secret. Used for Preferences/Device and as a fallback.
  function jsonView(value, keyName) {
    if (value === null || value === undefined) return elh('span', { text: 'null' });
    if (typeof value !== 'object') {
      const s = String(value);
      if (isSecretContextKey(keyName) && s.length > 0) {
        const span = elh('span', { class: 'df-masked', text: '••••• (reveal)' });
        span.addEventListener('click', () => { span.className = ''; span.textContent = s; });
        return span;
      }
      return elh('span', { text: s });
    }
    if (Array.isArray(value)) {
      if (!value.length) return elh('span', { text: '[]' });
      const tbl = elh('table');
      value.forEach((item, i) => tbl.append(elh('tr', null, elh('td', { class: 'df-kv-key', text: String(i) }), elh('td', null, jsonView(item)))));
      return tbl;
    }
    const keys = Object.keys(value);
    if (!keys.length) return elh('span', { text: '{}' });
    const tbl = elh('table');
    for (const k of keys) tbl.append(elh('tr', null, elh('td', { class: 'df-kv-key', text: k }), elh('td', null, jsonView(value[k], k))));
    return tbl;
  }

  function renderLogs(j) {
    const logs = j && j.logs;
    if (!Array.isArray(logs) || !logs.length) {
      clearDockSnapshot();
      dockEmpty(j && j.error ? j.error : 'No logs.');
      return;
    }
    const frag = document.createDocumentFragment();
    for (const e of logs) {
      const level = e.l || e.level || 'Info';
      const row = elh('div', { class: 'df-log-row df-log-' + level });
      row.append(elh('span', { class: 'df-log-time', text: (e.t || '').replace('T', ' ').replace('Z', '') + ' ' }));
      row.append(elh('span', { text: '[' + level + '] ' }));
      if (e.c || e.s) row.append(elh('span', { class: 'df-log-cat', text: (e.c || e.s) + ' ' }));
      row.append(elh('span', { text: e.m || '' }));
      if (e.e) row.append(elh('div', { class: 'df-log-ERROR', text: String(e.e) }));
      frag.append(row);
    }
    dockBodyEl.replaceChildren(frag);
    // The logs API returns entries newest first.
    const captured = logs.slice(0, 100);
    recordDockSnapshot(
      'logs',
      `Logs · ${captured.length === logs.length ? logs.length : `newest ${captured.length} of ${logs.length}`}`,
      captured,
      logs.length,
      { capturedCount: captured.length });
  }

  function networkSignature(reqs) {
    return JSON.stringify(reqs.map((r) => [
      r.id || null,
      r.method || null,
      r.url || r.path || null,
      r.statusCode ?? null,
      r.durationMs ?? null,
      r.error || null,
    ]));
  }

  function renderNetwork(j, options) {
    const opts = options || {};
    const reqs = j && j.requests;
    if (!Array.isArray(reqs)) {
      if (opts.nonDestructive) return false;
      clearDockSnapshot();
      networkListSignature = '';
      dockEmpty(j && j.error ? j.error : 'Network data unavailable.');
      return true;
    }
    const signature = networkSignature(reqs);
    if (!opts.force && signature === networkListSignature) return false;
    const scrollTop = opts.preserveScroll ? dockBodyEl.scrollTop : 0;
    networkListSignature = signature;
    if (!reqs.length) {
      clearDockSnapshot();
      dockEmpty('No requests captured.');
      return true;
    }
    const tbl = elh('table');
    tbl.append(elh('tr', null, elh('th', { text: 'Method' }), elh('th', { text: 'URL' }), elh('th', { text: 'Status' }), elh('th', { text: 'ms' })));
    for (const r of reqs) {
      const tr = elh('tr', { class: 'df-row-click' },
        elh('td', { text: r.method || '' }),
        elh('td', { text: r.url || r.path || '' }),
        elh('td', { text: r.statusCode != null ? String(r.statusCode) : (r.error ? 'ERR' : '') }),
        elh('td', { text: r.durationMs != null ? String(r.durationMs) : '' }));
      if (r.id) tr.addEventListener('click', () => loadNetworkDetail(r.id));
      tbl.append(tr);
    }
    dockBodyEl.replaceChildren(tbl);
    if (opts.preserveScroll) dockBodyEl.scrollTop = scrollTop;
    recordDockSnapshot(
      'network',
      `Network · ${reqs.length} request${reqs.length === 1 ? '' : 's'}`,
      reqs.map((r) => ({
        id: r.id || null,
        method: r.method || null,
        url: r.url || r.path || null,
        statusCode: r.statusCode ?? null,
        durationMs: r.durationMs ?? null,
        error: r.error || null,
      })),
      reqs.length,
      { view: 'list' });
    return true;
  }

  function networkListIsCurrent(generation) {
    return generation === dockViewGeneration && dockActiveTab === 'network' && networkDetailId === null;
  }

  function networkPollIsActive() {
    return networkListIsCurrent(dockViewGeneration)
      && !dockEl.classList.contains('df-hidden')
      && !document.body.classList.contains('df-dock-collapsed')
      && !document.hidden
      && !replaying;
  }

  async function loadNetworkList(options) {
    const opts = options || {};
    if (opts.automatic && networkPollInFlight) return false;
    if (opts.automatic) networkPollInFlight = true;
    const generation = opts.generation ?? dockViewGeneration;
    const requestEpoch = ++networkRequestEpoch;
    try {
      const j = await apiPost('/api/network', { limit: 100 });
      if (requestEpoch !== networkRequestEpoch) return false;
      if (!networkListIsCurrent(generation)) return false;
      if (opts.automatic && !networkPollIsActive()) return false;
      const changed = renderNetwork(j, {
        force: !opts.automatic,
        nonDestructive: !!opts.automatic,
        preserveScroll: !!opts.automatic,
      });
      if (opts.automatic && changed) setDockMeta('live · updated ' + new Date().toLocaleTimeString());
      return changed;
    } finally {
      if (opts.automatic) networkPollInFlight = false;
    }
  }

  async function loadNetworkDetail(id) {
    if (!id) return;
    const generation = ++dockViewGeneration;
    networkDetailId = id;
    clearDockSnapshot();
    setDockMeta('loading…');
    const j = await apiPost('/api/network/detail', { id });
    if (generation !== dockViewGeneration || dockActiveTab !== 'network' || networkDetailId !== id) return;
    setDockMeta('live paused · captured ' + new Date().toLocaleTimeString());
    const r = j && j.request;
    if (!r) { dockEmpty('Request detail unavailable.'); return; }
    const frag = document.createDocumentFragment();
    frag.append(elh('button', { class: 'df-dock-btn', type: 'button', text: '‹ Back to requests', onclick: () => loadTab('network') }));
    frag.append(elh('div', { class: 'df-section-title', text: (r.method || '') + ' ' + (r.url || '') }));
    frag.append(jsonView({
      status: r.statusCode, statusText: r.statusText, durationMs: r.durationMs,
      requestContentType: r.requestContentType, responseContentType: r.responseContentType,
      requestHeaders: r.requestHeaders, responseHeaders: r.responseHeaders,
      requestBody: r.requestBody, responseBody: r.responseBody, error: r.error,
    }));
    dockBodyEl.replaceChildren(frag);
    recordDockSnapshot(
      'network',
      `Network request · ${r.method || ''} ${r.url || ''}`.trim(),
      {
        id: r.id || id,
        method: r.method || null,
        url: r.url || null,
        statusCode: r.statusCode ?? null,
        statusText: r.statusText || null,
        durationMs: r.durationMs ?? null,
        requestContentType: r.requestContentType || null,
        responseContentType: r.responseContentType || null,
        requestHeaders: r.requestHeaders || null,
        responseHeaders: r.responseHeaders || null,
        error: r.error || null,
      },
      1,
      { view: 'detail', requestId: r.id || id });
  }

  function renderPreferences(j) {
    if (!j || j.ok === false) {
      clearDockSnapshot();
      dockEmpty((j && j.error) || 'Preferences unavailable.');
      return;
    }
    const frag = document.createDocumentFragment();
    frag.append(elh('div', { class: 'df-section-title', text: 'Known preferences (values with secret-looking keys are masked)' }));
    frag.append(jsonView(j.preferences));
    dockBodyEl.replaceChildren(frag);
    const count = j.preferences && typeof j.preferences === 'object' ? Object.keys(j.preferences).length : 0;
    recordDockSnapshot('preferences', `Preferences · ${count} entr${count === 1 ? 'y' : 'ies'}`, j.preferences, count);
  }

  function renderDevice(j) {
    const dev = j && j.device;
    if (!dev || typeof dev !== 'object' || !Object.keys(dev).length) {
      clearDockSnapshot();
      dockEmpty('Device info unavailable.');
      return;
    }
    const frag = document.createDocumentFragment();
    const labels = { 'device-info': 'Device', 'device-display': 'Display', battery: 'Battery', connectivity: 'Connectivity' };
    const normalizedDevice = {};
    for (const k of Object.keys(dev)) {
      const rawValue = dev[k];
      const value = k === 'battery' && rawValue && rawValue.success === false
        ? (rawValue.reason === 'missing_permission'
          ? {
              available: false,
              status: 'Unavailable on this target',
              guidance: 'Detailed battery statistics are a privileged Android capability and are not available to ordinary apps.',
            }
          : {
              available: false,
              status: 'Unavailable on this target',
              guidance: 'Battery information is not available from this app on the current target.',
            })
        : rawValue;
      normalizedDevice[k] = value;
      frag.append(elh('div', { class: 'df-section-title', text: labels[k] || k }));
      frag.append(jsonView(value));
    }
    dockBodyEl.replaceChildren(frag);
    recordDockSnapshot('device', 'Device snapshot', normalizedDevice, Object.keys(normalizedDevice).length);
  }

  function renderSensors(j) {
    const frag = document.createDocumentFragment();
    const geoBtn = elh('button', { class: 'df-dock-btn df-icon-label', type: 'button' },
      svgIcon('i-location'), elh('span', { text: 'Read geolocation' }));
    geoBtn.addEventListener('click', readGeolocation);
    frag.append(geoBtn);
    const geoOut = elh('div', { id: 'df-geo-out' });
    frag.append(geoOut);
    const sensors = j && j.sensors;
    if (Array.isArray(sensors) && sensors.length) {
      const tbl = elh('table');
      tbl.append(elh('tr', null, elh('th', { text: 'Sensor' }), elh('th', { text: 'Supported' }), elh('th', { text: 'Active' }), elh('th', { text: 'Subscribers' })));
      for (const s of sensors) tbl.append(elh('tr', null,
        elh('td', { text: s.sensor || '' }), elh('td', { text: String(s.supported) }),
        elh('td', { text: String(s.active) }), elh('td', { text: String(s.subscribers != null ? s.subscribers : '') })));
      frag.append(tbl);
    } else {
      frag.append(elh('div', { class: 'df-empty', text: 'No sensors reported.' }));
    }
    dockBodyEl.replaceChildren(frag);
    if (Array.isArray(sensors) && sensors.length) {
      recordDockSnapshot('sensors', `Sensors · ${sensors.length}`, sensors, sensors.length, { geolocationIncluded: false });
    } else {
      clearDockSnapshot();
    }
  }

  async function readGeolocation() {
    if (replaying) { setStatus('Geolocation is disabled during replay.'); return; }
    const out = document.getElementById('df-geo-out');
    if (out) out.textContent = 'Reading location…';
    const j = await apiPost('/api/geolocation', {});
    if (out) out.replaceChildren(j && j.ok ? jsonView(j.location) : elh('span', { class: 'df-empty', text: (j && j.error) || 'Geolocation unavailable.' }));
  }

  async function renderFiles(j) {
    const roots = j && j.roots;
    const frag = document.createDocumentFragment();
    const rootsArr = extractRoots(roots);
    if (!rootsArr.length) {
      clearDockSnapshot();
      dockEmpty((j && j.error) || 'No storage roots advertised by this app.');
      return;
    }
    filesRoots = rootsArr;
    if (!filesRoot || !rootsArr.some((r) => r.id === filesRoot)) filesRoot = rootsArr[0].id;
    const bar = elh('div', { class: 'df-files-toolbar' });
    bar.append(elh('label', { class: 'df-kv-key', for: 'df-files-root', text: 'Root' }));
    const sel = elh('select', { class: 'df-dock-btn', id: 'df-files-root' });
    for (const r of rootsArr) sel.append(elh('option', { value: r.id, text: r.label }));
    sel.value = filesRoot;
    sel.addEventListener('change', () => {
      filesRoot = sel.value;
      filesPath = '';
      updateFilesRootInfo();
      loadFiles();
    });
    bar.append(sel);
    bar.append(elh('span', {
      class: 'df-files-mode',
      title: 'The inspector browses files without changing them. DevFlow tools can download, upload, or delete files when explicitly requested.',
      text: 'Browse only',
    }));
    frag.append(bar);
    frag.append(elh('div', { id: 'df-files-root-info', class: 'df-files-root-info' }));
    frag.append(elh('div', { id: 'df-files-list' }));
    dockBodyEl.replaceChildren(frag);
    updateFilesRootInfo();
    await loadFiles();
  }

  function extractRoots(roots) {
    let arr = [];
    if (Array.isArray(roots)) arr = roots;
    else if (roots && Array.isArray(roots.roots)) arr = roots.roots;
    return arr.map((r) => (typeof r === 'string'
      ? { id: r, label: r }
      : {
        id: r.id || r.name || r.root || r.path || '',
        label: r.displayName || r.name || r.id || r.path || '(root)',
        kind: r.kind || null,
        isReadOnly: r.isReadOnly === true,
        isPersistent: r.isPersistent === true,
        isUserVisible: r.isUserVisible !== false,
      })).filter((r) => r.id);
  }

  function currentFilesRoot() {
    return filesRoots.find((r) => r.id === filesRoot) || null;
  }

  function updateFilesRootInfo() {
    const info = document.getElementById('df-files-root-info');
    if (!info) return;
    const root = currentFilesRoot();
    if (!root) { info.textContent = ''; return; }
    const parts = [];
    if (root.kind === 'appData') parts.push('Private app storage');
    else if (root.kind) parts.push(root.kind);
    if (root.isPersistent) parts.push('persistent');
    if (root.isReadOnly) parts.push('root is read-only');
    info.textContent = parts.join(' · ');
  }

  async function loadFiles() {
    const list = document.getElementById('df-files-list');
    if (!list) return;
    const loadGeneration = ++filesLoadGeneration;
    const dockGeneration = dockViewGeneration;
    const requestedRoot = filesRoot;
    const requestedPath = filesPath;
    clearDockSnapshot();
    list.textContent = 'Loading…';
    const j = await apiPost('/api/files/list', { root: requestedRoot, path: requestedPath });
    if (loadGeneration !== filesLoadGeneration || dockGeneration !== dockViewGeneration
        || dockActiveTab !== 'files' || requestedRoot !== filesRoot || requestedPath !== filesPath) return;
    const frag = document.createDocumentFragment();
    // Breadcrumb.
    const crumb = elh('div', null);
    crumb.append(elh('span', { class: 'df-crumb', text: '/', onclick: () => { filesPath = ''; loadFiles(); } }));
    if (filesPath) {
      const parts = filesPath.split('/').filter(Boolean);
      let acc = '';
      for (const p of parts) { acc += (acc ? '/' : '') + p; const here = acc; crumb.append(elh('span', { text: ' ' }), elh('span', { class: 'df-crumb', text: p, onclick: () => { filesPath = here; loadFiles(); } })); }
    }
    frag.append(crumb);
    const entries = extractEntries(j && j.files);
    if (j && j.error) {
      clearDockSnapshot();
      frag.append(elh('div', { class: 'df-empty', text: j.error }));
    } else if (!entries.length) {
      const empty = elh('div', { class: 'df-empty df-files-empty' });
      const root = currentFilesRoot();
      empty.append(elh('strong', {
        text: filesPath ? 'This folder is empty.' : ((root && root.label) || 'This storage root') + ' is empty.',
      }));
      if (!filesPath) {
        empty.append(elh('div', {
          text: 'Files appear here after the app writes to this location. In-memory data and Preferences are not files.',
        }));
      }
      frag.append(empty);
    }
    else {
      const tbl = elh('table');
      tbl.append(elh('tr', null, elh('th', { text: 'Name' }), elh('th', { text: 'Size' })));
      // Directories first.
      for (const e of entries.filter((x) => x.dir)) {
        const openFolder = () => { filesPath = (filesPath ? filesPath + '/' : '') + e.name; loadFiles(); };
        const nameCell = elh('td', null,
          elh('button', { class: 'df-file-link', type: 'button', title: 'Open ' + e.name, onclick: openFolder },
            svgIcon('i-folder'), elh('span', { text: e.name })));
        tbl.append(elh('tr', null, nameCell, elh('td', { text: '' })));
      }
      for (const e of entries.filter((x) => !x.dir)) {
        tbl.append(elh('tr', null,
          elh('td', null, elh('span', { class: 'df-file-name' }, svgIcon('i-file'), elh('span', { text: e.name }))),
          elh('td', { text: e.size != null ? String(e.size) : '' })));
      }
      frag.append(tbl);
    }
    list.replaceChildren(frag);
    if (!(j && j.error)) {
      const root = currentFilesRoot();
      recordDockSnapshot(
        'files',
        `Files · ${(root && root.label) || filesRoot || 'storage'}`,
        {
          root: root ? {
            id: root.id,
            displayName: root.label,
            kind: root.kind,
            isReadOnly: root.isReadOnly,
            isPersistent: root.isPersistent,
          } : { id: filesRoot },
          path: filesPath,
          entries: entries.map((entry) => ({
            name: entry.name,
            type: entry.dir ? 'directory' : 'file',
            size: entry.size,
            lastModified: entry.lastModified || null,
          })),
        },
        entries.length,
        { contentsIncluded: false });
    }
  }

  function extractEntries(files) {
    let arr = [];
    if (Array.isArray(files)) arr = files;
    else if (files && Array.isArray(files.entries)) arr = files.entries;
    else if (files && Array.isArray(files.files)) arr = files.files;
    return arr.map((e) => (typeof e === 'string'
      ? { name: e, dir: false, size: null }
      : {
        name: e.name || e.path || '',
        dir: !!(e.isDirectory || e.directory || e.dir || e.type === 'directory'),
        size: e.size != null ? e.size : e.length,
        lastModified: e.lastModified || e.modified || null,
      })).filter((e) => e.name);
  }

  function dockLoadIsCurrent(name, generation) {
    return dockActiveTab === name && dockViewGeneration === generation;
  }

  const tabLoaders = {
    layout: async () => {
      if (isDiagnosticsPaneVisible()) layoutPanel?.activate(dockBodyEl);
    },
    logs: async (generation) => {
      const j = await apiPost('/api/logs', { limit: 200 });
      if (dockLoadIsCurrent('logs', generation)) renderLogs(j);
    },
    network: async (generation) => loadNetworkList({ generation }),
    preferences: async (generation) => {
      const j = await apiPost('/api/preferences', {});
      if (dockLoadIsCurrent('preferences', generation)) renderPreferences(j);
    },
    device: async (generation) => {
      const j = await apiPost('/api/device', {});
      if (dockLoadIsCurrent('device', generation)) renderDevice(j);
    },
    sensors: async (generation) => {
      const j = await apiPost('/api/sensors', {});
      if (dockLoadIsCurrent('sensors', generation)) renderSensors(j);
    },
    files: async (generation) => {
      const j = await apiPost('/api/files/roots', {});
      if (dockLoadIsCurrent('files', generation)) await renderFiles(j);
    },
    alerts: async (generation) => {
      const j = await apiPost('/api/alerts', {});
      if (dockLoadIsCurrent('alerts', generation)) renderAlerts(j);
    },
    webview: async (generation) => {
      const j = await apiPost('/api/cdp/webviews', {});
      if (dockLoadIsCurrent('webview', generation)) renderWebView(j);
    },
  };

  function renderAlerts(result) {
    if (!result || result.supported === false) {
      dockEmpty((result && result.error) || 'Native alert control is unavailable for this target.');
      return;
    }
    if (!result.ok) {
      dockEmpty(result.error || 'Could not inspect native alerts.');
      return;
    }
    if (!result.alert) {
      dockEmpty('No native alert is visible.');
      return;
    }
    recordDockSnapshot('alerts', result.alert.title || 'Native alert', result.alert, 1);

    const fragment = document.createDocumentFragment();
    fragment.append(elh('div', { class: 'df-section-title', text: result.alert.title || 'Native alert' }));
    const buttons = Array.isArray(result.alert.buttons) ? result.alert.buttons : [];
    if (!buttons.length) {
      fragment.append(elh('div', { class: 'df-empty', text: 'This alert has no actionable buttons.' }));
    } else {
      const actions = elh('div', { class: 'df-alert-actions' });
      for (const alertButton of buttons) {
        const label = String(alertButton.label || 'Dismiss');
        const button = elh('button', { class: 'df-dock-btn', text: label, 'data-alert-action': 'dismiss' });
        button.disabled = !presentation.driveAllowed;
        button.title = !isWriter
          ? 'Take control before dismissing native alerts.'
          : (!presentation.healthy ? 'Retry the Preview before dismissing native alerts.' : `Dismiss with ${label}`);
        button.addEventListener('click', async () => {
          if (!ensureCanDrive()) return;
          button.dataset.pending = 'true';
          button.disabled = true;
          const response = await apiPost('/api/alerts/dismiss', { buttonLabel: label });
          if (response && response.ok && response.dismissed) {
            setStatus(`Dismissed native alert with ${label}.`);
            await loadTab('alerts');
          } else {
            delete button.dataset.pending;
            button.disabled = false;
            setStatus((response && response.error) || 'Could not dismiss the native alert.');
          }
        });
        actions.append(button);
      }
      fragment.append(actions);
    }
    dockBodyEl.replaceChildren(fragment);
  }

  // ── Blazor WebView CDP tab — list WebViews, view source, evaluate JS ──
  function extractWebviews(v) {
    let arr = [];
    if (Array.isArray(v)) arr = v;
    else if (v && Array.isArray(v.webViews)) arr = v.webViews;
    else if (v && Array.isArray(v.webviews)) arr = v.webviews;
    else if (v && Array.isArray(v.targets)) arr = v.targets;
    return arr.map((w) => (typeof w === 'string'
      ? { id: w, label: w }
      : { id: w.id || w.targetId || w.webviewId || '', label: w.title || w.url || w.id || 'webview' })).filter((w) => w.id);
  }

  async function renderWebView(j) {
    const wvs = extractWebviews(j && j.webviews);
    if (!wvs.length) { dockEmpty((j && j.error) || 'No Blazor WebViews in this app.'); return; }
    const frag = document.createDocumentFragment();
    const bar = elh('div', null, elh('span', { class: 'df-kv-key', text: 'WebView: ' }));
    const sel = elh('select', { class: 'df-dock-btn' });
    for (const w of wvs) sel.append(elh('option', { value: w.id, text: w.label }));
    if (!cdpWebviewId || !wvs.some((w) => w.id === cdpWebviewId)) cdpWebviewId = wvs[0].id;
    sel.value = cdpWebviewId;
    sel.addEventListener('change', () => { cdpWebviewId = sel.value; });
    bar.append(sel);
    bar.append(document.createTextNode(' '));
    bar.append(elh('button', { class: 'df-dock-btn', text: 'View source', onclick: cdpViewSource }));
    frag.append(bar);
    const evalRow = elh('div', null);
    const inp = elh('input', { class: 'df-dock-btn', id: 'df-cdp-expr', placeholder: 'JS expression, e.g. document.title' });
    inp.style.width = '360px';
    // Wait for key release before focusing the confirmation action so this Enter cannot approve it.
    let enterArmed = false;
    inp.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && !e.repeat) enterArmed = true;
    });
    inp.addEventListener('keyup', (e) => {
      if (e.key !== 'Enter' || !enterArmed) return;
      enterArmed = false;
      cdpEval();
    });
    inp.addEventListener('blur', () => { enterArmed = false; });
    evalRow.append(inp, document.createTextNode(' '), elh('button', { class: 'df-dock-btn', text: 'Run', onclick: cdpEval }));
    frag.append(evalRow);
    frag.append(elh('div', { id: 'df-cdp-out' }));
    dockBodyEl.replaceChildren(frag);
  }

  async function cdpViewSource() {
    const out = document.getElementById('df-cdp-out');
    if (out) out.textContent = 'Loading…';
    const j = await apiPost('/api/cdp/source', { webviewId: cdpWebviewId });
    if (out) out.replaceChildren(elh('pre', { class: 'df-log-row', text: (j && j.ok && j.source != null) ? String(j.source) : ((j && j.error) || 'No source.') }));
  }

  async function cdpEval() {
    const inp = document.getElementById('df-cdp-expr');
    const out = document.getElementById('df-cdp-out');
    const expr = inp ? inp.value : '';
    if (!expr) return;
    if (!ensureCanDrive()) return;
    const targetWebViewId = cdpWebviewId;
    const confirmed = await confirmModal(
      'Run this JavaScript in the selected LIVE WebView? It can read or change application data.',
      'Run JavaScript');
    if (!confirmed) return;
    if (out) out.textContent = 'Running…';
    const j = await apiPost('/api/cdp/eval', { expression: expr, webviewId: targetWebViewId });
    if (out) out.replaceChildren(jsonView(j && j.ok ? j.result : ((j && j.error) || 'evaluate failed')));
  }

  async function loadTab(name) {
    const requestedTab = dockTabButtons.find(button =>
      !button.hidden && button.getAttribute('data-tab') === name);
    if (!requestedTab) return;
    layoutPanel?.deactivate();
    const generation = ++dockViewGeneration;
    dockActiveTab = name;
    networkDetailId = null;
    clearDockSnapshot();
    for (const b of dockTabsEl.querySelectorAll('.df-dock-tab')) {
      const active = b.getAttribute('data-tab') === name;
      b.classList.toggle('df-active', active);
      b.setAttribute('aria-selected', String(active));
      b.tabIndex = active ? 0 : -1;
      if (active && b.id) dockBodyEl.setAttribute('aria-labelledby', b.id);
    }
    syncPaneChrome();
    dockEmpty('Loading…');
    setDockMeta('loading…');
    try {
      await tabLoaders[name](generation);
      if (!dockLoadIsCurrent(name, generation)) return;
      setDockMeta(name === 'layout' ? 'explicit scan / opt-in live' :
        (name === 'network' ? 'live · updated ' : 'captured ') + new Date().toLocaleTimeString());
    }
    catch (e) {
      if (!dockLoadIsCurrent(name, generation)) return;
      dockEmpty('Failed to load.');
      setDockMeta('');
    }
  }

  function openDock() {
    const active = document.activeElement;
    if (active instanceof HTMLElement && active !== document.body && !dockEl.contains(active))
      dockReturnFocus = active;
    if (isTransientPaneLayout()) {
      propertyGrid.close();
      if (isTreeDrawerLayout()) setTreeVisible(false);
    }
    dockEl.classList.remove('df-hidden');
    document.body.classList.add('df-dock-open');
    document.body.classList.remove('df-dock-collapsed');
    if (dockCollapseBtn) {
      dockCollapseBtn.setAttribute('aria-expanded', 'true');
      dockCollapseBtn.title = 'Collapse data panel';
    }
    if (toggleDockBtn) {
      toggleDockBtn.classList.add('df-active');
      toggleDockBtn.setAttribute('aria-pressed', 'true');
    }
    if (!dockLoaded) { dockLoaded = true; loadTab(dockActiveTab); }
    else if (dockActiveTab === 'layout') layoutPanel?.activate(dockBodyEl);
    syncPaneChrome();
  }
  function closeDock(restore = false) {
    layoutPanel?.deactivate();
    dockEl.classList.add('df-hidden');
    document.body.classList.remove('df-dock-open', 'df-dock-collapsed');
    if (toggleDockBtn) {
      toggleDockBtn.classList.remove('df-active');
      toggleDockBtn.setAttribute('aria-pressed', 'false');
    }
    syncPaneChrome();
    if (restore) restoreFocus(dockReturnFocus, tb.more || toggleDockBtn);
  }
  function toggleDockCollapsed() {
    const collapsed = document.body.classList.toggle('df-dock-collapsed');
    if (dockActiveTab === 'layout') {
      if (collapsed) layoutPanel?.deactivate();
      else layoutPanel?.activate(dockBodyEl);
    }
    if (dockCollapseBtn) {
      dockCollapseBtn.setAttribute('aria-expanded', String(!collapsed));
      dockCollapseBtn.title = collapsed ? 'Expand data panel' : 'Collapse data panel';
    }
    syncPaneChrome();
  }
  if (toggleDockBtn) toggleDockBtn.addEventListener('click', () => (dockEl.classList.contains('df-hidden') ? openDock() : closeDock()));
  if (dockCloseBtn) dockCloseBtn.addEventListener('click', () => closeDock(true));
  if (dockCollapseBtn) dockCollapseBtn.addEventListener('click', toggleDockCollapsed);
  if (dockRefreshBtn) dockRefreshBtn.addEventListener('click', () => {
    if (dockActiveTab === 'layout') layoutPanel?.refresh();
    else if (dockActiveTab === 'network' && networkDetailId) loadNetworkDetail(networkDetailId);
    else loadTab(dockActiveTab);
  });
  if (attachDataBtn) attachDataBtn.addEventListener('click', attachDockDataToCopilot);
  const dockTabButtons = [...dockTabsEl.querySelectorAll('.df-dock-tab')];
  for (const b of dockTabButtons) {
    b.addEventListener('click', () => loadTab(b.getAttribute('data-tab')));
    b.addEventListener('keydown', (e) => {
      const tabs = visibleDockTabs();
      const index = tabs.indexOf(b);
      let target = null;
      if (e.key === 'ArrowRight') target = tabs[(index + 1 + tabs.length) % tabs.length];
      else if (e.key === 'ArrowLeft') target = tabs[(index - 1 + tabs.length) % tabs.length];
      else if (e.key === 'Home') target = tabs[0];
      else if (e.key === 'End') target = tabs[tabs.length - 1];
      else return;
      e.preventDefault();
      target.focus();
      loadTab(target.getAttribute('data-tab'));
    });
  }
  setInterval(() => {
    if (networkPollIsActive()) loadNetworkList({ automatic: true, generation: dockViewGeneration });
  }, NETWORK_AUTO_REFRESH_MS);

  // ── Presence / single-writer coordination ──
  function renderWriterPresence(presenceState = presentation.presence) {
    const presence = document.getElementById('df-presence');
    if (!presence) return;
    const previousText = presence.textContent;
    const previousClass = presence.className;
    const scheduleIfChanged = () => {
      if (presence.textContent !== previousText || presence.className !== previousClass)
        scheduleToolbarLayout();
    };
    const setPresence = (icon, label) => {
      presence.innerHTML = `<svg class="df-ic"><use href="#${icon}"/></svg>`;
      const text = document.createElement('span');
      text.className = 'df-presence-label';
      text.textContent = label;
      presence.append(text);
    };
    if (!presenceState) {
      presence.replaceChildren();
      presence.className = 'df-presence';
      presence.title = 'No session currently controls the app.';
      scheduleIfChanged();
      return;
    }
    setPresence(presenceState.icon, presenceState.label);
    presence.title = presenceState.title;
    presence.className = 'df-presence'
      + (presenceState.tone === 'writer' ? ' df-writer'
        : presenceState.tone === 'read-only' ? ' df-readonly'
          : presenceState.tone === 'disconnected' ? ' df-disconnected' : '');
    scheduleIfChanged();
  }

  function setWriterUi(writer, heldByOther, holderLabel, expiresInMs, authority) {
    const previousPresentation = presentation;
    const announceTransition = leaseStateInitialized;
    leaseStateInitialized = true;
    const lostLease = isWriter && !writer;   // we were driving; another session just took over
    isWriter = !!writer;
    leaseHeldByOther = !!heldByOther;
    otherLeaseLabel = leaseHeldByOther && typeof holderLabel === 'string'
      ? holderLabel.trim().slice(0, 80) || null
      : null;
    otherLeaseExpiresInMs = leaseHeldByOther && expiresInMs != null && Number.isFinite(Number(expiresInMs))
      ? Number(expiresInMs)
      : null;
    leaseAuthority = typeof authority === 'string' ? authority : null;
    if (tb.takeControl) {
      tb.takeControl.dataset.dfBaseTitle = heldByOther
        ? `${otherLeaseLabel || 'Another session'} is driving this app; take control`
        : 'No session is driving this app; take control';
    }
    if (presentationReady) renderHealthPresentation(previousPresentation, announceTransition);
    // Recording is app-scoped: another valid lease holder can continue it after handoff. This tab
    // becomes a passive observer and can resume the existing recording if it takes control again.
    if (lostLease && recordingId) {
      setStatus('Read-only — recording continues under the session that took control.');
    }
  }
  async function control(action, force) {
    const requestVersion = ++controlRequestVersion;
    const j = await apiPost('/api/control', force ? { action, force: true } : { action });
    if (requestVersion !== controlRequestVersion) return j;
    if (j && j.ok !== false) {
      const wasWriter = isWriter;
      setWriterUi(j.youAreWriter, j.heldByOther, j.label, j.expiresInMs, j.authority);
      if ((j.youAreWriter && (!wasWriter || recordingId)) || (!j.youAreWriter && recordingId))
        await syncRecordingStatus();
    } else {
      const previousPresentation = presentation;
      const announceTransition = leaseStateInitialized;
      leaseStateInitialized = true;
      leaseAuthority = 'unknown';
      if (presentationReady) renderHealthPresentation(previousPresentation, announceTransition);
    }
    return j;
  }
  const takeControlEl = document.getElementById('df-take-control');
  if (takeControlEl) takeControlEl.addEventListener('click', async () => {
    if (leaseHeldByOther) {
      const owner = otherLeaseLabel || 'Another session';
      const confirmed = await confirmModal(
        `${owner} is currently driving this app. Taking control may interrupt their interaction or recording.`,
        'Take control');
      if (!confirmed) return;
    }
    await control('claim', true);
  });
  control('claim');   // optimistically claim the writer lease on load
  setInterval(() => { if (!document.hidden) control(isWriter ? 'heartbeat' : 'status'); }, 4000);
  // On refocus, immediately reconcile writer presence instead of waiting up to 4s for the next tick —
  // a backgrounded tab pauses its heartbeat, so its lease may have expired or been claimed elsewhere.
  document.addEventListener('visibilitychange', () => { if (!document.hidden) control(isWriter ? 'heartbeat' : 'status'); });

  // ── Toolbar wiring + init ──
  tb.interact.addEventListener('click', () => setMode('interact'));
  tb.inspect.addEventListener('click', () => setMode('inspect'));
  for (const button of [tb.interact, tb.inspect]) {
    button.addEventListener('keydown', (e) => {
      if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(e.key)) return;
      e.preventDefault();
      const modes = [tb.interact, tb.inspect].filter(candidate => !candidate.hidden);
      const index = modes.indexOf(button);
      const next = e.key === 'Home'
        ? modes[0]
        : (e.key === 'End'
          ? modes[modes.length - 1]
          : modes[(index + 1 + modes.length) % modes.length]);
      if (!next) return;
      next.focus({ preventScroll: true });
      if (next.getAttribute('aria-disabled') === 'true') {
        setStatus(commandStates[next.id]?.reason?.text || 'This mode is currently unavailable.');
        return;
      }
      setMode(next === tb.interact ? 'interact' : 'inspect');
    });
  }
  tb.tree.addEventListener('click', () => {
    setTreeVisible(document.body.classList.contains('df-tree-hidden'));
  });
  tb.bounds.addEventListener('click', () => {
    const on = document.body.classList.toggle('df-show-all');
    tb.bounds.classList.toggle('df-active', on);
    tb.bounds.setAttribute('aria-pressed', String(on));
  });

  function scheduleScreenshotResolution(logicalWidth, logicalHeight, renderedScale) {
    if (!screenshotResolutionSupported) return;
    const next = selectNativeAwareScreenshotScaleBucket(
      selectScreenshotScaleBucket({
        logicalWidth,
        logicalHeight,
        renderedWidth: logicalWidth * renderedScale,
        renderedHeight: logicalHeight * renderedScale,
        devicePixelRatio: window.devicePixelRatio || 1,
      }),
      learnedMaximumScreenshotScale);
    const previousDesiredScale = desiredScreenshotScale;
    desiredScreenshotScale = next;
    if (next === requestedScreenshotScale) {
      if (screenshotResolutionTimer) clearTimeout(screenshotResolutionTimer);
      screenshotResolutionTimer = null;
      return;
    }
    if (screenshotResolutionTimer && next === previousDesiredScale) return;
    if (screenshotResolutionTimer) clearTimeout(screenshotResolutionTimer);
    const delay = next > committedScreenshotScale
      ? SCREENSHOT_RESOLUTION_UPGRADE_DELAY_MS
      : SCREENSHOT_RESOLUTION_DOWNGRADE_DELAY_MS;
    screenshotResolutionTimer = setTimeout(async () => {
      screenshotResolutionTimer = null;
      if (pendingTapRequests > 0 || latestTap !== null) {
        scheduleScreenshotResolution(logicalWidth, logicalHeight, renderedScale);
        return;
      }
      if (desiredScreenshotScale === requestedScreenshotScale) return;
      requestedScreenshotScale = desiredScreenshotScale;
      scheduleRefresh(0, REFRESH_INTENT_EVIDENCE, true);
    }, delay);
  }
  // ── Inspector-only viewport navigation ──────────────────────────────────────
  const fitBtn = document.getElementById('df-toggle-fit');
  viewportController = createViewportController({
    document,
    window,
    viewport,
    stage,
    wrap: vpWrap,
    fitButton: fitBtn,
    zoomOutButton: document.getElementById('df-zoom-out'),
    zoomInButton: document.getElementById('df-zoom-in'),
    zoomPreset: document.getElementById('df-zoom-preset'),
    zoomIndicator: document.getElementById('df-zoom-indicator'),
    zoomAnnouncement: document.getElementById('df-zoom-announcement'),
    isEditing: () => !!activeEditor,
    isCommandEnabled: id => commandStates[id]?.enabled !== false,
    canNavigate: () => !isGesturing,
    onTransformChanged: ({ bounds, transform }) =>
      scheduleScreenshotResolution(bounds.contentWidth, bounds.contentHeight, transform.scale),
  });
  window.addEventListener('resize', () => {
    updateHostLayout();
    viewportController.scheduleLayout();
    scheduleToolbarLayout();
  });

  // ── Host theme sync (devflow:theme) ────────────────────────────────────────
  // A cross-origin iframe can't read the host's theme and prefers-color-scheme is unreliable across
  // the VS Code / Canvas boundary, so the host tells us over the authenticated bridge. Two knobs:
  //   mode:    'light' | 'dark' | 'system' -> pins <html data-theme> (or clears it for OS behavior)
  //   palette: { '--df-*': '<color>' }      -> overrides specific semantic tokens (e.g. VS Code colors)
  // Every palette value is whitelisted by key and strictly validated as a CSS color before it touches
  // the DOM, so a hostile/compromised host frame can't inject url()/expressions/rule-breaking values.
  const THEME_VARS = new Set(['--df-bg','--df-surface','--df-surface-2','--df-fg','--df-muted','--df-border','--df-border-subtle','--df-hover','--df-hover-row','--df-accent','--df-accent-fg','--df-selected','--df-selected-fg','--df-danger','--df-focus','--df-type','--df-name','--df-source','--df-success','--df-warn','--df-error','--df-outline-hover','--df-outline-select']);
  const HEX_RE = /^#(?:[0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})$/i;
  const FUNC_RE = /^(?:rgb|rgba|hsl|hsla)\(\s*[0-9.,%\/\s]+\)$/i;
  function sanitizeColor(v) {
    if (typeof v !== 'string') return null;
    const s = v.trim();
    if (!s || s.length > 48) return null;
    if (/[;{}@]|url\(|var\(|expression|javascript:|<|>/i.test(s)) return null;
    return (HEX_RE.test(s) || FUNC_RE.test(s)) ? s : null;
  }
  function applyTheme(t) {
    if (!t || typeof t !== 'object') return;
    const root = document.documentElement;
    if (t.mode === 'light' || t.mode === 'dark') root.dataset.theme = t.mode;
    else if (t.mode === 'system' || t.mode === 'auto' || t.mode === null) delete root.dataset.theme;
    if (t.palette && typeof t.palette === 'object') {
      for (const k of Object.keys(t.palette)) {
        const key = k.charAt(0) === '-' ? k : ('--df-' + k);
        if (!THEME_VARS.has(key)) continue;
        const col = sanitizeColor(t.palette[k]);
        if (col) root.style.setProperty(key, col); else root.style.removeProperty(key);
      }
    }
  }

  // Escape closes the topmost transient host chrome. The inline editor owns Escape while active.
  document.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape' || activeEditor) return;
    if (document.body.classList.contains('df-more-open')) { setMoreOpen(false, false, true); return; }
    if (document.body.classList.contains('df-dock-open')) { closeDock(true); return; }
    if (propsPaneEl && !propsPaneEl.classList.contains('df-hidden')) { propertyGrid.close(); return; }
    if (isTreeDrawerLayout() && !document.body.classList.contains('df-tree-hidden')) setTreeVisible(false, true);
  });

  document.body.dataset.hostKind = hostKind;
  document.documentElement.dataset.hostKind = hostKind;
  updateHostLayout();
  scheduleToolbarLayout();
  elementTree.build();


  // Layout dock: the original list/detail/coverage UX over the current report contract.
  const diagnosticsPane = document.getElementById('df-diagnostics-pane');
  const diagnosticOverlays = document.getElementById('diagnostic-overlays');
  const diagnosticsToggle = document.getElementById('df-toggle-diagnostics');

  let latestDiagnostics = null;
  let latestRootOffsetX = 0;
  let latestRootOffsetY = 0;
  layoutPanel = createLayoutPanel({
    document,
    scan: async options => {
      const result = await inspectorApi.postDetailed('/api/diagnostics/layout', options);
      if (!result.ok) throw new Error(result.body?.error || result.body?.message || result.error ||
        `Layout scan failed (HTTP ${result.status}).`);
      return result.body;
    },
    selection: () => selectedId,
    defaultRoot: () => layoutRootElementId([...viewport.querySelectorAll('.devflow-element')].map(element => ({
      id: element.getAttribute('data-id'),
      type: element.getAttribute('data-type'),
      parentId: element.getAttribute('data-parentId'),
      isVisible: element.getAttribute('data-isVisible') !== 'false',
    }))),
    status: setStatus,
    show: finding => {
      diagnosticOverlays.replaceChildren();
      clearDiagnosticHighlights();
      if (finding) selectDiagnosticFinding(finding, rootOffsetX, rootOffsetY);
    },
    hasSource: finding => elById(finding.element?.id)?.getAttribute('data-hasSource') === 'true',
    openSource: async finding => {
      const target = elById(finding.element?.id);
      if (!target || target.getAttribute('data-hasSource') !== 'true') {
        setStatus('Source mapping is no longer available for this element. Rescan first.');
        return;
      }
      selectElement(finding.element.id);
      await openSource();
    },
    copy: async (report, id, attach, stale) => {
      recordDockSnapshot('layout', id ? 'Selected Layout finding' : 'Layout diagnostics',
        layoutContextPayload(report, id), id ? 1 : report.findings.length,
        { stale: !!stale, snapshotCapturedAt: report.snapshot?.capturedAt });
      if (attach) await attachDockDataToCopilot();
      else {
        const ok = await copyText(JSON.stringify(dockSnapshot, null, 2));
        setStatus(ok ? 'Copied bounded Layout context.' : 'Could not copy Layout context.');
      }
    },
    suppress: async (finding, policyFilePath) => {
      if (!policyFilePath) {
        setStatus('Layout suppressions require the inspected project\'s full path.');
        return false;
      }
      if (!await confirmModal(
        `${finding.suppressed ? 'Remove' : 'Save'} this Layout suppression?\nThis changes ${policyFilePath}`,
        finding.suppressed ? 'Remove suppression' : 'Save suppression')) return false;
      const result = await inspectorApi.postDetailed(`/api/diagnostics/${finding.suppressed ? 'unsuppress' : 'suppress'}`, {
        findingId: finding.id,
        reason: finding.suppressed ? null : 'Reviewed in DevFlow Inspector',
        policyFilePath,
      });
      if (!result.ok || result.body?.success !== true) {
        setStatus(result.body?.error || result.body?.message || result.error || 'The Layout policy could not be changed.');
        return false;
      }
      return true;
    },
    changed: (report, stale) => {
      latestDiagnostics = report;
      const tab = document.getElementById('df-tab-layout');
      if (tab) tab.textContent = report ? `Layout (${report.summary?.violations || 0})` : 'Layout';
      if (dockActiveTab !== 'layout') return;
      if (!report || stale) clearDockSnapshot();
      else recordDockSnapshot('layout', 'Layout diagnostics', layoutContextPayload(report), report.findings.length);
    },
  });

  function selectDiagnosticFinding(finding, rootOffsetX, rootOffsetY) {
    diagnosticOverlays.replaceChildren();
    clearDiagnosticHighlights();
    highlightElement(finding.element?.id, 'diagnostic-selected', true);
    for (const related of finding.relatedElements || [])
      highlightElement(related.element?.id, 'diagnostic-related-highlight', false);

    addDiagnosticRegion(finding.evidence?.fullRegion, 'diagnostic-region-full', rootOffsetX, rootOffsetY);
    addDiagnosticRegion(finding.evidence?.visibleRegion, 'diagnostic-region-visible', rootOffsetX, rootOffsetY);
    addDiagnosticRegion(finding.evidence?.parentRegion, 'diagnostic-region-parent', rootOffsetX, rootOffsetY);
    for (const clip of finding.evidence?.clipChain || [])
      addDiagnosticRegion(clip.region, 'diagnostic-region-clip', rootOffsetX, rootOffsetY);
    addDiagnosticRegion(finding.evidence?.overlap?.intersectionRegion, 'diagnostic-region-overlap', rootOffsetX, rootOffsetY);
    addOverflowEdges(finding.evidence, rootOffsetX, rootOffsetY);
  }

  function addDiagnosticRegion(region, className, rootOffsetX, rootOffsetY) {
    const bounds = region?.bounds;
    if (!bounds || bounds.width <= 0 || bounds.height <= 0) return;
    if (region.points?.length >= 3) {
      const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      svg.classList.add('diagnostic-region');
      Object.assign(svg.style, { position: 'absolute', inset: '0', overflow: 'visible' });
      const polygon = document.createElementNS('http://www.w3.org/2000/svg', 'polygon');
      polygon.setAttribute('class', className);
      polygon.setAttribute('points', region.points
        .map(point => `${point.x - rootOffsetX},${point.y - rootOffsetY}`)
        .join(' '));
      svg.appendChild(polygon);
      diagnosticOverlays.appendChild(svg);
      return;
    }

    const overlay = document.createElement('div');
    overlay.className = `diagnostic-region ${className}`;
    Object.assign(overlay.style, {
      left: `${bounds.x - rootOffsetX}px`,
      top: `${bounds.y - rootOffsetY}px`,
      width: `${bounds.width}px`,
      height: `${bounds.height}px`
    });
    diagnosticOverlays.appendChild(overlay);
  }

  function addOverflowEdges(evidence, rootOffsetX, rootOffsetY) {
    const bounds = evidence?.fullRegion?.bounds;
    const insets = evidence?.overflowInsetsPhysicalPixels;
    if (!bounds || !insets) return;
    const scale = latestDiagnostics?.snapshot?.windows?.[0]?.scale || 1;
    const edges = [
      ['left', insets.left, bounds.x, bounds.y, 3, bounds.height],
      ['top', insets.top, bounds.x, bounds.y, bounds.width, 3],
      ['right', insets.right, bounds.x + bounds.width - 3, bounds.y, 3, bounds.height],
      ['bottom', insets.bottom, bounds.x, bounds.y + bounds.height - 3, bounds.width, 3]
    ];
    for (const [, pixels, x, y, width, height] of edges) {
      if ((pixels || 0) < 1) continue;
      const edge = document.createElement('div');
      edge.className = 'diagnostic-region diagnostic-region-overlap';
      Object.assign(edge.style, {
        left: `${x - rootOffsetX}px`,
        top: `${y - rootOffsetY}px`,
        width: `${Math.max(width, pixels / scale)}px`,
        height: `${Math.max(height, pixels / scale)}px`
      });
      diagnosticOverlays.appendChild(edge);
    }
  }

  function clearDiagnosticHighlights() {
    viewport.querySelectorAll('.diagnostic-selected, .diagnostic-related-highlight')
      .forEach(element => {
        element.classList.remove('diagnostic-selected');
        element.classList.remove('diagnostic-related-highlight');
      });
  }

  function highlightElement(elementId, className, scroll) {
    if (!elementId) return;
    const target = [...viewport.querySelectorAll('.devflow-element')]
      .find(element => element.dataset.id === elementId);
    if (!target) return;
    target.classList.add(className);
    if (scroll)
      viewportController?.revealElement(target);
  }

  function applyDiagnosticsDelta(delta) {
    if (delta) layoutPanel?.invalidate('Layout evidence changed. Rescan to review the current report.');
  }

  function setDiagnosticsPaneVisible(visible) {
    if (visible) {
      const loadLayout = dockActiveTab !== 'layout';
      dockLoaded = true;
      openDock();
      if (loadLayout) loadTab('layout');
    } else {
      closeDock(true);
    }
  }
  diagnosticsToggle?.addEventListener('click', () =>
    setDiagnosticsPaneVisible(!isDiagnosticsPaneVisible()));

  presentationReady = true;
  renderHealthPresentation(null, false);
  settleInitialScreenshot();
  refreshCapabilities();
  setInterval(() => {
    if (!document.hidden) refreshCapabilities();
  }, CAPABILITY_REFRESH_MS);

})();
