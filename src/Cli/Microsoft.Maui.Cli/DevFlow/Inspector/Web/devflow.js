// DevFlow Web Inspector — Interaction Script
// Composition root: coordinates browser events, app transport, recording, hosts, and feature modules.
import { createInspectorApi } from './inspector-api.js';
import { confirmModal } from './inspector-dialog.js';
import { createDataSnapshot, isSecretContextKey, supportsDataContextScope } from './inspector-data-context.js';
import { createDataDockController } from './inspector-data-controller.js';
import { createDataUi } from './inspector-data-ui.js';
import { chooseLayoutScopeRoot, createLayoutDataPayload, diffLayoutReports, formatLayoutReport, formatPerformanceSummary } from './inspector-diagnostics.js';
import { createEvidenceController } from './inspector-evidence.js';
import { createAgentRequestController } from './inspector-agent-requests.js';
import { createInspectorHostBridge } from './inspector-host-bridge.js';
import { createPropertyGridController } from './inspector-properties.js';
import { createElementTreeController } from './inspector-tree.js';
import { createInspectorWorkbench } from './inspector-workbench.js';
import { createPrototypeStudyJournal } from './inspector-study.js';
import { mergeRecordedFlow } from './inspector-steps.js';

(function () {
  'use strict';

  const viewport = document.getElementById('app-viewport');
  const screenshot = document.getElementById('screenshot');
  const layoutOverlays = document.getElementById('df-layout-overlays');
  // Fit-to-container scaling (responsive). #app-viewport holds the app at its fixed logical size;
  // #df-stage is sized to the scaled box and #app-viewport carries the CSS scale transform.
  const stage = document.getElementById('df-stage');
  const vpWrap = document.getElementById('df-viewport-wrap');
  const disconnectedOverlay = document.getElementById('df-disconnected-overlay');
  const retryConnectionBtn = document.getElementById('df-retry-connection');
  let fitMode = true;   // default: scale down to fit (never upscale past 1:1)
  let scaleRaf = 0;
  let rootOffsetX = parseFloat(viewport.dataset.rootOffsetX) || 0;
  let rootOffsetY = parseFloat(viewport.dataset.rootOffsetY) || 0;

  // Determine base path for API calls (handles being served under /inspector/{id}/)
  const basePath = location.pathname.replace(/\/$/, '');
  const prototypeStudyJournal = createPrototypeStudyJournal();
  // Per-inspector read token for data tabs, injected into the page by InspectorServer. Same-origin
  // only — a cross-origin page can't set this custom header without a preflight the broker refuses.
  const inspectorToken = (document.querySelector('meta[name="devflow-inspector-token"]') || {}).content || '';
  const inspectorApi = createInspectorApi(basePath, inspectorToken);
  const apiPost = inspectorApi.post;
  function inspectorRequestHeaders(contentType) {
    const headers = {};
    if (contentType) headers['Content-Type'] = contentType;
    if (inspectorToken) headers['X-DevFlow-Inspector-Token'] = inspectorToken;
    return headers;
  }
  const hostBridge = createInspectorHostBridge(window);
  let testWorkbench = null;
  let agentRequestController = null;
  function metaContent(name) {
    const meta = document.querySelector(`meta[name="${name}"]`);
    return meta && typeof meta.content === 'string' && meta.content ? meta.content : null;
  }
  function metaFlag(name) {
    return metaContent(name) === 'true';
  }
  const previewFeatures = Object.freeze({
    workbench: metaFlag('devflow-preview-workbench'),
    agentAuthoring: metaFlag('devflow-preview-agent-authoring'),
    repair: metaFlag('devflow-preview-repair'),
    source: metaFlag('devflow-preview-source'),
    traceImport: metaFlag('devflow-preview-trace-import'),
  });
  // Optional panels whose routes only some broker layers serve. The server advertises the ones it
  // actually routes; an absent meta means the panel stays hidden rather than fetching a 404.
  const inspectorSurfaces = Object.freeze({
    layoutDiagnostics: metaFlag('devflow-surface-layout-diagnostics'),
    deviceHost: metaFlag('devflow-surface-device-host'),
  });
  const inspectorAgent = Object.freeze({
    id: metaContent('devflow-agent-id'),
    instanceId: metaContent('devflow-agent-instance-id'),
    appName: metaContent('devflow-app-name'),
    platform: metaContent('devflow-platform'),
    port: Number(metaContent('devflow-agent-port')) || null,
  });
  // A per-tab writer token identifies this session for the single-writer lock. A global fetch
  // wrapper stamps it on every same-origin /api/ call and flips to read-only on a writer 409.
  let writerToken = (window.crypto && crypto.randomUUID) ? crypto.randomUUID() : ('w' + Math.random().toString(36).slice(2) + Date.now());
  let leaseHolderKind = 'web';
  let leaseHolderLabel = 'Browser Inspector';
  let isWriter = false;
  let leaseHeldByOther = false;
  let otherLeaseLabel = null;
  let otherLeaseExpiresInMs = null;
  let hostInteractionAdopted = false;
  let interactionAdoptionGeneration = 0;
  let connected = true;   // a live app is reachable (derived from /api/state); gates the drive-actions
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
      if (resp.status === 409) { resp.clone().json().then((j) => { if (j && j.reason === 'writer') { setWriterUi(false, true, j.label, j.expiresInMs); setStatus('Read-only — another session is driving. Take control to interact.'); } }).catch(() => {}); }
      return resp;
    }
    return _origFetch(url, opts);
  };
  window.addEventListener('pagehide', () => {
    stopDeviceVideo();
    clearManagedDeviceScreenshot();
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

  async function adoptHostInteractionSession(sessionId) {
    if (typeof sessionId !== 'string' || !sessionId || sessionId === writerToken) return;
    const generation = ++interactionAdoptionGeneration;
    hostInteractionAdopted = true;
    const previousToken = writerToken;
    const releasePrevious = isWriter;
    writerToken = sessionId;
    isWriter = false;
    if (releasePrevious) {
      try {
        await _origFetch(`${basePath}/api/control`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'X-DevFlow-Lease': previousToken,
            'X-DevFlow-Writer': previousToken,
            'X-DevFlow-Holder': leaseHolderKind,
            'X-DevFlow-Label': leaseHolderLabel,
          },
          body: JSON.stringify({ action: 'release' }),
        });
      } catch {
        // The previous lease expires automatically; continue adopting the host session.
      }
    }
    if (generation === interactionAdoptionGeneration)
      await control('claim');
  }

  let gesturePoints = [];
  let isGesturing = false;
  let isDragging = false;
  let refreshInProgress = false;

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
  let appendRecordingBase = null;
  // Replay + checkpoint (return-to-start-route) state.
  let lastMarkdown = null;
  let lastMarkdownName = null;
  let lastMarkdownSource = null;
  let replaying = false;
  let authoringLoadGeneration = 0;
  let lastWorkbenchCanDrive = null;
  const pendingRecordingWork = new Set();
  // An imported trace is a captured diagnostic artifact, not an execution authority. This guard
  // sits below the UI so pointer, keyboard, property, and data mutations fail closed too.
  let capturedTraceMode = false;
  // Human-authoring drafts stay in the shared Inspector composition root. The Plan and Steps
  // modules only render/edit this state; canonical validation and persistence remain in C#.
  const authoringDraft = {
    flowName: null,
    markdown: null,
    flow: null,
    flowDigest: null,
    plan: null,
    planJson: null,
    planDigest: null,
    planRevision: null,
    committedFlowDigest: null,
    committedPlanDigest: null,
    committedPlanRevision: null,
    committedPlan: null,
    flowDirty: false,
    planDirty: false,
    saving: false,
    stale: false,
    bindingStale: false,
    errors: [],
    warnings: [],
    issues: [],
    diff: null,
    checkPassed: false,
    diffReviewed: false,
    attentionStepSequence: null,
    workspaceAvailable: null,
    rawDraft: false,
    recordingDraft: false,
    recordingDraftStepCount: 0,
    guidanceMessage: null,
    savedTestPickerOpen: false,
    savedTestsLoading: false,
    savedTests: [],
    savedTestsError: null,
  };

  // Convert browser (client) coordinates to app logical coordinates. The viewport may be fit-scaled
  // by a CSS transform (see applyScale), so getBoundingClientRect() returns the on-screen (scaled)
  // box. Map back into the app's own pixel space by the ratio of the logical size (data-width/height)
  // to the rendered size. This is scale-safe and reduces to (clientX - left) when shown 1:1.
  function toAppCoords(clientX, clientY) {
    const rect = viewport.getBoundingClientRect();
    const dw = parseFloat(viewport.dataset.width) || rect.width || 1;
    const dh = parseFloat(viewport.dataset.height) || rect.height || 1;
    const sx = rect.width ? dw / rect.width : 1;
    const sy = rect.height ? dh / rect.height : 1;
    return {
      x: (clientX - rect.left) * sx + rootOffsetX,
      y: (clientY - rect.top) * sy + rootOffsetY,
    };
  }

  // ── Device space ───────────────────────────────────────────────────────────────────────────
  // The Inspector's viewport is the APP WINDOW. When a device layer is present the app window is
  // inset within a larger device screen, so a click can land outside the app entirely — on a
  // system dialog, the soft keyboard, or OS navigation. Those coordinates belong to the device,
  // not the app.
  //
  // Everything about that second space lives here, beside toAppCoords, rather than being
  // recomputed in each pointer handler. Six handlers each doing their own arithmetic is how an
  // overlay ends up correct in portrait and subtly wrong in landscape.
  let deviceContext = null; // { deviceId, originX, originY, screenWidth, screenHeight, canTap }
  let stageScale = 1;

  function setDeviceContext(ctx) {
    const previous = deviceContext && deviceContext.deviceId;
    deviceContext = ctx && ctx.deviceId ? ctx : null;
    // Deliberately NOT cleared when a refresh fails: the device outlives the app, and the last
    // known context is what makes the app's death survivable rather than terminal.
    try {
      document.body.classList.toggle('df-device-live', hasDeviceLayer());
    } catch (e) { /* pre-body call during init */ }

    // Restart the stream when the device changes; leave it alone otherwise so a routine poll does
    // not tear down a live decoder.
    if (deviceContext && deviceContext.deviceId !== previous) startDeviceVideo();
    else if (!deviceContext) stopDeviceVideo();
  }

  // ── Live device video ──────────────────────────────────────────────────────────────────────
  // Replaces the polled screenshot with decoded H.264 where the device can stream it. Two
  // properties matter more than the picture itself:
  //
  //   * It never touches the agent. Frames come from the device host through the broker's proxy,
  //     so video costs the app nothing AND keeps arriving after the app has died.
  //   * It never drives the tree. Frame cadence and tree cadence stay separate — 50 frames a
  //     second must not mean 50 visual-tree pulls a second, so nothing here schedules a refresh.
  let videoSurface = null;
  let videoCanvas = null;
  let deviceFrameImage = null;
  let deviceFrameTimer = null;
  let deviceFrameObjectUrl = null;
  let videoGeneration = 0;

  function deviceVideoUrl() {
    if (!deviceContext || !deviceContext.canStream || !deviceContext.brokerPort) return null;
    // The embed token proves this is an Inspector session. Without it the broker refuses the
    // upgrade, which is what stops any other local page from opening a feed of the device.
    const embed = new URLSearchParams(location.search).get('embed');
    if (!embed) return null;

    const scheme = location.protocol === 'https:' ? 'wss' : 'ws';
    const params = new URLSearchParams({ deviceId: deviceContext.deviceId, fps: '30', embed });
    // Encode only what the panel can actually show. A narrow side panel should not pay to encode
    // a full 3x framebuffer.
    const box = deviceScreenBox();
    if (box && stage && stage.clientWidth > 0) {
      params.set('scale', String(Math.min(1, Math.max(0.1, stage.clientWidth / box.width))));
    }
    return `${scheme}://localhost:${deviceContext.brokerPort}/ws/video?${params.toString()}`;
  }

  async function startDeviceVideo() {
    stopDeviceVideo();
    const url = deviceVideoUrl();
    if (!stage) return;
    if (!url) {
      startDeviceScreenshotPolling();
      return;
    }

    // Device changes arrive on a poll, so two starts can overlap across the module fetch below.
    // Without a generation guard the loser keeps a live socket and decoder painting into the same
    // canvas, and its eventual failure would tear down the stream that actually won.
    const generation = ++videoGeneration;

    try {
      const mod = await import(`${basePath}/inspector-video.js`);
      if (generation !== videoGeneration) return;
      if (!mod.isVideoSupported(window)) {
        startDeviceScreenshotPolling();
        return;
      }

      if (!videoCanvas) {
        videoCanvas = document.createElement('canvas');
        videoCanvas.id = 'df-device-video';
        videoCanvas.hidden = true;
        stage.insertBefore(videoCanvas, stage.firstChild);
      }

      const surface = new mod.DeviceVideoSurface({
        url,
        canvas: videoCanvas,
        scope: window,
        onFrame: () => {
          if (generation !== videoGeneration) return;
          if (deviceFrameImage) deviceFrameImage.hidden = true;
          // Revealed only once a frame exists, so a stream that never starts leaves the stage as
          // it was rather than covering it in black.
          if (videoCanvas) videoCanvas.hidden = false;
          document.body.classList.add('df-video-live');
        },
        onUnavailable: () => {
          if (generation !== videoGeneration) return;
          stopDeviceVideo();
          startDeviceScreenshotPolling();
        },
      });

      videoSurface = surface;
      surface.start();
    } catch (e) {
      // Any failure here means the Inspector keeps doing exactly what it did before.
      if (generation === videoGeneration) {
        stopDeviceVideo();
        startDeviceScreenshotPolling();
      }
    }
  }

  function startDeviceScreenshotPolling() {
    if (!stage || !deviceContext?.canScreenshot) return;
    const generation = ++videoGeneration;
    if (!deviceFrameImage) {
      deviceFrameImage = document.createElement('img');
      deviceFrameImage.id = 'df-device-frame';
      deviceFrameImage.alt = 'Live device screenshot';
      deviceFrameImage.hidden = true;
      stage.insertBefore(deviceFrameImage, stage.firstChild);
    }

    const poll = async () => {
      if (generation !== videoGeneration || !deviceContext?.deviceId) return;
      if (!document.hidden) {
        try {
          const response = await fetch(
            `${basePath}/api/device/screenshot?deviceId=${encodeURIComponent(deviceContext.deviceId)}`,
            { headers: inspectorRequestHeaders(), cache: 'no-store' });
          if (response.ok) {
            const blob = await response.blob();
            if (generation !== videoGeneration) return;
            const nextUrl = URL.createObjectURL(blob);
            if (deviceFrameObjectUrl) URL.revokeObjectURL(deviceFrameObjectUrl);
            deviceFrameObjectUrl = nextUrl;
            deviceFrameImage.src = nextUrl;
            deviceFrameImage.hidden = false;
            document.body.classList.add('df-video-live');
          }
        } catch { }
      }
      if (generation === videoGeneration)
        deviceFrameTimer = setTimeout(poll, 1000);
    };
    poll();
  }

  function stopDeviceVideo() {
    videoGeneration++;
    try { if (videoSurface) videoSurface.stop(); } catch (e) { /* already stopped */ }
    videoSurface = null;
    if (deviceFrameTimer) clearTimeout(deviceFrameTimer);
    deviceFrameTimer = null;
    if (deviceFrameObjectUrl) URL.revokeObjectURL(deviceFrameObjectUrl);
    deviceFrameObjectUrl = null;
    if (deviceFrameImage) {
      deviceFrameImage.hidden = true;
      deviceFrameImage.removeAttribute('src');
    }
    document.body.classList.remove('df-video-live');
    // Hide AND clear: a hidden canvas still holds the last decoded frame, and revealing it later
    // for a different device would briefly show the previous one.
    if (videoCanvas) {
      videoCanvas.hidden = true;
      try {
        videoCanvas.getContext('2d').clearRect(0, 0, videoCanvas.width, videoCanvas.height);
      } catch (e) { /* no context yet */ }
    }
  }

  function hasDeviceLayer() {
    return !!(deviceContext &&
      (deviceContext.canTap || deviceContext.canStream || deviceContext.canScreenshot));
  }

  function canTapDevice() {
    return !!deviceContext?.canTap;
  }

  // Converts app logical coordinates to device points by adding the window's screen origin.
  // Returns null when we do not know where the window sits, in which case callers must not
  // fabricate a device coordinate — guessing an inset silently taps the wrong place.
  function toDeviceCoords(appX, appY) {
    if (!deviceContext) return null;
    const ox = Number(deviceContext.originX);
    const oy = Number(deviceContext.originY);
    if (!isFinite(ox) || !isFinite(oy)) return null;
    return { x: appX + ox, y: appY + oy };
  }

  // Whether an app-space point falls outside the app window, i.e. belongs to the OS rather than
  // the app. The viewport is sized to the window, so anything beyond its bounds is out of app.
  function isOutsideAppWindow(appX, appY) {
    const dw = parseFloat(viewport.dataset.width) || 0;
    const dh = parseFloat(viewport.dataset.height) || 0;
    if (!dw || !dh) return false;
    const x = appX - rootOffsetX;
    const y = appY - rootOffsetY;
    return x < 0 || y < 0 || x >= dw || y >= dh;
  }

  // Sends a tap to the device rather than the app. Used only when the app cannot service it:
  // outside the window, under a foreign window, or with no agent attached at all.
  //
  // Takes APP-space coordinates and converts them; callers already in device-screen space use
  // deviceTapScreen instead. Keeping both entry points here means the app→device conversion
  // exists exactly once.
  async function deviceTap(appX, appY) {
    const point = toDeviceCoords(appX, appY);
    if (!point) {
      setStatus('The app window position on the device is unknown, so a device tap cannot be placed.');
      return false;
    }
    return deviceTapScreen(point.x, point.y);
  }

  // Sends a tap at a point already in device-screen points.
  async function sendDeviceControl(action, values = {}) {
    if (!deviceContext?.deviceId || managedDeviceBusyAction) return false;
    try {
      const resp = await fetch(`${basePath}/api/device/control`, {
        method: 'POST',
        headers: inspectorRequestHeaders('application/json'),
        body: JSON.stringify({ action, deviceId: deviceContext.deviceId, ...values }),
      });
      const body = await resp.json().catch(() => null);
      if (!resp.ok || (body && body.success === false)) {
        setStatus((body && (body.reason || body.error)) || `The device refused ${action.replace(/-/g, ' ')}.`);
        return false;
      }
      if (recordingId) {
        if (action === 'tap') {
          await syncRecordingStatus();
          notifyAuthoring();
        } else {
          setStatus(`${action.replace(/-/g, ' ')} completed, but only direct device taps are recorded as flow steps.`);
        }
      }
      scheduleRefresh(300);
      return true;
    } catch (e) {
      setStatus('The device layer is unreachable.');
      return false;
    }
  }

  async function deviceTapScreen(x, y) {
    if (!isFinite(x) || !isFinite(y)) return false;
    return sendDeviceControl('tap', { x, y });
  }

  async function refreshState() {
    if (refreshInProgress) return;
    refreshInProgress = true;
    try {
      const resp = await fetch(`${basePath}/api/state`);
      if (!resp.ok) { markConnected(false); return; }
      const state = await resp.json();
      markConnected(true);
      // The device the app is running inside, when it is paired with one. Absent for desktop apps
      // and machines with no device host, in which case every device path stays disabled.
      setDeviceContext(state.device);
      // Cadence split: while video is live the screenshot is a stale duplicate of pixels the
      // stream is already painting, so skip the fetch entirely. This is why video makes the
      // Inspector cheaper rather than more expensive — the frame half of every poll disappears
      // and the tree half is unchanged.
      if (screenshot && state.screenshotUrl && !document.body.classList.contains('df-video-live')) {
        screenshot.src = state.screenshotUrl;
      }

      // Update viewport size if changed
      let viewportLayoutChanged = false;
      if (state.viewportWidth && state.viewportHeight) {
        viewportLayoutChanged = viewport.dataset.width !== String(state.viewportWidth)
          || viewport.dataset.height !== String(state.viewportHeight);
        viewport.style.width = state.viewportWidth + 'px';
        viewport.style.height = state.viewportHeight + 'px';
        viewport.dataset.width = state.viewportWidth;
        viewport.dataset.height = state.viewportHeight;
        applyScale();   // re-fit after an app resize/rotation changed the logical size
      }
      rootOffsetX = Number(state.rootOffsetX) || 0;
      rootOffsetY = Number(state.rootOffsetY) || 0;
      viewport.dataset.rootOffsetX = String(rootOffsetX);
      viewport.dataset.rootOffsetY = String(rootOffsetY);

      // Smart DOM diff — only update elements that changed, preserving hover/selection
      let elementLayoutChanged = false;
      if (state.elements) {
        elementLayoutChanged = patchElements(state.elements);
        onElementsUpdated();
      }
      if (viewportLayoutChanged || elementLayoutChanged) {
        markLayoutStale(viewportLayoutChanged
          ? 'state-refresh:viewport'
          : `state-refresh:elements:${lastElementPatchReason || 'unknown'}`);
      }
    } catch (err) {
      markConnected(false);
      console.error('State refresh failed:', err);
    } finally {
      refreshInProgress = false;
    }
  }

  // Reachability of the live app, derived from /api/state. Toggling it re-evaluates the drive-action
  // buttons so they disable when the agent goes away (and re-enable when it returns).
  const disconnectedStatus = 'App disconnected — waiting for DevFlow to reconnect.';
  let disconnectReturnFocus = null;
  function markConnected(value) {
    if (connected === value) return;
    connected = value;
    document.body.classList.toggle('df-disconnected', !value);
    if (disconnectedOverlay) {
      disconnectedOverlay.classList.toggle('df-hidden', value);
      disconnectedOverlay.setAttribute('aria-hidden', String(value));
    }
    if (!value) {
      disconnectReturnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
      markLayoutStale('disconnect');
    } else if (disconnectReturnFocus && disconnectReturnFocus.isConnected) {
      const active = document.activeElement;
      if (!active || active === document.body || active === document.documentElement ||
          disconnectedOverlay?.contains(active)) {
        disconnectReturnFocus.focus({ preventScroll: true });
      }
      disconnectReturnFocus = null;
    }
    if (value) scheduleLayoutLiveScan(0);
    renderWriterPresence();
    const status = document.getElementById('df-status');
    if (!value) setStatus(disconnectedStatus);
    else if (status && status.textContent === disconnectedStatus) setStatus('');
    updateFlowButtons();
  }
  if (retryConnectionBtn) retryConnectionBtn.addEventListener('click', async () => {
    if (refreshInProgress) {
      setStatus('Reconnect check queued…');
      scheduleRefresh(100);
      return;
    }
    retryConnectionBtn.disabled = true;
    setStatus('Checking for the running app…');
    try {
      await refreshState();
    } finally {
      retryConnectionBtn.disabled = false;
    }
  });

  // Keyed DOM diff: match elements by data-id, update in-place if changed
  let lastElementPatchReason = null;
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
    let changed = false;
    lastElementPatchReason = null;
    oldEls.forEach(el => {
      const id = el.getAttribute('data-id');
      if (id) oldMap.set(id, el);
    });

    // Remove elements that no longer exist
    oldMap.forEach((el, id) => {
      if (!newMap.has(id)) {
        el.remove();
        changed = true;
        lastElementPatchReason ??= 'removed';
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
          changed = true;
          lastElementPatchReason ??= 'style';
        }
        // Sync data attributes
        const dataChanged = syncDataAttrs(oldEl, newEl);
        changed = dataChanged || changed;
        if (dataChanged) lastElementPatchReason ??= 'data';
        // Ensure correct order
        if (prevEl && prevEl.nextElementSibling !== oldEl) {
          prevEl.after(oldEl);
          changed = true;
          lastElementPatchReason ??= 'order';
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
        changed = true;
        lastElementPatchReason ??= 'added';
      }
    }
    return changed;
  }

  // Sync data-* attributes from src to dst without replacing the element
  function syncDataAttrs(dst, src) {
    let changed = false;
    // Remove old data attrs not in src
    for (const attr of [...dst.attributes]) {
      if (attr.name.startsWith('data-') && !src.hasAttribute(attr.name)) {
        dst.removeAttribute(attr.name);
        changed = true;
      }
    }
    // Set/update from src
    for (const attr of src.attributes) {
      if (attr.name.startsWith('data-') && dst.getAttribute(attr.name) !== attr.value) {
        dst.setAttribute(attr.name, attr.value);
        changed = true;
      }
    }
    return changed;
  }

  // Debounced refresh — coalesce rapid calls
  let refreshTimer = null;
  function scheduleRefresh(delayMs) {
    if (refreshTimer) clearTimeout(refreshTimer);
    refreshTimer = setTimeout(() => {
      refreshTimer = null;
      refreshState();
    }, delayMs || 300);
  }
  if (screenshot) screenshot.addEventListener('error', () => scheduleRefresh(3000));

  // ── Click → Tap (with text-input awareness) ──
  // Element types that should open a text editor instead of just tapping.
  const TEXT_INPUT_TYPES = new Set([
    'Entry', 'Editor', 'SearchBar', 'SearchHandler',
    'TextField', 'TextBox', 'TextArea', 'TextView',
    'UITextField', 'UITextView',
    'EditText', 'NSTextField',
  ]);

  function isTextInput(el) {
    if (!el || !el.classList || !el.classList.contains('devflow-element')) return false;
    const type = el.dataset.type || '';
    if (TEXT_INPUT_TYPES.has(type)) return true;
    // Heuristic: traits often expose "TextInput" / "Editable"
    const traits = (el.dataset.traits || '').toLowerCase();
    return traits.includes('textinput') || traits.includes('editable');
  }

  function ensureCanDrive() {
    if (recordingStopping) {
      setStatus('Recording is stopping. Wait for the captured actions to finish.');
      return false;
    }
    if (capturedTraceMode) {
      setStatus('Captured result mode is read-only. Open a separate local run check to drive a live app.');
      return false;
    }
    if (!connected) {
      setStatus(disconnectedStatus);
      return false;
    }
    if (isWriter) return true;

    const owner = otherLeaseLabel || 'another session';
    setStatus(leaseHeldByOther
      ? `Read-only — ${owner} is driving. Take control to interact.`
      : 'Read-only — take control to interact.');
    const takeControl = document.getElementById('df-take-control');
    if (takeControl && !takeControl.classList.contains('df-hidden')) {
      takeControl.classList.add('df-attention');
      takeControl.focus({ preventScroll: true });
      setTimeout(() => takeControl.classList.remove('df-attention'), 1200);
    }
    return false;
  }

  // Overlay editor that we float on top of the clicked text element.
  let activeEditor = null;
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
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ x, y }),
      });
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

  function closeEditor(commit) {
    if (!activeEditor) return;
    const editor = activeEditor;
    activeEditor = null;
    if (commit && ensureCanDrive()) {
      const elementId = editor.dataset.elementId;
      const text = editor.value;
      const fillWork = fetch(`${basePath}/api/fill`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ elementId, text }),
      }).then(async (resp) => {
        if (recordingId && resp && resp.ok) await recordStepById('fill', elementId, { value: text });
        scheduleRefresh(300);
      }).catch(err => console.error('Fill failed:', err));
      if (recordingId) trackRecordingWork(fillWork);
    }
    editor.remove();
  }

  function openEditor(targetEl) {
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
    const editor = document.createElement(isMultiline ? 'textarea' : 'input');
    if (!isMultiline) editor.type = 'text';
    editor.value = targetEl.dataset.text || targetEl.dataset.value || '';
    editor.dataset.elementId = elementId;
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
    activeEditor = editor;
    // Use a microtask so the click that opened us doesn't immediately blur it.
    setTimeout(() => { editor.focus(); editor.select(); }, 0);
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
    const payload = { x, y };
    if (targetId) payload.elementId = targetId;

    const resp = await fetch(`${basePath}/api/tap`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
    const result = await resp.json().catch(() => null);
    if (!resp.ok || !result || result.ok !== true) {
      if (resp.status !== 409) {
        const reason = result && typeof result.reason === 'string'
          ? result.reason
          : 'The app did not accept the tap.';
        setStatus(`Tap did not run — ${reason}`);
      }
      return { ok: false, result, target: targetEl };
    }

    const actual = result.elementId ? (elById(result.elementId) || targetEl) : targetEl;
    pulseActionTarget(actual);
    const label = actual ? elementLabel(actual) : targetLabel;
    setStatus(label ? `Tap sent to ${label}.` : 'Tap sent to the app.');
    return { ok: true, result, target: actual || targetEl };
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
      const picked = underCursor && underCursor.closest ? underCursor.closest('.devflow-element') : null;
      await selectAtPoint(e.clientX, e.clientY, picked);
      return;
    }
    // Crash survival: the agent dies with the app, but the device does not. When the app is gone
    // and a device layer is present, a click still reaches the screen — which is what lets a user
    // dismiss a crash dialog or relaunch, instead of facing a frozen last frame.
    if (!connected && canTapDevice()) {
      const gone = toAppCoords(e.clientX, e.clientY);
      await deviceTap(gone.x, gone.y);
      return;
    }
    if (!ensureCanDrive()) return;

    let textEl = underCursor;
    while (textEl && textEl !== viewport && !isTextInput(textEl)) textEl = textEl.parentElement;
    if (textEl && textEl !== viewport && isTextInput(textEl)) {
      // Still send a tap so the native control gets focus on the app side.
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

    // Layer fallthrough. The user does one thing — click — and the highest-fidelity layer that
    // can service it is chosen automatically. A semantic tap is always preferred because it is
    // what produces a durable recorded selector; the device layer is the fallback that keeps the
    // session alive when the app cannot be the one to answer.
    if (canTapDevice() && (!connected || isOutsideAppWindow(x, y))) {
      const sent = await deviceTap(x, y);
      if (sent && recordingId) {
        setStatus('Tapped the device directly — not recorded, because it happened outside the app.');
      }
      return;
    }

    try {
      const tap = await sendInspectorTap(x, y, tapEl);
      if (recordingId) {
        if (tap.ok && tap.target) await recordStep('tap', tap.target);
        else if (!tap.target) setStatus('Tap not recorded — no element under the cursor.');
      }
      scheduleRefresh(400);
    } catch (err) {
      console.error('Tap failed:', err);
      setStatus('Tap did not run — the Inspector could not reach the app.');
    }
  });

  // Clicks on the device screen OUTSIDE the app window. This is the surface the app does not own:
  // the status bar, OS navigation, the soft keyboard, and any system dialog sitting on top. The
  // app agent cannot see or reach any of it, so these go straight to the device.
  //
  // Bound to the stage rather than the viewport because the viewport IS the app window — a click
  // that misses the app never reaches it.
  if (stage) {
    let devicePointerGesture = null;
    let suppressNextDeviceClick = false;
    stage.addEventListener('click', async (e) => {
      if (e.target !== stage) return;          // the viewport handles its own clicks
      if (suppressNextDeviceClick) {
        suppressNextDeviceClick = false;
        return;
      }
      if (isDragging) return;
      if (mode === 'inspect' || e.altKey || e.shiftKey) return;  // selection is an app concept
      if (!canTapDevice()) return;

      const dev = deviceScreenBox();
      if (!dev) return;

      // Stage-local pixels back to device points. The stage is the device screen, so this is a
      // plain unscale — no window origin involved, because we are already in screen space.
      const rect = stage.getBoundingClientRect();
      const s = stageScale > 0 ? stageScale : 1;
      const x = (e.clientX - rect.left) / s;
      const y = (e.clientY - rect.top) / s;

      await deviceTapScreen(x, y);
    });

    stage.addEventListener('pointerdown', (e) => {
      if (e.target !== stage || mode !== 'interact' || !canTapDevice()) return;
      const rect = stage.getBoundingClientRect();
      const scale = stageScale > 0 ? stageScale : 1;
      devicePointerGesture = {
        pointerId: e.pointerId,
        startX: (e.clientX - rect.left) / scale,
        startY: (e.clientY - rect.top) / scale,
        endX: (e.clientX - rect.left) / scale,
        endY: (e.clientY - rect.top) / scale,
        screenX: e.clientX,
        screenY: e.clientY,
        startedAt: performance.now(),
      };
      stage.setPointerCapture(e.pointerId);
    });
    stage.addEventListener('pointermove', (e) => {
      if (!devicePointerGesture || devicePointerGesture.pointerId !== e.pointerId) return;
      const rect = stage.getBoundingClientRect();
      const scale = stageScale > 0 ? stageScale : 1;
      devicePointerGesture.endX = (e.clientX - rect.left) / scale;
      devicePointerGesture.endY = (e.clientY - rect.top) / scale;
    });
    stage.addEventListener('pointerup', async (e) => {
      const gesture = devicePointerGesture;
      devicePointerGesture = null;
      if (!gesture || gesture.pointerId !== e.pointerId) return;
      const distance = Math.hypot(e.clientX - gesture.screenX, e.clientY - gesture.screenY);
      const duration = Math.max(0, (performance.now() - gesture.startedAt) / 1000);
      if (distance > 12 && deviceContext?.canSwipe) {
        suppressNextDeviceClick = true;
        await sendDeviceControl('swipe', {
          startX: gesture.startX,
          startY: gesture.startY,
          endX: gesture.endX,
          endY: gesture.endY,
          duration: Math.max(0.05, duration),
        });
      } else if (duration >= 0.65 && deviceContext?.canLongPress) {
        suppressNextDeviceClick = true;
        await sendDeviceControl('long-press', {
          x: gesture.startX,
          y: gesture.startY,
          duration,
        });
      }
    });
  }

  // ── Wheel → Scroll ──  let scrollAccumX = 0, scrollAccumY = 0;
  let scrollFlushTimer = null;
  let lastScrollX = 0, lastScrollY = 0;

  async function flushPendingScroll() {
    if (!scrollAccumX && !scrollAccumY) return;
    const { x, y } = toAppCoords(lastScrollX, lastScrollY);
    const dx = scrollAccumX, dy = scrollAccumY;
    scrollAccumX = 0;
    scrollAccumY = 0;
    scrollFlushTimer = null;

    try {
      const resp = await fetch(`${basePath}/api/scroll`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ x, y, deltaX: dx, deltaY: dy })
      });
      if (recordingId && resp.ok) await recordStep('scroll', null, { dx, dy });
      scheduleRefresh(300);
    } catch (err) {
      console.error('Scroll failed:', err);
    }
  }

  viewport.addEventListener('wheel', (e) => {
    e.preventDefault();
    if (!ensureCanDrive()) return;
    scrollAccumX += e.deltaX;
    scrollAccumY += e.deltaY;
    lastScrollX = e.clientX;
    lastScrollY = e.clientY;

    if (scrollFlushTimer) clearTimeout(scrollFlushTimer);
    scrollFlushTimer = setTimeout(() => {
      const work = flushPendingScroll();
      if (recordingId) trackRecordingWork(work);
    }, 100);
  }, { passive: false });

  // ── Pointer Drag → Gesture ──
  viewport.addEventListener('pointerdown', (e) => {
    if (!ensureCanDrive()) return;
    const { x, y } = toAppCoords(e.clientX, e.clientY);
    gesturePoints = [{ x, y, sx: e.clientX, sy: e.clientY, t: Date.now() }];
    isGesturing = true;
    isDragging = false;
    viewport.setPointerCapture(e.pointerId);
  });

  viewport.addEventListener('pointermove', (e) => {
    if (!isGesturing) return;
    const { x, y } = toAppCoords(e.clientX, e.clientY);
    gesturePoints.push({ x, y, sx: e.clientX, sy: e.clientY, t: Date.now() });
    // Drag-vs-tap decision uses on-screen (CSS px) distance so it behaves the same at any fit scale.
    const s0 = gesturePoints[0];
    if (Math.hypot(e.clientX - s0.sx, e.clientY - s0.sy) > 8) isDragging = true;
  });

  viewport.addEventListener('pointerup', async (e) => {
    if (!isGesturing) return;
    isGesturing = false;

    if (gesturePoints.length >= 2) {
      const first = gesturePoints[0];
      const last = gesturePoints[gesturePoints.length - 1];
      // Threshold on screen distance (scale-independent); send app-coordinate points to the app.
      const screenDist = Math.hypot(last.sx - first.sx, last.sy - first.sy);

      if (mode === 'interact' && screenDist > 12) {
        if (!ensureCanDrive()) {
          gesturePoints = [];
          setTimeout(() => { isDragging = false; }, 50);
          return;
        }
        try {
          await fetch(`${basePath}/api/gesture`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ points: gesturePoints.map((p) => ({ x: p.x, y: p.y, t: p.t })) })
          });
          scheduleRefresh(300);
          if (recordingId) setStatus('Gesture performed but not recorded (flow tests support tap/fill/scroll).');
        } catch (err) {
          console.error('Gesture failed:', err);
        }
      }
    }

    gesturePoints = [];
    setTimeout(() => { isDragging = false; }, 50);
  });

  // ── Live updates: a WebSocket to the broker-proxied /ws/events makes the mirror react instantly
  // to app-side changes. The 3s poll below stays as a zero-regression fallback and only refreshes
  // when the socket is NOT live (wsLive) — so if the WS never connects, behavior is exactly as before.
  let wsLive = false;
  let eventsWs = null;
  let eventConnectTimer = null;
  let eventSupportCheckInFlight = false;
  const EVENT_RETRY_MS = 3000;
  const EVENT_UNSUPPORTED_RECHECK_MS = 60000;

  function scheduleEventConnect(delay) {
    wsLive = false;
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
        scheduleEventConnect(EVENT_UNSUPPORTED_RECHECK_MS);
        return;
      }

      const wsUrl = (location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + basePath + '/ws/events';
      eventsWs = new WebSocket(wsUrl);
      eventsWs.onopen = () => {
        wsLive = true;
        if (!document.hidden && !replaying) scheduleRefresh(0);
      };
      eventsWs.onmessage = (event) => {
        let message = null;
        let type = null;
        try {
          message = JSON.parse(event.data);
          type = message.type || null;
        } catch { }
        if (type === 'problemsChange') {
          const problemsTab = document.getElementById('df-tab-problems');
          const problemsStatus = document.getElementById('df-problems-status');
          const count = Number(message && message.data && message.data.count);
          problemsTab?.classList.add('df-has-update');
          if (problemsTab) {
            problemsTab.setAttribute(
              'aria-label',
              Number.isFinite(count) ? `Problems, ${count} available` : 'Problems, updated');
          }
          if (problemsStatus) {
            problemsStatus.textContent = Number.isFinite(count)
              ? `${count} runtime UI problem${count === 1 ? '' : 's'} available.`
              : 'Runtime UI problems updated.';
          }
          if (!document.hidden && dockActiveTab === 'problems' && !dockEl.classList.contains('df-hidden'))
            loadTab('problems');
          return;
        }
        if (!document.hidden && !replaying
          && (!type || ['treeChange', 'navigation', 'lifecycle', 'themeChange', 'alert'].includes(type))) {
          markLayoutStale(`event:${type || 'unknown'}`);
          scheduleLayoutLiveScan(175);
          scheduleRefresh(150);
        }
      };
      eventsWs.onclose = () => { wsLive = false; eventsWs = null; scheduleEventConnect(EVENT_RETRY_MS); };
      eventsWs.onerror = () => { try { eventsWs && eventsWs.close(); } catch (e) { /* onclose reconnects */ } };
    } catch (e) {
      scheduleEventConnect(EVENT_RETRY_MS);
    } finally {
      eventSupportCheckInFlight = false;
    }
  }
  connectEvents();
  window.addEventListener('offline', () => markConnected(false));
  window.addEventListener('online', () => {
    if (!replaying) scheduleRefresh(0);
    if (!eventsWs || eventsWs.readyState >= WebSocket.CLOSING) connectEvents();
  });

  // ── Periodic refresh for app-side changes (AJAX, no flash) — fallback when the WS isn't live ──
  let pollInterval = setInterval(() => {
    if (!document.hidden && !refreshTimer && !wsLive) {
      refreshState();
    }
  }, 3000);

  document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
      clearInterval(pollInterval);
      pollInterval = null;
    } else if (!pollInterval) {
      if (!replaying) scheduleRefresh(0);
      pollInterval = setInterval(() => {
        if (!refreshTimer && !wsLive) refreshState();
      }, 3000);
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
  const propertyGrid = createPropertyGridController({
    pane: propsPaneEl,
    body: propsBodyEl,
    labelElement: propsElLabel,
    closeButton: propsCloseBtn,
    api: inspectorApi,
    getIsWriter: () => isWriter && connected && !capturedTraceMode,
    prepareOpen: () => {
      const active = document.activeElement;
      if (active instanceof HTMLElement && active !== document.body && !propsPaneEl.contains(active))
        propsReturnFocus = active;
      if (!isTransientPaneLayout()) return;
      if (isTreeDrawerLayout()) setTreeVisible(false);
      closeDock(false, true);
      testWorkbench?.close();
    },
    syncPaneChrome,
    setStatus,
    labelFor: elementLabel,
    getLayoutState: (elementId) => layoutHealthForElement(elementId),
    onSelectDiagnostic: (finding) => selectLayoutFinding(finding),
    onStartLayout: ({ elementId, force }) =>
      openLayoutDiagnostics({ elementId, forceScan: force === true }),
    onOpenLayout: ({ elementId }) => openLayoutDiagnostics({ elementId }),
    layoutDiagnosticsSupported: inspectorSurfaces.layoutDiagnostics,
    onOpen: () => {
      if (isTreeDrawerLayout() && propsReturnFocus && propsReturnFocus.classList.contains('df-tree-node'))
        propsPaneEl.focus({ preventScroll: true });
    },
    onClose: () => restoreFocus(propsReturnFocus, tb && tb.tree),
    onRuntimeChange: ({ elementId, name, value }) => {
      if (recordingId) recordStep('setProperty', elById(elementId), { name, value });
      markLayoutStale('property-change');
      scheduleLayoutLiveScan(175);
      scheduleRefresh(200);
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
    interact: document.getElementById('df-mode-interact'),
    inspect: document.getElementById('df-mode-inspect'),
    tree: document.getElementById('df-toggle-tree'),
    bounds: document.getElementById('df-toggle-bounds'),
    data: document.getElementById('df-toggle-dock'),
    dataStatus: document.getElementById('df-data-status'),
    more: document.getElementById('df-more'),
    secondary: document.getElementById('df-toolbar-secondary'),
    overflow: document.getElementById('df-toolbar-overflow'),
    status: document.getElementById('df-status'),
  };
  const toolbarActions = tb.secondary ? [...tb.secondary.querySelectorAll(':scope > button')] : [];
  const toolbarPriorities = new Map([
    ['df-evidence', 10],
    ['df-toggle-bounds', 20],
    ['df-toggle-fit', 30],
    ['df-open-source', 40],
    ['df-send-copilot', 45],
    ['df-toggle-tree', 70],
    ['df-toggle-dock', 90],
    ['df-toggle-workbench', 100],
  ]);
  toolbarActions.forEach((button, index) => {
    button.dataset.toolbarOrder = String(index);
    button.dataset.toolbarPriority = String(toolbarPriorities.get(button.id) || 40);
    if (button.hasAttribute('aria-pressed')) button.dataset.toolbarToggle = 'true';
  });
  let toolbarLayoutFrame = 0;
  const treePanel = document.getElementById('df-tree');
  const paneScrim = document.getElementById('df-pane-scrim');
  let hostIdentity = 'browser';
  let hostLayoutWidth = 'wide';
  let hostLayoutHeight = 'tall';
  let dockReturnFocus = null;

  function isFocusableVisible(element) {
    return element instanceof HTMLElement && element.isConnected && element.getClientRects().length > 0;
  }

  function restoreFocus(preferred, fallback) {
    const target = isFocusableVisible(preferred) ? preferred : (isFocusableVisible(fallback) ? fallback : null);
    if (target) target.focus({ preventScroll: true });
  }

  // Layout is a pure function of the geometry the Inspector was given. Host identity and declared
  // placement never select a layout, so two surfaces of the same size behave identically.
  // Width and height are independent axes: a wide-but-short window keeps its tree docked and only
  // gives up vertical chrome.
  function classifyLayoutWidth() {
    const width = document.documentElement.clientWidth || window.innerWidth || 1;
    if (width < 720) return 'narrow';
    if (width < 1400) return 'compact';
    return 'wide';
  }

  function classifyLayoutHeight() {
    const height = document.documentElement.clientHeight || window.innerHeight || 1;
    return height < 560 ? 'short' : 'tall';
  }

  // The properties pane becomes a transient drawer as soon as horizontal room is tight.
  function isTransientPaneLayout() {
    return hostLayoutWidth === 'compact' || hostLayoutWidth === 'narrow';
  }

  // The tree only becomes a drawer when there is genuinely no horizontal room for it.
  function isTreeDrawerLayout() {
    return hostLayoutWidth === 'narrow';
  }

  // Data dock and workflow timeline float as overlay sheets whenever either axis is tight, so they
  // never eat the screenshot's remaining budget.
  function isOverlayChrome() {
    return hostLayoutWidth !== 'wide' || hostLayoutHeight === 'short';
  }

  function setTreeVisible(visible, restore = false, preserveWorkbench = false) {
    if (visible && isTransientPaneLayout()) {
      setMoreOpen(false);
      if (document.body.classList.contains('df-dock-open')) closeDock();
      if (!preserveWorkbench) testWorkbench?.close();
    }
    document.body.classList.toggle('df-tree-hidden', !visible);
    if (tb.tree) {
      tb.tree.classList.toggle('df-active', visible);
      tb.tree.setAttribute('aria-expanded', String(visible));
      setToolbarToggleState(tb.tree, visible);
    }
    if (visible && isTreeDrawerLayout())
      propertyGrid.close();
    syncPaneChrome();
    if (!visible && restore) restoreFocus(tb.tree);
  }

  function overflowButtons() {
    return tb.overflow
      ? [...tb.overflow.querySelectorAll(':scope > button:not(:disabled)')]
      : [];
  }

  function moveToolbarAction(container, button) {
    const order = Number(button.dataset.toolbarOrder);
    const before = [...container.children].find(child =>
      Number(child.dataset.toolbarOrder) > order);
    container.insertBefore(button, before || null);
    const isToggle = button.dataset.toolbarToggle === 'true';
    const state = button.getAttribute('aria-pressed') ?? button.getAttribute('aria-checked') ?? 'false';
    if (container === tb.overflow) {
      button.setAttribute('role', isToggle ? 'menuitemcheckbox' : 'menuitem');
      if (isToggle) {
        button.setAttribute('aria-checked', state);
        button.removeAttribute('aria-pressed');
      }
    } else {
      button.removeAttribute('role');
      if (isToggle) {
        button.setAttribute('aria-pressed', state);
        button.removeAttribute('aria-checked');
      }
    }
  }

  function setToolbarToggleState(button, on) {
    if (!button) return;
    const state = String(!!on);
    if (tb.overflow?.contains(button)) button.setAttribute('aria-checked', state);
    else button.setAttribute('aria-pressed', state);
  }

  function toolbarFits() {
    const toolbar = document.getElementById('df-toolbar');
    return !!toolbar && toolbar.scrollWidth <= toolbar.clientWidth + 1;
  }

  function layoutToolbarActions() {
    toolbarLayoutFrame = 0;
    if (!tb.secondary || !tb.overflow || !tb.more) return;
    const focused = document.activeElement;
    const wasOpen = document.body.classList.contains('df-more-open');
    setMoreOpen(false);
    for (const button of toolbarActions)
      moveToolbarAction(tb.secondary, button);
    tb.more.classList.add('df-hidden');

    if (!toolbarFits()) {
      tb.more.classList.remove('df-hidden');
      while (!toolbarFits() && tb.secondary.children.length > 0) {
        const candidate = [...tb.secondary.children]
          .sort((a, b) =>
            Number(a.dataset.toolbarPriority) - Number(b.dataset.toolbarPriority) ||
            Number(b.dataset.toolbarOrder) - Number(a.dataset.toolbarOrder))[0];
        if (!candidate) break;
        moveToolbarAction(tb.overflow, candidate);
      }
    }

    if (tb.overflow.children.length === 0)
      tb.more.classList.add('df-hidden');
    if (focused instanceof HTMLElement && toolbarActions.includes(focused)) {
      if (tb.overflow.contains(focused))
        tb.more.focus({ preventScroll: true });
      else if (focused.isConnected)
        focused.focus({ preventScroll: true });
    }
    tb.more.classList.toggle('df-active', !!tb.overflow.querySelector('.df-tool-btn.df-active'));
    if (wasOpen && tb.overflow.children.length > 0)
      setMoreOpen(true);
  }

  function scheduleToolbarLayout() {
    if (toolbarLayoutFrame) cancelAnimationFrame(toolbarLayoutFrame);
    toolbarLayoutFrame = requestAnimationFrame(layoutToolbarActions);
  }

  function setMoreOpen(open, focusMenu = false, restore = false) {
    if (!tb.overflow || !tb.more || tb.overflow.children.length === 0)
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

  function syncPaneChrome() {
    const propsOpen = !!propsPaneEl && !propsPaneEl.classList.contains('df-hidden');
    const treeOpen = !document.body.classList.contains('df-tree-hidden');
    const dockOpen = document.body.classList.contains('df-dock-open');
    const showScrim =
      (isTreeDrawerLayout() && treeOpen) ||
      (isTransientPaneLayout() && propsOpen) ||
      (isOverlayChrome() && dockOpen);
    document.body.classList.toggle('df-props-open', propsOpen);
    if (paneScrim) paneScrim.classList.toggle('df-hidden', !showScrim);
  }

  function updateHostLayout() {
    const nextWidth = classifyLayoutWidth();
    const nextHeight = classifyLayoutHeight();
    if (nextWidth !== hostLayoutWidth ||
        nextHeight !== hostLayoutHeight ||
        !document.body.dataset.layoutWidth) {
      hostLayoutWidth = nextWidth;
      hostLayoutHeight = nextHeight;
      const chrome = isOverlayChrome() ? 'overlay' : 'docked';
      for (const root of [document.body, document.documentElement]) {
        root.dataset.layoutWidth = nextWidth;
        root.dataset.layoutHeight = nextHeight;
        root.dataset.layoutChrome = chrome;
      }
      setMoreOpen(false);
      setTreeVisible(!isTreeDrawerLayout(), false, true);
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
    // Declared placement is provenance only. It is never allowed to select a layout: two surfaces
    // of the same size must behave identically.
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
      if (button.getAttribute('aria-disabled') === 'true') {
        e.preventDefault();
        e.stopPropagation();
        setStatus(button.title || 'This action is currently unavailable.');
        return;
      }
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
  function setStatus(text) { if (tb.status) tb.status.textContent = text || ''; }
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

  function setMode(next) {
    if (capturedTraceMode && next === 'interact') {
      next = 'inspect';
      setStatus('Captured trace mode is read-only. Inspect remains available.');
    }
    setHover(null);
    mode = next;
    tb.interact.classList.toggle('df-active', next === 'interact');
    tb.inspect.classList.toggle('df-active', next === 'inspect');
    tb.interact.setAttribute('aria-checked', String(next === 'interact'));
    tb.inspect.setAttribute('aria-checked', String(next === 'inspect'));
    tb.interact.tabIndex = next === 'interact' ? 0 : -1;
    tb.inspect.tabIndex = next === 'inspect' ? 0 : -1;
    document.body.classList.toggle('df-mode-inspect', next === 'inspect');
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
    b.style.left = el.offsetLeft + 'px';
    b.style.top = Math.max(0, el.offsetTop - 18) + 'px';
    b.style.display = 'block';
  }
  function isInteractableOverlay(el) {
    if (!el || !el.classList || !el.classList.contains('devflow-element')) return false;
    return el.dataset.interactable === 'true';
  }
  function isTapTargetOverlay(el) {
    if (!isInteractableOverlay(el)) return false;
    return (el.dataset.traits || '').split(',').some((trait) => trait.trim().toLowerCase() === 'interactive');
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
  function selectElement(id) {
    viewport.querySelectorAll('.devflow-element.df-selected').forEach((el) => el.classList.remove('df-selected'));
    selectedId = id || null;
    const layoutScopeControl = document.getElementById('df-layout-selected-scope');
    if (layoutScopeControl) {
      layoutScopeControl.disabled = !selectedId;
      if (!selectedId) {
        layoutScopeControl.checked = false;
        layoutOptions.selectedScope = false;
      }
    }
    if (!id) {
      propertyGrid.close();
      elementTree.updateSelection();
      setStatus('');
      updateHostButtons();
      updateFlowButtons();
      if (testWorkbench?.isOpen?.()) testWorkbench.updateState({});
      postSelectionToHost(null);
      return;
    }
    const el = elById(id);
    if (el) el.classList.add('df-selected');
    elementTree.updateSelection();
    elementTree.reveal(id);
    if (el) { propertyGrid.open(el); setStatus(elementLabel(el)); }
    updateHostButtons();
    updateFlowButtons();
    if (testWorkbench?.isOpen?.())
      testWorkbench.updateState({});
    postSelectionToHost(el);
  }

  // Called after every patchElements(): rebuild the tree only on structural change,
  // and re-apply the selection highlight to the (possibly replaced) overlay div.
  function onElementsUpdated() {
    elementTree.syncStructure();
    if (selectedId) {
      const el = elById(selectedId);
      if (el) el.classList.add('df-selected'); else selectElement(null);
    }
    if (hoveredEl && !hoveredEl.isConnected) { hoveredEl = null; if (badgeEl) badgeEl.style.display = 'none'; }
  }

  // ── Workflow recording: capture interactions into the shared Flow format, then
  // download a replayable Markdown test. Reuses the broker's /api/flows/record/* endpoints, which
  // feed the same FlowRecorder/FlowMarkdown engine as the MCP maui_flow_record_* tools. ──
  // ── Timeline: a live strip of recorded step chips (shown while recording; each chip reselects its element) ──
  const timelineEl = document.getElementById('df-timeline');
  const timelineStepsEl = document.getElementById('df-timeline-steps');
  const timelineMetaEl = document.getElementById('df-timeline-meta');
  const timelineTitleText = document.getElementById('df-timeline-title-text');
  const cancelRecordingBtn = document.getElementById('df-record-cancel');
  const timelineCloseBtn = document.getElementById('df-timeline-close');
  const workflowFileInput = document.getElementById('df-workflow-file-input');
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
        if (a.kind === 'propEquals') return name + '.' + a.name + ' = ' + String(a.expected).slice(0, 18);
        return name + (a.kind === 'notExists' ? ' no longer exists' : ' exists');
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

  function updateRecordingUi() {
    if (cancelRecordingBtn) cancelRecordingBtn.classList.toggle('df-hidden', !recordingId);
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

  function trackRecordingWork(work) {
    const promise = Promise.resolve(work);
    pendingRecordingWork.add(promise);
    promise.finally(() => pendingRecordingWork.delete(promise));
    return promise;
  }

  function recordStep(action, el, extra) {
    const activeRecordingId = recordingId;
    if (!activeRecordingId) return Promise.resolve();
    const body = Object.assign({ recordingId: activeRecordingId, action }, selectorPayload(el), extra || {});
    return trackRecordingWork((async () => {
      try {
        const r = await fetch(`${basePath}/api/flows/record/step`, {
          method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
        });
        const j = r.ok ? await r.json().catch(() => null) : null;
        if (j && j.ok) {
          recStepCount = j.stepCount;
          updateRecordingUi();
          notifyAuthoring();
          timelineAdd(Number(j.seq) || j.stepCount, action, el, extra);
        }
      } catch (err) { console.error('record step failed:', err); }
    })());
  }

  function recordStepById(action, elementId, extra) {
    return recordStep(action, elById(elementId), extra);
  }

  const recordingCapabilityStorageKey = 'maui-devflow-recording-capabilities-v1';
  function savedRecordingCapabilities() {
    try {
      const parsed = JSON.parse(sessionStorage.getItem(recordingCapabilityStorageKey) || '[]');
      return Array.isArray(parsed)
        ? parsed.filter((id) => /^[a-f0-9]{24}$/.test(String(id))).slice(0, 16)
        : [];
    } catch {
      return [];
    }
  }
  function writeRecordingCapabilities(ids) {
    try {
      sessionStorage.setItem(recordingCapabilityStorageKey, JSON.stringify(ids.slice(0, 16)));
    } catch {
      // Storage can be unavailable in locked-down webviews; the in-memory capability still works.
    }
  }
  function rememberRecordingCapability(id) {
    if (!/^[a-f0-9]{24}$/.test(String(id || ''))) return;
    writeRecordingCapabilities([id, ...savedRecordingCapabilities().filter((item) => item !== id)]);
  }
  function forgetRecordingCapability(id) {
    if (!id) return;
    writeRecordingCapabilities(savedRecordingCapabilities().filter((item) => item !== id));
  }
  async function requestRecordingStatus(candidate) {
    const response = await fetch(`${basePath}/api/flows/record/status`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(candidate ? { recordingId: candidate } : {}),
    });
    return await response.json().catch(() => null);
  }

  async function startRecording(options = {}) {
    const quick = options.quick === true;
    const append = options.append === true;
    if (!isWriter || !connected || capturedTraceMode || replaying) {
      setStatus(capturedTraceMode
        ? 'Recording is unavailable while an imported result is open.'
        : !connected
          ? 'Recording requires a connected app.'
          : 'Take control of the app before recording steps.');
      return false;
    }
    if (!quick && !hasAuthoringGoal()) {
      openGoalForRecovery('Add a Goal before recording actions.');
      return false;
    }
    const appendBase = append
      ? {
        flow: cloneAuthoring(currentAuthoringFlow()),
        markdown: authoringDraft.markdown,
        flowName: authoringDraft.flowName,
      }
      : null;
    if (append && (!appendBase.flow || !Array.isArray(appendBase.flow.steps))) {
      setStatus('Open a recorded test before adding more steps.');
      return false;
    }
    const name = 'recording-' + new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    try {
      const r = await fetch(`${basePath}/api/flows/record/start`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name }),
      });
      const j = r.ok ? await r.json().catch(() => null) : null;
      if (j && j.ok) {
        recordingId = j.recordingId; recStepCount = Number(j.steps) || 0; recName = j.name;
        appendRecordingBase = appendBase;
        recordStudyEvent('recording-started', {
          quick,
          provenance: studyProvenance(),
        });
        authoringDraft.rawDraft = quick;
        authoringDraft.recordingDraft = false;
        authoringDraft.recordingDraftStepCount = 0;
        authoringDraft.guidanceMessage = quick
          ? 'Quick recording is active. This raw draft is not repair-authoritative.'
          : null;
        rememberRecordingCapability(recordingId);
        updateFlowButtons();
        timelineStart();
        setStatus(quick
          ? 'Quick recording — actions will be kept as a raw draft.'
          : append
            ? 'Recording more steps — interact with the app, then stop to append them.'
            : 'Recording — interact with the app; each action is captured.');
        notifyAuthoring();
        return true;
      } else {
        setStatus('Could not start recording.');
      }
    } catch (err) { setStatus('Could not start recording.'); }
    return false;
  }

  async function stopRecording(reason) {
    if (!recordingId || recordingStopping) return;
    const id = recordingId;
    const pre = reason ? reason + ' ' : '';   // optional prefix, e.g. when a lost writer lease forces the stop
    recordingStopping = true;
    updateFlowButtons();
    setStatus(pre + 'Stopping recording…');
    try {
      if (scrollFlushTimer) {
        clearTimeout(scrollFlushTimer);
        scrollFlushTimer = null;
        await flushPendingScroll();
      }
      while (pendingRecordingWork.size > 0)
        await Promise.allSettled([...pendingRecordingWork]);
      const r = await fetch(`${basePath}/api/flows/record/stop`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ recordingId: id }),
      });
      const j = await r.json().catch(() => null);
      if (!r.ok || !j || !j.ok) {
        const detail = (j && j.error) || `request failed (${r.status})`;
        if (/no recording is active|active recording no longer exists|unknown recordingid/i.test(detail)) {
          recordingId = null;
          forgetRecordingCapability(id);
          recStepCount = 0;
          recName = null;
          appendRecordingBase = null;
          dismissTimeline();
          setStatus(`${pre}Recording already ended in another session.`);
          updateHostButtons();
          return;
        }
        setStatus(`${pre}Could not stop recording: ${detail} Recording is still active; retry Stop.`);
        return;
      }

      recordStudyEvent('recording-stopped', {
        stepCount: Number(j.steps) || 0,
        provenance: studyProvenance(),
      });
      recordingId = null;
      forgetRecordingCapability(id);
      if (j.markdown) {
        let markdown = j.markdown;
        let fname = (j.name || recName || 'recording') + '.md';
        let totalSteps = Number(j.steps) || 0;
        if (appendRecordingBase?.flow) {
          const appended = parseAuthoringFlow(j.markdown);
          const merged = mergeRecordedFlow(appendRecordingBase.flow, appended);
          markdown = replaceAuthoringFlow(appendRecordingBase.markdown, merged);
          fname = appendRecordingBase.flowName || fname;
          totalSteps = merged.steps.length;
        }
        lastMarkdown = markdown;
        lastMarkdownName = fname;
        lastMarkdownSource = 'recording';
        syncAuthoringFlow(markdown, fname, 'recording');
        if (!authoringDraft.plan) authoringDraft.rawDraft = true;
        timelineStop(totalSteps);
        authoringDraft.recordingDraft = true;
        authoringDraft.recordingDraftStepCount = totalSteps;
        authoringDraft.guidanceMessage = authoringDraft.rawDraft
          ? 'Recording draft saved. Add a Goal before saving this raw draft as a managed test.'
          : 'Recording draft saved. Review the steps and save the test when it is ready.';
        testWorkbench?.openStage?.('review', true);
        workbenchAnnouncement(`Recording draft saved — ${totalSteps} step(s).`);
        setStatus(`${pre}Recording draft saved — ${totalSteps} step(s). Review and save it when ready.`);
        appendRecordingBase = null;
      } else {
        dismissTimeline();
        setStatus(pre + 'Recording stopped: no replayable steps.');
        appendRecordingBase = null;
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
      let status = null;
      let currentCapabilityEnded = false;
      const candidates = [...new Set(
        [recordingId, ...savedRecordingCapabilities()].filter(Boolean))];
      candidates.push(null);
      for (const candidate of candidates) {
        const candidateStatus = await requestRecordingStatus(candidate);
        if (candidateStatus && candidateStatus.ok && candidateStatus.recording) {
          status = candidateStatus;
          break;
        }
        if (candidate === recordingId && candidateStatus) {
          currentCapabilityEnded = (candidateStatus.ok === true && candidateStatus.recording === false)
            || /unknown recordingid|no recording is active|active recording no longer exists/i.test(
              candidateStatus.error || '');
        }
      }
      if (!status && currentCapabilityEnded && recordingId) {
        forgetRecordingCapability(recordingId);
        recordingId = null;
        recordingStopping = false;
        appendRecordingBase = null;
        dismissTimeline();
        setStatus('Recording ended in another session.');
        updateFlowButtons();
        updateHostButtons();
      }
      if (!status) return;

      if (status.recording) {
        const discovered = !recordingId;
        recordingId = status.recordingId || recordingId;
        rememberRecordingCapability(recordingId);
        recStepCount = Number(status.steps) || 0;
        recName = status.name || recName;
        if (discovered) timelineStart();
      }
      updateFlowButtons();
      updateHostButtons();
    } catch {
      // The regular connection state handles transport failures; keep the local draft visible.
    }
  }

  let hostRecordingSync = null;
  function syncHostRecordingStatus() {
    if (hostRecordingSync) return hostRecordingSync;
    hostRecordingSync = (async () => {
      await syncRecordingStatus();
      notifyAuthoring();
    })().finally(() => { hostRecordingSync = null; });
    return hostRecordingSync;
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

      recordStudyEvent('recording-stopped', {
        discarded: true,
        stepCount: recStepCount,
        provenance: studyProvenance(),
      });
      recordingId = null;
      forgetRecordingCapability(id);
      recStepCount = 0;
      recName = null;
      appendRecordingBase = null;
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

  function downloadText(filename, text, contentType = 'text/markdown') {
    try {
      const blob = new Blob([text], { type: contentType });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url; a.download = filename;
      document.body.appendChild(a); a.click(); a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
      return true;
    } catch (err) {
      console.error('download failed:', err);
      return false;
    }
  }

  function setLoadedWorkflow(markdown, name, source, steps, loadGeneration = null) {
    const normalized = normalizeLoadedWorkflowMarkdown(markdown);
    if (!normalized.ok) {
      setStatus(normalized.error || 'Could not load the selected workflow.');
      return { ok: false, migrated: false };
    }
    const generation = loadGeneration ?? ++authoringLoadGeneration;
    if (generation !== authoringLoadGeneration) return { ok: false, migrated: false };
    markdown = normalized.markdown;
    lastMarkdown = markdown;
    lastMarkdownName = String(name || 'workflow.md');
    lastMarkdownSource = source || 'file';
    authoringDraft.savedTestPickerOpen = false;
    authoringDraft.savedTestsLoading = false;
    authoringDraft.savedTestsError = null;
    syncAuthoringFlow(markdown, lastMarkdownName, lastMarkdownSource, generation);
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
    dismissTimeline();
    updateFlowButtons();
    updateHostButtons();
    return { ok: true, migrated: normalized.migrated === true };
  }

  async function loadProjectWorkflow(name) {
    if (!name) return;
    const generation = ++authoringLoadGeneration;
    setStatus(`Loading ${name}…`);
    const result = await apiPost('/api/flows/files/load', { name });
    if (generation !== authoringLoadGeneration) return;
    if (!result || result.ok !== true || typeof result.markdown !== 'string') {
      setStatus((result && result.error) || 'Could not load the selected project workflow.');
      return;
    }
    const loaded = setLoadedWorkflow(result.markdown, result.name || name, 'project', result.steps, generation);
    if (!loaded?.ok) return;
    setStatus(loaded.migrated
      ? `Loaded ${result.name || name} and normalized its legacy schemaVersion payload. Save the test to persist the migrated schema field.`
      : `Loaded ${result.name || name} from the project.`);
  }

  async function loadWorkflowFile(file) {
    if (!file) return;
    if (!/\.md$/i.test(file.name || '') || file.size > 1024 * 1024) {
      setStatus('Choose a Markdown workflow file smaller than 1 MB.');
      return;
    }
    try {
      const markdown = await file.text();
      const loaded = setLoadedWorkflow(markdown, file.name, 'file', null);
      if (!loaded?.ok) {
        return;
      }
      setStatus(loaded.migrated
        ? `Loaded ${file.name} and normalized its legacy schemaVersion payload. Save the test to persist the migrated schema field.`
        : `Loaded ${file.name}. Replay validates it before driving the app.`);
    } catch {
      setStatus('Could not read the selected workflow file.');
    }
  }

  async function chooseWorkflowFile() {
    if (hostBridge.has('workflowFilePicker')) {
      const result = await hostBridge.request('workflowFilePicker', {});
      if (result && result.ok && typeof result.markdown === 'string') {
        const loaded = setLoadedWorkflow(result.markdown, result.name || 'workflow.md', 'file', result.steps);
        if (!loaded?.ok) return;
        setStatus(loaded.migrated
          ? `Loaded ${result.name || 'workflow.md'} and normalized its legacy schemaVersion payload. Save the test to persist the migrated schema field.`
          : `Loaded ${result.name || 'workflow.md'}.`);
      } else if (result && result.error) {
        setStatus(result.error);
      }
      return;
    }
    workflowFileInput?.click();
  }

  if (cancelRecordingBtn) cancelRecordingBtn.addEventListener('click', cancelRecording);
  if (workflowFileInput) workflowFileInput.addEventListener('change', async () => {
    const file = workflowFileInput.files && workflowFileInput.files[0];
    workflowFileInput.value = '';
    await loadWorkflowFile(file);
  });

  // ── Replay and run support ──
  function updateFlowButtons() {
    // canDrive: this session holds the writer lease AND a live app is connected. The drive-actions
    // (record / replay / assert / return-to-start-route) 409 or fail otherwise, so disable them
    // rather than let a click error out. Record stays clickable WHILE recording so you can stop.
    const canDrive = isWriter && connected && !capturedTraceMode;
    const workbenchCanDrive = canDrive && !replaying;
    const workbenchAuthoring = recordingId
      ? 'recording'
      : authoringDraft.saving
        ? 'validating'
        : authoringDraft.stale
          ? 'stale'
          : authoringDraft.flowDirty || authoringDraft.planDirty
            ? 'draft'
            : lastMarkdown
              ? 'saved'
              : 'none';
    const driveAuthorityChanged = lastWorkbenchCanDrive !== workbenchCanDrive;
    lastWorkbenchCanDrive = workbenchCanDrive;
    if (testWorkbench) {
      const authoringChanged = testWorkbench.state().authoring !== workbenchAuthoring;
      if (authoringChanged || driveAuthorityChanged)
        testWorkbench.updateState({ authoring: workbenchAuthoring });
    }
    updateRecordingUi();
    if (cancelRecordingBtn) cancelRecordingBtn.disabled = !recordingId || recordingStopping || replaying || !canDrive;
    propertyGrid.updateWriterState();
    document.querySelectorAll('#df-dock .df-alert-choice').forEach((button) => {
      button.disabled = !canDrive;
      button.title = canDrive
        ? `Choose ${button.textContent || 'this action'}`
        : (capturedTraceMode ? 'Captured trace mode cannot dismiss native alerts.' : 'Take control before choosing a native alert action.');
    });
    if (tb?.interact) tb.interact.disabled = capturedTraceMode;
  }

  function setReplayUi(on) {
    replaying = on;
    document.body.classList.toggle('df-replaying', on);
    updateFlowButtons();
    // Pause the 3s poll while replaying so the screenshot doesn't churn under the driven app.
    if (on && pollInterval) { clearInterval(pollInterval); pollInterval = null; }
    else if (!on && !pollInterval) { pollInterval = setInterval(() => { if (!refreshTimer && !wsLive) refreshState(); }, 3000); }
  }

  async function captureCheckpoint() {
    try {
      const r = await fetch(`${basePath}/api/checkpoint`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}',
      });
      const j = r.ok ? await r.json().catch(() => null) : null;
      if (j && j.ok && j.route) updateFlowButtons();
    } catch (err) { /* best-effort */ }
  }

  async function legacyQuickReplay() {
    if (!lastMarkdown || recordingId || replaying) return;
    const workflowLabel = lastMarkdownName ? ` “${lastMarkdownName}”` : '';
    if (!(await confirmModal(`Legacy quick replay${workflowLabel} will drive the LIVE app and may change its data. It skips the Test Workbench run check. Continue?`, 'Legacy quick replay'))) return;
    // Capture a broker checkpoint before compatibility replay for external recovery tooling.
    await captureCheckpoint();
    setReplayUi(true);
    setStatus('Replaying…');
    const restoreWriter = isWriter;
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
    finally {
      if (restoreWriter && !capturedTraceMode) await control('claim');
      setReplayUi(false);
      scheduleRefresh(400);
    }
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
    if (rep.evidenceAvailable && timelineStepsEl) {
      const download = document.createElement('button');
      download.type = 'button';
      download.className = 'df-tool-btn';
      download.textContent = 'Download failure evidence';
      download.setAttribute('aria-label', 'Download redacted workflow replay failure evidence');
      download.addEventListener('click', async () => {
        try {
          const response = await fetch(`${basePath}/api/flows/replay/evidence`, {
            headers: { 'X-DevFlow-Inspector-Token': inspectorToken },
          });
          if (!response.ok) throw new Error();
          const link = document.createElement('a');
          link.href = URL.createObjectURL(await response.blob());
          link.download = 'devflow-replay-failure.mauitrace';
          link.click();
          URL.revokeObjectURL(link.href);
        } catch { setStatus('Failure evidence is no longer available.'); }
      });
      timelineStepsEl.append(download);
    }
    setStatus(rep.ok ? `Replay passed ${rep.passed}/${rep.total}.` : `Replay: ${rep.failed} step(s) did not pass.`);
  }

  updateFlowButtons();
  // ── Host bridge ──────────────────────────────────────────────────────────────────────────────
  // Every host conversation goes through the one bridge in inspector-host-bridge.js. This page has
  // no second listener and no capability vocabulary of its own: it asks the bridge how an operation
  // resolves in this surface, then either dispatches it or runs the Inspector's own equivalent.
  const copilotBtn = document.getElementById('df-send-copilot');
  const copilotMenu = document.getElementById('df-copilot-menu');
  const copilotMenuItems = copilotMenu
    ? [...copilotMenu.querySelectorAll('[data-copilot-context]')]
    : [];
  const sourceBtn = document.getElementById('df-open-source');
  const attachDataBtn = document.getElementById('df-attach-data');
  let dockSnapshot = null;
  let dockActiveTab = null;

  function leaseKindForHost(hostId) {
    const value = String(hostId || '').toLowerCase();
    if (value === 'canvas') return 'canvas';
    if (value === 'vscode') return 'vscode';
    if (value === 'browser') return 'browser';
    return 'embedded-host';
  }

  // Compact, durable element context shared with the host (Copilot). Everything the agent needs to
  // resolve "the selected element" without a screenshot.
  function elementInfo(el) {
    if (!el) return null;
    return {
      id: el.getAttribute('data-id') || null,
      type: el.getAttribute('data-type') || 'Element',
      automationId: el.getAttribute('data-automationId') || null,
      stableItemKey: el.getAttribute('data-stableItemKey') || null,
      collectionScope: el.getAttribute('data-collectionScope') || null,
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
    _selHostTimer = setTimeout(() => {
      _selHostTimer = null;
      hostBridge.notify('selection', { element: _selHostPending });
    }, 120);
  }

  hostBridge.onHostMessage((event) => {
    if (event.type === 'host') {
      const manifest = event.manifest;
      hostIdentity = manifest.hostId;
      leaseHolderKind = leaseKindForHost(manifest.hostId);
      leaseHolderLabel = manifest.hostLabel;
      document.body.dataset.hostKind = hostIdentity;
      document.documentElement.dataset.hostKind = hostIdentity;
      applyHostProfile(manifest.profile);
      adoptHostInteractionSession(manifest.interactionSessionId);
      updateHostButtons();
      if (event.theme) applyTheme(event.theme);   // host may bundle its theme with the capability ack
      return;
    }
    if (event.type === 'theme') {
      if (event.profile) applyHostProfile(event.profile);
      applyTheme(event.theme);                     // host reports/updates its color scheme + palette
      return;
    }
    if (event.type === 'recording') {
      void syncHostRecordingStatus();
    }
  });


  function selectedElement() { return selectedId ? elById(selectedId) : null; }

  function setExplainedDisabled(button, disabled) {
    if (!button) return;
    button.disabled = false;
    button.setAttribute('aria-disabled', String(!!disabled));
  }

  function updateHostButtons() {
    const el = selectedElement();
    // Source: enabled only when the selected element has a XAML source map.
    const sourceAvailable = !!(el && el.getAttribute('data-hasSource') === 'true');
    setExplainedDisabled(sourceBtn, !sourceAvailable);
    if (sourceBtn) sourceBtn.hidden = !sourceAvailable;
    const dataAvailable = !!dockSnapshot && supportsDataContextScope(dockSnapshot.scope);
    const copilotAvailable = !!(el || lastMarkdown || dataAvailable);
    setExplainedDisabled(copilotBtn, !copilotAvailable);
    if (copilotBtn) copilotBtn.hidden = !copilotAvailable;
    for (const item of copilotMenuItems) {
      const kind = item.getAttribute('data-copilot-context');
      item.disabled =
        (kind === 'selection' && !el) ||
        (kind === 'workflow' && !lastMarkdown) ||
        (kind === 'combined' && !(el && lastMarkdown)) ||
        (kind === 'data' && !dataAvailable);
    }
    updateDataAttachButton();
    scheduleToolbarLayout();
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
      if (!j || !j.ok || !j.file) { setStatus('No source available for this element.'); return; }
      if (hostBridge.notify('openSource', {
        file: j.file,
        line: j.line || 1,
        column: j.column || 1,
        sourceHash: j.sourceHash || null,
      })) {
        setStatus(`Opening ${shortFile(j.file)}:${j.line || 1}…`);
      } else {
        const loc = `${j.file}:${j.line || 1}`;
        await copyText(loc);
        setStatus(`Source: ${loc} (copied)`);
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
      if (hostBridge.has('copilotContext')) {
        setStatus(`Adding ${kind === 'combined' ? 'selection and workflow' : kind} context to Copilot…`);
        const result = await hostBridge.request('copilotContext', { context: kind, payload });
        setStatus(result && result.ok
          ? (result.message || 'Added Inspector context to Copilot.')
          : ((result && result.error) || 'The host could not add Inspector context to Copilot.'));
        return;
      }
      if (kind === 'selection' && hostBridge.notify('copilot', { payload })) {
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

  // ── Evidence: preview-then-download a redacted .mauitrace bundle ──
  // The toolbar action is an ACTION, not another host: it reuses the same broker routes the CLI
  // and MCP tools use, so a bundle downloaded here is byte-identical in policy to `maui devflow
  // evidence capture`.
  const evidenceBtn = document.getElementById('df-evidence');
  const evidence = createEvidenceController({
    basePath,
    inspectorToken,
    api: inspectorApi,
    setStatus,
    getSelectedId: () => selectedId,
    getWorkflow: () => lastMarkdown,
  });
  if (evidenceBtn) evidenceBtn.addEventListener('click', () => {
    setMoreOpen(false);
    evidence.open();
  });

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
  hostBridge.start();
  updateHostButtons();

  // ── Data dock: Logs / Network / Preferences / Device / Sensors / Files ──
  // Lazy-loaded read-only tabs over the token-gated broker proxies. All app-controlled data is
  // rendered with textContent / DOM nodes (never innerHTML) so a malicious log line, URL, header,
  // filename, or preference value can't inject markup. Inherited by every host (browser/VS Code/canvas).
  const dockEl = document.getElementById('df-dock');
  const dockTabsEl = document.getElementById('df-dock-tabs');
  const dockPanelEl = document.getElementById('df-dock-panel');
  const dockActionStripEl = document.getElementById('df-dock-action-strip');
  const dockBodyEl = document.getElementById('df-dock-body');
  const dockMetaEl = document.getElementById('df-dock-meta');
  const dockMetaNoteEl = document.getElementById('df-dock-meta-note');
  const dockCollapseBtn = document.getElementById('df-dock-collapse');
  const dockRefreshBtn = document.getElementById('df-dock-refresh');
  const dockCloseBtn = document.getElementById('df-dock-close');
  const toggleDockBtn = document.getElementById('df-toggle-dock');
  dockActiveTab = 'problems';
  let dockActiveView = 'default';
  let dockLoaded = false;
  let filesRoot = null, filesPath = '';
  let filesRoots = [];
  let filesLoadGeneration = 0;
  let cdpWebviewId = null;
  let cdpExpressionDraft = '';
  let cdpOutputValue = null;
  let cdpOutputError = null;
  let dockViewGeneration = 0;
  let networkDetailId = null;
  let networkListCache = null;
  let networkListSignature = '';
  let networkPollInFlight = false;
  let networkRequestEpoch = 0;
  const NETWORK_AUTO_REFRESH_MS = 2000;
  // Performance polling is deliberately slow and only runs while a session is recording, so the
  // tab never becomes a background load on the app it is measuring.
  const PERFORMANCE_POLL_MS = 3000;
  const LAYOUT_LIVE_POLL_MS = 2000;
  const DEVICE_RECORDING_POLL_MS = 2000;
  let performanceRecording = false;
  let performanceOwned = false;
  let performanceBusy = false;
  let sensorsView = 'list';
  let sensorsList = [];
  let sensorsListScrollTop = 0;
  let sensorsLocation = null;
  let sensorsLocationError = null;
  let sensorsLocationBusy = false;
  let currentAlert = null;
  let currentAlertRevision = null;
  let deviceAppSnapshot = null;
  let deviceHostState = null;
  let managedDeviceId = null;
  let managedDeviceRecording = null;
  let managedRecordingTimeoutSeconds = 180;
  let managedDeviceScreenshotUrl = null;
  let managedDeviceBusyAction = null;
  let managedDeviceView = 'overview';
  const managedDeviceStorageKey =
    `maui-devflow-managed-device:${inspectorAgent.id || 'default'}:${inspectorAgent.instanceId || 'unknown'}`;

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
  const dataUi = createDataUi(document);
  function dockEmpty(msg, options = {}) {
    dockBodyEl.replaceChildren(dataUi.state({
      kind: options.kind || 'empty',
      icon: options.icon || 'i-data',
      title: options.title || 'No data',
      message: msg,
      action: options.action || null,
    }));
  }
  function setDockMeta(text) { if (dockMetaEl) dockMetaEl.textContent = text || ''; }
  function setDockMetaNote(text) { if (dockMetaNoteEl) dockMetaNoteEl.textContent = text || ''; }
  const dataDockController = createDataDockController({
    panel: dockPanelEl,
    actionStrip: dockActionStripEl,
    body: dockBodyEl,
    renderActionStrip: (host, model) => dataUi.renderActionStrip(host, model),
    renderState: (options) => dataUi.state(options),
    setMeta: setDockMeta,
    setMetaNote: setDockMetaNote,
  });
  function setDockView(view) {
    dockActiveView = view || 'default';
    if (dockPanelEl) dockPanelEl.dataset.view = dockActiveView;
  }
  function activateDockSubview(view, label) {
    const generation = ++dockViewGeneration;
    setDockView(view);
    dataDockController.activate(dockActiveTab, {
      view: dockActiveView,
      generation,
      label: label || dockActiveTab.charAt(0).toUpperCase() + dockActiveTab.slice(1),
    });
    return generation;
  }
  function setDockActionStrip(model, generation = dockViewGeneration, view = dockActiveView) {
    dataDockController.setActionStrip(dockActiveTab, generation, model, view);
  }
  function hideDockActionStrip(generation = dockViewGeneration, view = dockActiveView) {
    setDockActionStrip(null, generation, view);
  }
  function dataStatusPill(label, tone = 'neutral') {
    return elh('span', { class: `df-status-pill df-status-pill-${tone}`, text: label });
  }
  function dataActionText(text, title = text) {
    return elh('span', { class: 'df-data-action-text', text, title });
  }
  function dataActionSelect(id, label, value, choices, onChange) {
    const select = elh('select', { class: 'df-field', id, 'aria-label': label, title: label });
    for (const [optionValue, optionLabel] of choices) {
      const option = elh('option', { value: optionValue, text: optionLabel });
      option.selected = optionValue === value;
      select.append(option);
    }
    select.addEventListener('change', () => onChange(select.value));
    return select;
  }
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
    if (hostBridge.has('attachData')) {
      setStatus(`Adding ${dockSnapshot.title} to Copilot…`);
      const result = await hostBridge.request('attachData', { snapshot: dockSnapshot });
      setStatus(result.ok
        ? (result.message || `Added ${dockSnapshot.title} to Copilot.`)
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
        const reveal = elh('button', {
          class: 'df-data-reveal',
          type: 'button',
          text: '••••• (reveal)',
          'aria-label': `Reveal ${keyName || 'masked'} value`,
        });
        reveal.addEventListener('click', () => {
          reveal.className = 'df-data-revealed';
          reveal.textContent = s;
          reveal.disabled = true;
        });
        return reveal;
      }
      return elh('span', { text: s });
    }
    if (Array.isArray(value)) {
      if (!value.length) return elh('span', { text: '[]' });
      const list = elh('ol', { class: 'df-data-array' });
      value.forEach((item) => list.append(elh('li', null, jsonView(item))));
      return list;
    }
    const keys = Object.keys(value);
    if (!keys.length) return elh('span', { text: '{}' });
    const list = elh('dl', { class: 'df-data-kv' });
    for (const k of keys) {
      list.append(
        elh('div', { class: 'df-data-kv-row' },
          elh('dt', { class: 'df-kv-key', text: k }),
          elh('dd', null, jsonView(value[k], k))));
    }
    return list;
  }

  function renderProblems(j) {
    const problems = j && j.problems;
    const problemsTab = document.getElementById('df-tab-problems');
    problemsTab?.classList.remove('df-has-update');
    if (problemsTab) {
      const count = Number.isFinite(j && j.count) ? j.count : null;
      problemsTab.textContent = `Problems${count !== null ? ` (${count})` : ''}`;
      problemsTab.setAttribute(
        'aria-label',
        count !== null ? `Problems, ${count} total` : 'Problems');
    }

    if (j && j.enabled === false) {
      clearDockSnapshot();
      dockEmpty('Binding and runtime diagnostics are disabled for this agent.', {
        kind: 'unavailable',
        title: 'Runtime Problems unavailable',
      });
      return;
    }
    if (!Array.isArray(problems) || !problems.length) {
      clearDockSnapshot();
      dockEmpty(j && j.error
        ? j.error
        : 'Binding and runtime diagnostics appear here automatically as the app runs.', {
        kind: j && j.error ? 'error' : 'empty',
        icon: j && j.error ? 'i-refresh' : 'i-check',
        title: j && j.error ? 'Could not read runtime Problems' : 'No runtime UI problems captured',
      });
      return;
    }

    const fragment = document.createDocumentFragment();
    for (const problem of problems) {
      const row = elh('button', { class: 'df-problem-row', type: 'button' });
      const heading = elh('div', { class: 'df-problem-heading' });
      heading.append(
        elh('span', { class: 'df-problem-code', text: problem.code || problem.kind || 'problem' }),
        elh('span', { class: 'df-problem-count', text: problem.count > 1 ? `×${problem.count}` : '' }));
      row.append(heading, elh('div', { class: 'df-problem-message', text: problem.message || 'Runtime UI problem' }));

      const context = [
        problem.elementType,
        problem.property,
        problem.bindingPath ? `Binding ${problem.bindingPath}` : null,
        problem.sourceFile ? `${shortFile(problem.sourceFile)}${problem.sourceLine ? `:${problem.sourceLine}` : ''}` : null,
      ].filter(Boolean).join(' · ');
      if (context) row.append(elh('div', { class: 'df-problem-context', text: context }));

      if (problem.elementId) {
        row.title = 'Select the affected element';
        row.addEventListener('click', () => {
          const target = elById(problem.elementId);
          if (!target) {
            setStatus('The affected element is no longer present in the current frame.');
            return;
          }
          selectElement(problem.elementId);
          propertyGrid.open(target);
        });
      } else {
        row.disabled = true;
      }
      fragment.append(row);
    }
    dockBodyEl.replaceChildren(fragment);
    recordDockSnapshot(
      'problems',
      `Problems · ${problems.length} shown`,
      problems,
      j.count || problems.length,
      { revision: j.revision || 0, evicted: j.evicted || 0 });
  }

  function renderLogs(j) {
    const logs = j && j.logs;
    const historyNote = 'May include prior launches.';
    if (!Array.isArray(logs) || !logs.length) {
      clearDockSnapshot();
      setDockMetaNote('');
      setDockMeta('');
      dockEmpty(j && j.error ? j.error : 'Log entries appear here as the app writes them.', {
        kind: j && j.error ? 'error' : 'empty',
        icon: j && j.error ? 'i-refresh' : 'i-check',
        title: j && j.error ? 'Could not read logs' : 'No logs captured',
      });
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
    setDockMetaNote(historyNote);
    // The logs API returns entries newest first.
    const captured = logs.slice(0, 100);
    recordDockSnapshot(
      'logs',
      `Logs · ${captured.length === logs.length ? logs.length : `newest ${captured.length} of ${logs.length}`} · ${historyNote}`,
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
      dockEmpty(j && j.error ? j.error : 'Network capture is unavailable for this app.', {
        kind: j && j.error ? 'error' : 'unavailable',
        title: j && j.error ? 'Could not refresh network data' : 'Network data unavailable',
      });
      return true;
    }
    const signature = networkSignature(reqs);
    if (!opts.force && signature === networkListSignature) return false;
    const scrollTop = opts.preserveScroll ? dockBodyEl.scrollTop : 0;
    networkListSignature = signature;
    if (!reqs.length) {
      clearDockSnapshot();
      dockEmpty('Requests appear here when the app uses the captured HTTP pipeline.', {
        icon: 'i-check',
        title: 'No requests captured',
      });
      return true;
    }
    const tbl = elh('table');
    tbl.append(elh('tr', null, elh('th', { text: 'Method' }), elh('th', { text: 'URL' }), elh('th', { text: 'Status' }), elh('th', { text: 'ms' })));
    for (const r of reqs) {
      const open = elh('button', {
        class: 'df-data-link',
        type: 'button',
        text: r.url || r.path || '',
        'data-network-request-id': r.id || '',
        'aria-label': `Open ${r.method || 'network'} request ${r.url || r.path || ''}`,
      });
      if (r.id) open.addEventListener('click', () => {
        networkListCache = {
          requests: reqs,
          scrollTop: dockBodyEl.scrollTop,
          focusId: r.id,
        };
        executeDataCommand('network.detail.open', { id: r.id });
      });
      const tr = elh('tr', null,
        elh('td', { text: r.method || '' }),
        elh('td', null, open),
        elh('td', { text: r.statusCode != null ? String(r.statusCode) : (r.error ? 'ERR' : '') }),
        elh('td', { text: r.durationMs != null ? String(r.durationMs) : '' }));
      tbl.append(tr);
    }
    dockBodyEl.replaceChildren(tbl);
    if (opts.preserveScroll) dockBodyEl.scrollTop = scrollTop;
    networkListCache = {
      requests: reqs,
      scrollTop: opts.preserveScroll ? scrollTop : dockBodyEl.scrollTop,
      focusId: networkListCache?.focusId || null,
    };
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

  async function loadNetworkDetail(id, options = {}) {
    if (!id) return;
    const retained = options.refresh ? {
      nodes: [...dockBodyEl.childNodes],
      scrollTop: dockBodyEl.scrollTop,
      stripModel: dataDockController.current().stripModel,
      snapshot: dockSnapshot,
      meta: dockMetaEl?.textContent || '',
      note: dockMetaNoteEl?.textContent || '',
    } : null;
    if (!options.refresh && (!networkListCache || !Array.isArray(networkListCache.requests))) {
      networkListCache = {
        requests: [],
        scrollTop: dockBodyEl.scrollTop,
        focusId: id,
      };
    }
    networkDetailId = id;
    const generation = activateDockSubview('detail', 'Network request');
    if (!options.refresh) clearDockSnapshot();
    if (options.refresh) {
      dockBodyEl.setAttribute('aria-busy', 'true');
    } else {
      setDockMeta('loading…');
      dockBodyEl.replaceChildren(dataUi.state({
        kind: 'loading',
        icon: 'i-refresh',
        title: 'Loading request',
        message: 'Reading the selected network request.',
      }));
    }
    const j = await apiPost('/api/network/detail', { id });
    if (!dataDockController.isCurrent('network', generation, 'detail') || networkDetailId !== id) return;
    setDockMeta('live paused · captured ' + new Date().toLocaleTimeString());
    const r = j && j.request;
    const back = dataUi.button('Requests', {
      commandId: 'network.detail.back',
      icon: 'i-chevron',
      className: 'df-data-back df-network-back',
      onClick: () => executeDataCommand('network.detail.back'),
    });
    if (!r) {
      if (retained) {
        dockBodyEl.replaceChildren(...retained.nodes);
        dockBodyEl.scrollTop = retained.scrollTop;
        dockBodyEl.setAttribute('aria-busy', 'false');
        dockSnapshot = retained.snapshot;
        updateDataAttachButton();
        updateHostButtons();
        setDockActionStrip(retained.stripModel, generation, 'detail');
        setDockMeta('stale · refresh failed');
        setDockMetaNote(retained.note);
        return;
      }
      setDockActionStrip({
        visible: true,
        variant: 'navigation',
        ariaLabel: 'Network request navigation',
        context: [back, layoutStatusPill('Unavailable', 'error')],
      }, generation, 'detail');
      dockEmpty((j && j.error) || 'Request detail unavailable.', {
        kind: 'error',
        title: 'Could not load request',
      });
      return;
    }
    dockBodyEl.setAttribute('aria-busy', 'false');
    const shortUrl = String(r.url || '').length > 90 ? `${String(r.url).slice(0, 87)}…` : String(r.url || '');
    setDockActionStrip({
      visible: true,
      variant: 'navigation',
      ariaLabel: 'Network request navigation',
      context: [
        back,
        layoutStatusPill(r.statusCode != null ? String(r.statusCode) : r.error ? 'ERR' : 'Pending',
          r.error || Number(r.statusCode) >= 400 ? 'error' : 'neutral'),
        dataActionText(`${r.method || ''} ${shortUrl}`.trim(), `${r.method || ''} ${r.url || ''}`.trim()),
      ],
    }, generation, 'detail');
    dockBodyEl.replaceChildren(jsonView({
      status: r.statusCode, statusText: r.statusText, durationMs: r.durationMs,
      requestContentType: r.requestContentType, responseContentType: r.responseContentType,
      requestHeaders: r.requestHeaders, responseHeaders: r.responseHeaders,
      requestBody: r.requestBody, responseBody: r.responseBody, error: r.error,
    }));
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

  function restoreNetworkList() {
    const cache = networkListCache;
    networkDetailId = null;
    activateDockSubview('list', 'Network');
    hideDockActionStrip();
    if (!cache || !Array.isArray(cache.requests)) {
      loadTab('network');
      return;
    }
    renderNetwork({ requests: cache.requests }, { force: true });
    dockBodyEl.scrollTop = cache.scrollTop || 0;
    requestAnimationFrame(() => {
      dockBodyEl.querySelector(`[data-network-request-id="${CSS.escape(cache.focusId || '')}"]`)?.focus();
    });
  }

  function renderPreferences(j) {
    if (!j || j.ok === false) {
      clearDockSnapshot();
      dockEmpty((j && j.error) || 'Preferences are not available from this app.', {
        kind: j && j.error ? 'error' : 'unavailable',
        title: j && j.error ? 'Could not refresh Preferences' : 'Preferences unavailable',
      });
      return;
    }
    const frag = document.createDocumentFragment();
    frag.append(jsonView(j.preferences));
    dockBodyEl.replaceChildren(frag);
    const count = j.preferences && typeof j.preferences === 'object' ? Object.keys(j.preferences).length : 0;
    recordDockSnapshot('preferences', `Preferences · ${count} entr${count === 1 ? 'y' : 'ies'}`, j.preferences, count);
  }

  function managedDevices() {
    return Array.isArray(deviceHostState?.devices) ? deviceHostState.devices : [];
  }

  function managedDevice() {
    return managedDevices().find((device) => device.id === managedDeviceId) || null;
  }

  function chooseManagedDevice() {
    const devices = managedDevices();
    const previous = managedDeviceId;
    if (!devices.length) {
      managedDeviceId = null;
      if (previous) clearManagedDeviceScreenshot();
      return null;
    }
    const persisted = (() => {
      try { return sessionStorage.getItem(managedDeviceStorageKey); } catch { return null; }
    })();
    const candidates = [
      managedDeviceId,
      deviceContext?.deviceId,
      persisted,
      devices.find((device) => device.agentId === inspectorAgent.id)?.id,
      devices.find((device) => device.isBooted)?.id,
      devices[0]?.id,
    ].filter(Boolean);
    managedDeviceId = candidates.find((id) => devices.some((device) => device.id === id)) || null;
    if (previous && managedDeviceId !== previous) clearManagedDeviceScreenshot();
    try {
      if (managedDeviceId) sessionStorage.setItem(managedDeviceStorageKey, managedDeviceId);
      else sessionStorage.removeItem(managedDeviceStorageKey);
    } catch { }
    return managedDevice();
  }

  function canManageDevice() {
    return deviceHostState?.available === true &&
      !capturedTraceMode &&
      !replaying &&
      !leaseHeldByOther &&
      !managedDeviceBusyAction;
  }

  function deviceStateTone(device) {
    if (!device) return 'neutral';
    if (device.isBooted || device.state === 'booted') return 'success';
    if (device.state === 'booting' || device.state === 'shutting-down') return 'warning';
    return device.isAvailable === false ? 'error' : 'neutral';
  }

  function deviceStateLabel(device) {
    if (!device) return 'No device';
    const state = String(device.state || 'unknown').replace(/-/g, ' ');
    return state.charAt(0).toUpperCase() + state.slice(1);
  }

  function deviceField(label, value) {
    return elh('div', { class: 'df-device-field' },
      elh('span', { class: 'df-device-field-label', text: label }),
      elh('span', { class: 'df-device-field-value', text: value == null || value === '' ? '—' : String(value) }));
  }

  function deviceInput(label, attributes = {}) {
    return elh('label', { class: 'df-device-input' },
      elh('span', { text: label }),
      elh('input', { class: 'df-field', ...attributes }));
  }

  function deviceSelect(label, choices, value) {
    const select = elh('select', { class: 'df-field', 'aria-label': label, title: label });
    for (const [optionValue, optionLabel] of choices) {
      const option = elh('option', { value: optionValue, text: optionLabel });
      option.selected = optionValue === value;
      select.append(option);
    }
    return select;
  }

  function selectedDeviceSummary(device) {
    if (!device) return null;
    return {
      name: device.name || null,
      platform: device.platform || null,
      state: device.state || null,
      osVersion: device.osVersion || null,
      model: device.deviceTypeName || device.modelIdentifier || null,
      orientation: device.display?.orientation || null,
      pairedApp: device.agentId ? inspectorAgent.appName : null,
    };
  }

  function clearManagedDeviceScreenshot() {
    if (managedDeviceScreenshotUrl) URL.revokeObjectURL(managedDeviceScreenshotUrl);
    managedDeviceScreenshotUrl = null;
  }

  async function loadManagedRecordingStatus(device) {
    managedDeviceRecording = null;
    if (!device?.capabilities?.recording) return;
    const result = await inspectorApi.getDetailed(
      `/api/device/recording?deviceId=${encodeURIComponent(device.id)}`);
    if (result.ok && result.body?.ok)
      managedDeviceRecording = result.body.recording || null;
  }

  async function loadManagedDeviceHost() {
    if (!inspectorSurfaces.deviceHost) {
      // No broker device layer in this build: report it instead of fetching a route that 404s.
      // renderDevice() still shows the app-reported device facts from /api/device.
      deviceHostState = {
        available: false,
        reason: 'This broker does not host the device layer, so emulators and simulators cannot be controlled from here.',
        devices: [],
      };
      managedDeviceRecording = null;
      return deviceHostState;
    }
    const result = await inspectorApi.getDetailed('/api/device/host');
    deviceHostState = result.ok && result.body
      ? result.body
      : { available: false, reason: result.body?.error || 'The broker device layer is unavailable.', devices: [] };
    const selected = chooseManagedDevice();
    await loadManagedRecordingStatus(selected);
    return deviceHostState;
  }

  async function runManagedDeviceAction(action, values = {}, options = {}) {
    const device = managedDevice();
    if (!canManageDevice()) {
      setStatus(capturedTraceMode
        ? 'Captured trace mode is read-only.'
        : leaseHeldByOther
          ? 'Take control before changing the device.'
          : 'Another device operation is still running.');
      return null;
    }
    if (action !== 'create' && !device) {
      setStatus('Select a device first.');
      return null;
    }

    managedDeviceBusyAction = action;
    if (dockActiveTab === 'device') renderDevice(deviceAppSnapshot, deviceHostState);
    setStatus(options.pending || `${action.replace(/-/g, ' ')} in progress…`);
    try {
      const result = await inspectorApi.postDetailed('/api/device/control', {
        action,
        deviceId: action === 'create' ? undefined : device.id,
        ...values,
      });
      const response = result.body || {};
      if (!result.ok || response.success !== true) {
        setStatus(response.reason || response.error || options.failure || 'The device operation failed.');
        return null;
      }
      if (response.device?.id) {
        if (response.device.id !== managedDeviceId) clearManagedDeviceScreenshot();
        managedDeviceId = response.device.id;
      }
      if (response.recording) managedDeviceRecording = response.recording;
      if (response.artifactName)
        setStatus(`Device recording saved as ${response.artifactName}.`);
      else
        setStatus(options.success || `${action.replace(/-/g, ' ')} completed.`);
      await loadManagedDeviceHost();
      await refreshState();
      return response;
    } finally {
      managedDeviceBusyAction = null;
      if (dockActiveTab === 'device') renderDevice(deviceAppSnapshot, deviceHostState);
    }
  }

  async function captureManagedDeviceScreenshot() {
    const device = managedDevice();
    if (!device) return;
    managedDeviceBusyAction = 'screenshot';
    renderDevice(deviceAppSnapshot, deviceHostState);
    setStatus('Capturing the device screen…');
    try {
      const response = await fetch(
        `${basePath}/api/device/screenshot?deviceId=${encodeURIComponent(device.id)}`,
        { headers: inspectorRequestHeaders(), cache: 'no-store' });
      if (!response.ok) {
        const error = await response.json().catch(() => null);
        setStatus(error?.error || 'The device screenshot is unavailable.');
        return;
      }
      const blob = await response.blob();
      if (managedDeviceScreenshotUrl) URL.revokeObjectURL(managedDeviceScreenshotUrl);
      managedDeviceScreenshotUrl = URL.createObjectURL(blob);
      setStatus('Device screenshot captured.');
    } catch {
      setStatus('The device screenshot could not be captured.');
    } finally {
      managedDeviceBusyAction = null;
      if (dockActiveTab === 'device') renderDevice(deviceAppSnapshot, deviceHostState);
    }
  }

  function openManagedDeviceCreate() {
    managedDeviceView = 'create';
    renderManagedDeviceCreate();
  }

  function renderManagedDeviceCreate() {
    const generation = activateDockSubview('device-create', 'Create device');
    const runtimes = (deviceHostState?.runtimes || []).filter((runtime) => runtime.isAvailable !== false);
    const types = deviceHostState?.deviceTypes || [];
    if (!runtimes.length || !types.length) {
      setDockActionStrip({
        visible: true,
        context: [dataUi.button('Device', {
          icon: 'i-chevron',
          className: 'df-data-back',
          onClick: () => { managedDeviceView = 'overview'; renderDevice(deviceAppSnapshot, deviceHostState); },
        })],
      }, generation, 'device-create');
      dockEmpty('No compatible runtime and device-type combination is installed.', {
        kind: 'unavailable',
        title: 'Device creation unavailable',
      });
      return;
    }

    const platforms = [...new Set(runtimes.map((runtime) => runtime.platform).filter(Boolean))];
    const platform = deviceSelect('Platform', platforms.map((value) => [value, value === 'ios' ? 'iOS' : 'Android']), platforms[0]);
    const name = deviceInput('Name', { maxlength: 128, value: `DevFlow ${platforms[0] === 'ios' ? 'iPhone' : 'Android'}` });
    const runtime = deviceSelect('Runtime', [], '');
    const type = deviceSelect('Device type', [], '');

    function populate() {
      const availableRuntimes = runtimes.filter((candidate) => candidate.platform === platform.value);
      runtime.replaceChildren(...availableRuntimes.map((candidate) =>
        elh('option', { value: candidate.id, text: `${candidate.name} · ${candidate.version}` })));
      const selectedRuntime = availableRuntimes.find((candidate) => candidate.id === runtime.value) || availableRuntimes[0];
      const supported = new Set(selectedRuntime?.supportedDeviceTypeIds || []);
      const availableTypes = types.filter((candidate) =>
        candidate.platform === platform.value && (!supported.size || supported.has(candidate.id)));
      type.replaceChildren(...availableTypes.map((candidate) =>
        elh('option', { value: candidate.id, text: candidate.name })));
    }
    platform.addEventListener('change', () => {
      name.querySelector('input').value = `DevFlow ${platform.value === 'ios' ? 'iPhone' : 'Android'}`;
      populate();
    });
    runtime.addEventListener('change', populate);
    populate();

    const create = dataUi.button(managedDeviceBusyAction === 'create' ? 'Creating…' : 'Create and boot', {
      commandId: 'device.create-submit',
      primary: true,
      disabled: !canManageDevice(),
      onClick: async () => {
        const created = await runManagedDeviceAction('create', {
          platform: platform.value,
          name: name.querySelector('input').value,
          runtimeId: runtime.value,
          deviceTypeId: type.value,
        }, {
          pending: 'Creating and booting the virtual device…',
          success: 'Virtual device created and selected.',
        });
        if (created) {
          managedDeviceView = 'overview';
          renderDevice(deviceAppSnapshot, deviceHostState);
        }
      },
    });
    setDockActionStrip({
      visible: true,
      ariaLabel: 'Create device',
      context: [dataUi.button('Device', {
        icon: 'i-chevron',
        className: 'df-data-back',
        onClick: () => { managedDeviceView = 'overview'; renderDevice(deviceAppSnapshot, deviceHostState); },
      })],
      primaryCommand: create,
    }, generation, 'device-create');

    const form = elh('div', { class: 'df-device-create-form' },
      elh('p', {
        class: 'df-device-guidance',
        text: 'Create an explicit virtual device from installed host runtimes. Nothing is downloaded automatically.',
      }),
      name,
      elh('label', { class: 'df-device-input' }, elh('span', { text: 'Platform' }), platform),
      elh('label', { class: 'df-device-input' }, elh('span', { text: 'Runtime' }), runtime),
      elh('label', { class: 'df-device-input' }, elh('span', { text: 'Device type' }), type));
    dockBodyEl.replaceChildren(form);
  }

  function renderManagedDeviceControls(container, device) {
    const controls = elh('div', { class: 'df-device-controls' });
    const canMutate = canManageDevice();

    const text = deviceInput('Text input', { maxlength: 8192, placeholder: 'Type into the focused device control' });
    const textField = text.querySelector('input');
    text.append(dataUi.button('Type', {
      commandId: 'device.type-text',
      disabled: !canMutate || !device.capabilities?.text,
      onClick: async () => {
        if (await runManagedDeviceAction('type-text', { text: textField.value }, { success: 'Text sent to the device.' }))
          textField.value = '';
      },
    }));

    const buttonChoices = device.platform === 'ios'
      ? ['home', 'lock', 'side-button', 'siri', 'apple-pay']
      : ['home', 'back', 'apps', 'power', 'volume-up', 'volume-down', 'menu'];
    const button = deviceSelect('Device button', buttonChoices.map((value) => [value, value.replace(/-/g, ' ')]), buttonChoices[0]);
    const buttonRow = elh('label', { class: 'df-device-input' },
      elh('span', { text: 'System button' }),
      button,
      dataUi.button('Press', {
        commandId: 'device.press-button',
        disabled: !canMutate || !device.capabilities?.button,
        onClick: () => runManagedDeviceAction('press-button', { button: button.value }, { success: `Pressed ${button.value}.` }),
      }));

    const key = deviceInput('USB HID key', { type: 'number', min: 0, max: 65535, value: 40 });
    key.append(dataUi.button('Press', {
      commandId: 'device.press-key',
      disabled: !canMutate || !device.capabilities?.key,
      onClick: () => runManagedDeviceAction('press-key', {
        keyCode: Number(key.querySelector('input').value),
      }, { success: 'Key sent to the device.' }),
    }));

    const orientation = deviceSelect('Orientation', [
      ['portrait', 'Portrait'],
      ['portrait-upside-down', 'Portrait upside down'],
      ['landscape-left', 'Landscape left'],
      ['landscape-right', 'Landscape right'],
    ], String(device.display?.orientation || 'portrait').replace(/([A-Z])/g, '-$1').toLowerCase());
    const orientationRow = elh('label', { class: 'df-device-input' },
      elh('span', { text: 'Orientation' }),
      orientation,
      dataUi.button('Rotate', {
        commandId: 'device.rotate',
        disabled: !canMutate || !device.capabilities?.rotate,
        onClick: () => runManagedDeviceAction('rotate', { orientation: orientation.value }, { success: `Rotated to ${orientation.value}.` }),
      }));

    const recordingTimeout = deviceInput('Recording seconds', {
      type: 'number',
      min: 1,
      max: 3600,
      step: 1,
      value: managedRecordingTimeoutSeconds,
    });
    recordingTimeout.querySelector('input').addEventListener('change', (event) => {
      managedRecordingTimeoutSeconds = Math.max(1, Math.min(3600, Number(event.target.value) || 180));
      event.target.value = String(managedRecordingTimeoutSeconds);
    });
    recordingTimeout.append(elh('span', {
      class: 'df-device-recording-state',
      text: managedDeviceRecording?.isRecording
        ? 'Recording now'
        : managedDeviceRecording?.artifactName
          ? `Last: ${managedDeviceRecording.artifactName}`
          : 'Not recording',
    }));

    const coordinateDetails = elh('details', { class: 'df-device-coordinate-controls' });
    coordinateDetails.append(elh('summary', { text: 'Exact coordinate input' }));
    const x = deviceInput('X', { type: 'number', min: 0, step: 'any', value: 0 });
    const y = deviceInput('Y', { type: 'number', min: 0, step: 'any', value: 0 });
    const duration = deviceInput('Hold seconds', { type: 'number', min: 0.1, max: 60, step: 0.1, value: 1 });
    const pointActions = elh('div', { class: 'df-device-inline-actions' },
      dataUi.button('Tap', {
        commandId: 'device.tap',
        disabled: !canMutate || !device.capabilities?.tap,
        onClick: () => runManagedDeviceAction('tap', {
          x: Number(x.querySelector('input').value),
          y: Number(y.querySelector('input').value),
        }, { success: 'Tapped the device.' }),
      }),
      dataUi.button('Long press', {
        commandId: 'device.long-press',
        disabled: !canMutate || !device.capabilities?.longPress,
        onClick: () => runManagedDeviceAction('long-press', {
          x: Number(x.querySelector('input').value),
          y: Number(y.querySelector('input').value),
          duration: Number(duration.querySelector('input').value),
        }, { success: 'Long press sent to the device.' }),
      }));
    coordinateDetails.append(elh('div', { class: 'df-device-form-grid' }, x, y, duration), pointActions);

    const swipeDetails = elh('details', { class: 'df-device-coordinate-controls' });
    swipeDetails.append(elh('summary', { text: 'Exact swipe input' }));
    const swipeFields = [
      ['Start X', 'startX', 0], ['Start Y', 'startY', 0],
      ['End X', 'endX', Math.round(device.display?.pointWidth || 0)],
      ['End Y', 'endY', Math.round(device.display?.pointHeight || 0)],
      ['Seconds', 'duration', 0.35],
    ].map(([label, name, value]) => deviceInput(label, {
      type: 'number', min: 0, step: 'any', value, 'data-device-value': name,
    }));
    swipeDetails.append(
      elh('div', { class: 'df-device-form-grid' }, ...swipeFields),
      dataUi.button('Swipe', {
        commandId: 'device.swipe',
        disabled: !canMutate || !device.capabilities?.swipe,
        onClick: () => {
          const values = {};
          for (const field of swipeFields) {
            const input = field.querySelector('input');
            values[input.dataset.deviceValue] = Number(input.value);
          }
          return runManagedDeviceAction('swipe', values, { success: 'Swipe sent to the device.' });
        },
      }));

    controls.append(
      elh('p', {
        class: 'df-device-guidance',
        text: 'Semantic app actions remain preferred. Use these controls for system UI, hardware buttons, or content outside the MAUI visual tree.',
      }),
      text,
      buttonRow,
      key,
      orientationRow,
      recordingTimeout,
      coordinateDetails,
      swipeDetails);
    container.append(
      elh('div', { class: 'df-section-title', text: 'Direct device input' }),
      controls);
  }

  function renderDevice(appResult, hostResult = deviceHostState) {
    deviceAppSnapshot = appResult;
    deviceHostState = hostResult || deviceHostState;
    if (managedDeviceView === 'create') {
      renderManagedDeviceCreate();
      return;
    }

    const dev = appResult?.device;
    const hasAppData = dev && typeof dev === 'object' && Object.keys(dev).length > 0;
    const devices = managedDevices();
    const selected = chooseManagedDevice();
    if (!hasAppData && !deviceHostState?.available && !devices.length) {
      clearDockSnapshot();
      hideDockActionStrip();
      dockEmpty(
        deviceHostState?.reason || appResult?.error || 'No device host or app device information is available.',
        {
          kind: appResult?.error ? 'error' : 'unavailable',
          title: appResult?.error ? 'Could not refresh device information' : 'Device controls unavailable',
        });
      return;
    }

    const selector = deviceSelect(
      'Managed device',
      devices.map((device) => [
        device.id,
        `${device.name || device.id} · ${deviceStateLabel(device)}${device.agentId ? ' · app attached' : ''}`,
      ]),
      selected?.id || '');
    selector.disabled = !devices.length || !!managedDeviceBusyAction;
    selector.addEventListener('change', async () => {
      clearManagedDeviceScreenshot();
      managedDeviceId = selector.value;
      try { sessionStorage.setItem(managedDeviceStorageKey, managedDeviceId); } catch { }
      await loadManagedRecordingStatus(managedDevice());
      renderDevice(deviceAppSnapshot, deviceHostState);
    });

    const primary = selected && !selected.isBooted && selected.capabilities?.boot
      ? dataUi.button(managedDeviceBusyAction === 'boot' ? 'Booting…' : 'Boot', {
          commandId: 'device.boot',
          primary: true,
          disabled: !canManageDevice(),
          onClick: () => runManagedDeviceAction('boot', {}, { pending: 'Booting the device…', success: 'Device booted.' }),
        })
      : selected?.capabilities?.reveal
        ? dataUi.button(managedDeviceBusyAction === 'reveal' ? 'Opening…' : 'Show window', {
            commandId: 'device.reveal',
            primary: true,
            disabled: !canManageDevice(),
            onClick: () => runManagedDeviceAction('reveal', {}, { success: 'Device window opened.' }),
          })
        : null;
    const recording = managedDeviceRecording?.isRecording === true;
    const secondary = [
      dataUi.button('New device', {
        commandId: 'device.create',
        icon: 'i-window',
        disabled: !deviceHostState?.available || !!managedDeviceBusyAction,
        onClick: openManagedDeviceCreate,
      }),
    ];
    if (selected) {
      secondary.push(
        dataUi.button('Screenshot', {
          commandId: 'device.screenshot',
          icon: 'i-image',
          disabled: !selected.capabilities?.screenshot || !!managedDeviceBusyAction,
          onClick: captureManagedDeviceScreenshot,
        }),
        dataUi.button(recording ? 'Stop recording' : 'Record', {
          commandId: recording ? 'device.recording-stop' : 'device.recording-start',
          icon: recording ? 'i-close' : 'i-record',
          disabled: !canManageDevice() || !selected.capabilities?.recording,
          onClick: () => runManagedDeviceAction(recording ? 'recording-stop' : 'recording-start',
            recording ? {} : { timeoutSeconds: managedRecordingTimeoutSeconds },
            recording
              ? { pending: 'Stopping device recording…' }
              : { pending: 'Starting device recording…', success: 'Device recording started.' }),
        }),
        dataUi.button('Restart', {
          commandId: 'device.restart',
          disabled: !canManageDevice() || !selected.capabilities?.restart,
          onClick: () => runManagedDeviceAction('restart', {}, { pending: 'Restarting the device…', success: 'Device restarted.' }),
        }),
        dataUi.button('Power off', {
          commandId: 'device.shutdown',
          disabled: !canManageDevice() || !selected.capabilities?.shutdown,
          onClick: () => runManagedDeviceAction('shutdown', {}, { pending: 'Powering off the device…', success: 'Device powered off.' }),
        }));
    }
    setDockActionStrip({
      visible: true,
      ariaLabel: 'Device controls',
      context: selected
        ? [
            dataStatusPill(deviceStateLabel(selected), deviceStateTone(selected)),
            selected.agentId
              ? dataStatusPill('App attached', 'success')
              : dataStatusPill('No app attached', 'neutral'),
            recording ? dataStatusPill('Recording', 'warning') : null,
          ].filter(Boolean)
        : [dataStatusPill(deviceHostState?.available ? 'No devices' : 'Unavailable', deviceHostState?.available ? 'neutral' : 'error')],
      persistentControls: devices.length ? [selector] : [],
      secondaryCommands: secondary,
      primaryCommand: primary,
    });

    const frag = document.createDocumentFragment();
    if (selected) {
      const summary = elh('section', { class: 'df-device-summary' },
        elh('div', { class: 'df-device-summary-heading' },
          elh('strong', { text: selected.name || selected.id }),
          elh('span', { text: `${selected.platform || 'unknown'} ${selected.osVersion || ''}`.trim() })),
        elh('div', { class: 'df-device-field-grid' },
          deviceField('State', deviceStateLabel(selected)),
          deviceField('Provider', selected.provider),
          deviceField('Runtime', selected.runtimeName || selected.runtimeId),
          deviceField('Model', selected.deviceTypeName || selected.modelIdentifier),
          deviceField('Architecture', selected.architecture),
          deviceField('Display', selected.display
            ? `${Math.round(selected.display.pointWidth)} x ${Math.round(selected.display.pointHeight)} pt · ${selected.display.orientation}`
            : null)));
      frag.append(summary);

      if (managedDeviceScreenshotUrl) {
        const image = elh('img', {
          class: 'df-device-screenshot-preview',
          src: managedDeviceScreenshotUrl,
          alt: `Captured screen of ${selected.name || 'the selected device'}`,
        });
        const preview = elh('section', { class: 'df-device-media-preview' },
          elh('div', { class: 'df-section-title', text: 'Captured device screen' }),
          image);
        if (hostBridge.canDownload()) {
          preview.append(elh('a', {
            class: 'df-data-button',
            href: managedDeviceScreenshotUrl,
            download: `${(selected.name || 'device').replace(/[^A-Za-z0-9._-]+/g, '-')}.png`,
            text: 'Download PNG',
          }));
        }
        frag.append(preview);
      }

      renderManagedDeviceControls(frag, selected);

      const danger = elh('section', { class: 'df-device-danger' },
        elh('div', { class: 'df-section-title', text: 'Device data' }),
        elh('p', {
          class: 'df-device-guidance',
          text: 'These actions permanently change the selected virtual device and always require a second exact confirmation.',
        }),
        dataUi.button('Erase content', {
          commandId: 'device.erase',
          disabled: !canManageDevice() || !selected.capabilities?.erase,
          onClick: async () => {
            const confirmationText = `erase ${selected.id}`;
            if (await confirmModal(
              `Erase all content and settings from ${selected.name || selected.id}? This cannot be undone.`,
              'Erase device',
              { requiredText: confirmationText })) {
              await runManagedDeviceAction('erase', {
                confirm: true,
                confirmedDeviceId: selected.id,
                confirmationText,
              }, { pending: 'Erasing device content…', success: 'Device content erased.' });
            }
          },
        }),
        dataUi.button('Delete device', {
          commandId: 'device.delete',
          disabled: !canManageDevice() || !selected.capabilities?.delete,
          onClick: async () => {
            const confirmationText = `delete ${selected.id}`;
            if (await confirmModal(
              `Permanently delete ${selected.name || selected.id}? This cannot be undone.`,
              'Delete device',
              { requiredText: confirmationText })) {
              const deleted = await runManagedDeviceAction('delete', {
                confirm: true,
                confirmedDeviceId: selected.id,
                confirmationText,
              }, { pending: 'Deleting the virtual device…', success: 'Virtual device deleted.' });
              if (deleted) renderDevice(deviceAppSnapshot, deviceHostState);
            }
          },
        }));
      frag.append(danger);
    }

    if (hasAppData) {
      const labels = { 'device-info': 'App device', 'device-display': 'App display', battery: 'Battery', connectivity: 'Connectivity' };
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
      recordDockSnapshot('device', 'Device snapshot', normalizedDevice, Object.keys(normalizedDevice).length, {
        hostDevice: selectedDeviceSummary(selected),
      });
    } else {
      clearDockSnapshot();
    }

    const diagnostics = Array.isArray(deviceHostState?.diagnostics) ? deviceHostState.diagnostics : [];
    if (diagnostics.length) {
      frag.append(elh('div', { class: 'df-section-title', text: 'Host diagnostics' }));
      frag.append(jsonView(diagnostics.map((entry) => ({
        platform: entry.platform,
        ready: entry.ready,
        checks: (entry.checks || []).map((check) => ({
          name: check.name,
          status: check.status,
          message: check.message,
          version: check.version || null,
        })),
      }))));
    }
    dockBodyEl.replaceChildren(frag);
  }

  function renderSensors(j) {
    if (j && j.error) {
      clearDockSnapshot();
      hideDockActionStrip();
      dockEmpty(j.error, { kind: 'error', title: 'Could not refresh sensors' });
      return;
    }
    sensorsList = Array.isArray(j && j.sensors) ? j.sensors : [];
    sensorsView = 'list';
    setDockView('list');
    renderSensorsList();
  }

  function renderSensorsList() {
    const sensors = sensorsList;
    setDockActionStrip({
      visible: true,
      variant: 'actions',
      ariaLabel: 'Sensor actions',
      context: [
        layoutStatusPill(
          `${sensors.length} sensor${sensors.length === 1 ? '' : 's'}`,
          sensors.length ? 'success' : 'neutral'),
      ],
      primaryCommand: dataUi.button('Read location', {
        commandId: 'sensors.location.read',
        className: 'df-sensors-location-action',
        icon: 'i-location',
        primary: true,
        disabled: replaying || sensorsLocationBusy,
        onClick: () => executeDataCommand('sensors.location.read'),
      }),
    });
    const frag = document.createDocumentFragment();
    if (Array.isArray(sensors) && sensors.length) {
      const tbl = elh('table');
      tbl.append(elh('tr', null, elh('th', { text: 'Sensor' }), elh('th', { text: 'Supported' }), elh('th', { text: 'Active' }), elh('th', { text: 'Subscribers' })));
      for (const s of sensors) tbl.append(elh('tr', null,
        elh('td', { text: s.sensor || '' }), elh('td', { text: String(s.supported) }),
        elh('td', { text: String(s.active) }), elh('td', { text: String(s.subscribers != null ? s.subscribers : '') })));
      frag.append(tbl);
    } else {
      frag.append(dataUi.state({
        kind: 'empty',
        icon: 'i-check',
        title: 'No sensors reported',
        message: 'Supported sensor services appear here when the app exposes them.',
      }));
    }
    dockBodyEl.replaceChildren(frag);
    dockBodyEl.scrollTop = sensorsListScrollTop;
    if (Array.isArray(sensors) && sensors.length) {
      recordDockSnapshot('sensors', `Sensors · ${sensors.length}`, sensors, sensors.length, { geolocationIncluded: false });
    } else {
      clearDockSnapshot();
    }
  }

  function renderSensorsLocation() {
    const back = dataUi.button('Sensors', {
      commandId: 'sensors.back',
      className: 'df-data-back df-sensors-back',
      icon: 'i-chevron',
      onClick: () => {
        sensorsView = 'list';
        activateDockSubview('list', 'Sensors');
        renderSensorsList();
        requestAnimationFrame(() => dockActionStripEl.querySelector('.df-sensors-location-action')?.focus());
      },
    });
    const accuracy = sensorsLocation && Number.isFinite(Number(sensorsLocation.accuracy))
      ? `${Math.round(Number(sensorsLocation.accuracy))} m accuracy`
      : sensorsLocationBusy ? 'Reading' : sensorsLocationError ? 'Unavailable' : 'Location';
    setDockActionStrip({
      visible: true,
      variant: 'navigation',
      ariaLabel: 'Location actions',
      context: [
        back,
        layoutStatusPill(accuracy, sensorsLocationError ? 'error' : sensorsLocation ? 'success' : 'neutral'),
      ],
      primaryCommand: dataUi.button(sensorsLocationBusy ? 'Reading…' : 'Read again', {
        commandId: 'sensors.location.read',
        icon: 'i-location',
        primary: true,
        disabled: replaying || sensorsLocationBusy,
        onClick: () => executeDataCommand('sensors.location.read'),
      }),
    });

    if (sensorsLocationBusy) {
      dockBodyEl.replaceChildren(dataUi.state({
        kind: 'loading',
        icon: 'i-location',
        title: 'Reading location',
        message: 'Waiting for the app location service.',
      }));
    } else if (sensorsLocationError) {
      dockBodyEl.replaceChildren(dataUi.state({
        kind: 'error',
        icon: 'i-location',
        title: 'Location unavailable',
        message: sensorsLocationError,
      }));
    } else {
      dockBodyEl.replaceChildren(jsonView(sensorsLocation));
    }
  }

  async function readGeolocation() {
    if (replaying) { setStatus('Geolocation is disabled during replay.'); return; }
    if (sensorsView !== 'location') sensorsListScrollTop = dockBodyEl.scrollTop;
    sensorsView = 'location';
    activateDockSubview('location', 'Location');
    sensorsLocationBusy = true;
    sensorsLocationError = null;
    renderSensorsLocation();
    const j = await apiPost('/api/geolocation', {});
    if (dockActiveTab !== 'sensors' || sensorsView !== 'location') return;
    sensorsLocationBusy = false;
    sensorsLocation = j && j.ok ? j.location : null;
    sensorsLocationError = j && j.ok ? null : ((j && j.error) || 'Geolocation unavailable.');
    setDockMeta((j && j.ok ? 'location read ' : 'location unavailable · ') + new Date().toLocaleTimeString());
    renderSensorsLocation();
  }

  async function renderFiles(j) {
    const roots = j && j.roots;
    const frag = document.createDocumentFragment();
    const rootsArr = extractRoots(roots);
    if (!rootsArr.length) {
      clearDockSnapshot();
      hideDockActionStrip();
      dockEmpty((j && j.error) || 'No storage roots advertised by this app.');
      return;
    }
    filesRoots = rootsArr;
    if (!filesRoot || !rootsArr.some((r) => r.id === filesRoot)) filesRoot = rootsArr[0].id;
    frag.append(elh('div', { id: 'df-files-list' }));
    dockBodyEl.replaceChildren(frag);
    updateFilesActionStrip();
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

  function filesRootDescription(root) {
    if (!root) return '';
    const parts = [];
    if (root.kind === 'appData') parts.push('Private app storage');
    else if (root.kind) parts.push(root.kind);
    if (root.isPersistent) parts.push('persistent');
    if (root.isReadOnly) parts.push('root is read-only');
    return parts.join(' · ');
  }

  function updateFilesActionStrip() {
    const root = currentFilesRoot();
    if (!root) {
      hideDockActionStrip();
      return;
    }
    const select = dataActionSelect(
      'df-files-root',
      'Storage root',
      filesRoot,
      filesRoots.map((item) => [item.id, item.label]),
      (value) => executeDataCommand('files.root.change', { root: value }));
    setDockActionStrip({
      visible: true,
      variant: 'scope',
      ariaLabel: 'File browser scope',
      context: [
        layoutStatusPill('Browse only', 'neutral'),
        dataActionText(filesRootDescription(root) || root.label),
      ],
      persistentControls: [select],
    });
  }

  async function loadFiles(options = {}) {
    const list = document.getElementById('df-files-list');
    if (!list) return;
    const retained = options.refresh ? [...list.childNodes] : null;
    const loadGeneration = ++filesLoadGeneration;
    const dockGeneration = dockViewGeneration;
    const requestedRoot = filesRoot;
    const requestedPath = filesPath;
    if (!options.refresh) {
      clearDockSnapshot();
      list.textContent = 'Loading…';
    } else {
      list.setAttribute('aria-busy', 'true');
    }
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
      if (retained) {
        list.replaceChildren(...retained);
        list.setAttribute('aria-busy', 'false');
        setDockMeta('stale · refresh failed');
        return;
      }
      clearDockSnapshot();
      frag.append(dataUi.state({
        kind: 'error',
        icon: 'i-refresh',
        title: 'Could not browse files',
        message: j.error,
      }));
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
    list.setAttribute('aria-busy', 'false');
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

  // ── Layout diagnostics tab ──
  let latestLayoutReport = null;
  let selectedLayoutFindingId = null;
  let layoutSubview = 'findings';
  let layoutFocusedElementId = null;
  let layoutError = null;
  let layoutStale = false;
  let layoutScanBusy = false;
  let layoutScanPending = false;
  let layoutLiveTimer = null;
  const layoutRuleSet = [
    'layout.element-clipped',
    'layout.element-outside-window',
    'layout.content-overflow',
    'layout.text-not-fully-rendered',
    'layout.interaction-occluded',
    'layout.visual-occluded',
    'layout.geometric-overlap',
    'layout.accessibility-visibility-mismatch',
    'layout.visible-zero-area',
    'layout.constraint-violation',
    'layout.desired-size-constrained',
    'layout.child-outside-parent',
  ];
  const layoutOptions = {
    profile: 'agent',
    selectedScope: false,
    outcome: 'all',
    minimumSeverity: 'info',
    minimumConfidence: 'low',
    rule: '',
    includeSuppressed: false,
    live: false,
    advancedOpen: false,
    coverageOpen: false,
  };

  function actionableLayoutFindings(report, elementId = null) {
    return (Array.isArray(report?.findings) ? report.findings : []).filter((finding) => {
      if (!finding || finding.suppressed === true ||
          !['violation', 'observation'].includes(finding.outcome)) return false;
      const findingElementId = finding.elementId || finding.element?.id;
      return !elementId || findingElementId === elementId;
    });
  }

  function layoutHealthForElement(elementId) {
    const findings = layoutFindingsForElement(elementId)
      .filter((finding) => finding.outcome !== 'pass' && finding.suppressed !== true);
    const issues = findings.filter((finding) =>
      finding.outcome === 'violation' || finding.outcome === 'observation');
    const hasViolation = issues.some((finding) => finding.outcome === 'violation');
    const view = latestLayoutReport ? formatLayoutReport(latestLayoutReport) : null;
    const status = !latestLayoutReport && layoutScanBusy
      ? 'scanning'
      : !latestLayoutReport && layoutError
        ? 'error'
        : !latestLayoutReport
          ? 'idle'
          : layoutStale ? 'stale' : 'ready';
    const issueLabel = `${issues.length} issue${issues.length === 1 ? '' : 's'}`;
    const coverage = view?.coverageLabel || 'Unavailable';
    const message = status === 'idle'
      ? 'Check this page for clipping, overflow, overlap, and sizing issues.'
      : status === 'scanning'
        ? 'Checking the current page. Results will appear here when the scan completes.'
        : status === 'error'
          ? (layoutError || 'Layout diagnostics are unavailable for this app.')
          : status === 'stale'
            ? `The UI changed after this scan. ${issueLabel} may be outdated.`
            : issues.length
              ? `${issueLabel} for this element. Coverage: ${coverage}.`
              : `No findings for this element in the evaluated checks. Coverage: ${coverage}.`;
    return {
      status,
      findings,
      issueCount: issues.length,
      tone: hasViolation ? 'error' : issues.length ? 'warning' : 'neutral',
      label: status === 'idle'
        ? 'Not checked'
        : status === 'scanning'
          ? 'Checking'
          : status === 'error'
            ? 'Unavailable'
            : status === 'stale'
              ? 'Stale'
              : issues.length ? issueLabel : 'No findings',
      message,
    };
  }

  function updateLayoutStatus() {
    if (!tb.data || !tb.dataStatus) return;
    const previousState = tb.dataStatus.dataset.state || '';
    const previousTone = tb.dataStatus.dataset.tone || '';
    const previousText = tb.dataStatus.textContent || '';
    const issues = actionableLayoutFindings(latestLayoutReport);
    const hasViolation = issues.some((finding) => finding.outcome === 'violation');
    let state = 'idle';
    let text = '';
    let tone = '';
    const dataAction = dockEl.classList.contains('df-hidden') ? 'Open Data' : 'Close Data';
    let label = dataAction;

    if (layoutScanBusy) {
      state = latestLayoutReport ? 'ready' : 'busy';
      text = latestLayoutReport ? String(issues.length) : '…';
      tone = latestLayoutReport
        ? hasViolation ? 'error' : issues.length ? 'warning' : 'neutral'
        : 'neutral';
      label = `${dataAction}; layout check in progress`;
    } else if (!latestLayoutReport && layoutError) {
      state = 'error';
      text = '!';
      tone = 'error';
      label = `${dataAction}; layout diagnostics unavailable`;
    } else if (latestLayoutReport && layoutStale) {
      state = 'ready';
      text = issues.length ? String(issues.length) : '';
      tone = hasViolation ? 'error' : issues.length ? 'warning' : 'neutral';
      label = issues.length
        ? `${dataAction}; layout has ${issues.length} issue${issues.length === 1 ? '' : 's'} and may be outdated`
        : `${dataAction}; layout results may be outdated`;
    } else if (latestLayoutReport) {
      state = 'ready';
      text = issues.length ? String(issues.length) : '';
      tone = hasViolation ? 'error' : issues.length ? 'warning' : 'neutral';
      label = issues.length
        ? `${dataAction}; layout has ${issues.length} issue${issues.length === 1 ? '' : 's'}`
        : `${dataAction}; layout has no findings in the evaluated checks`;
    }

    tb.data.setAttribute('aria-busy', String(layoutScanBusy));
    tb.data.setAttribute('aria-label', label);
    tb.dataStatus.hidden = !text;
    tb.dataStatus.dataset.state = state;
    if (tone) tb.dataStatus.dataset.tone = tone;
    else tb.dataStatus.removeAttribute('data-tone');
    tb.dataStatus.textContent = text;

    const tab = document.getElementById('df-tab-layout');
    if (tab) {
      tab.textContent = latestLayoutReport && issues.length ? `Layout (${issues.length})` : 'Layout';
      tab.setAttribute('aria-label', latestLayoutReport
        ? `Layout, ${issues.length} issue${issues.length === 1 ? '' : 's'}`
        : 'Layout');
    }
    if (previousState !== state || previousTone !== tone || previousText !== text)
      scheduleToolbarLayout();
  }

  function updateLayoutSurfaces() {
    updateLayoutStatus();
    propertyGrid.refreshDiagnostics();
  }

  function markLayoutStale(reason = 'unknown') {
    if (!latestLayoutReport || layoutScanBusy || layoutStale) return;
    layoutStale = true;
    document.body.dataset.layoutStaleReason = reason;
    updateLayoutSurfaces();
    if (dockActiveTab === 'layout' && !dockEl.classList.contains('df-hidden'))
      renderLayoutDiagnostics();
  }

  function clearLayoutOverlays() {
    layoutOverlays?.replaceChildren();
  }

  function scheduleLayoutLiveScan(delay = 200) {
    if (!layoutOptions.live ||
        !connected ||
        document.hidden) return;
    if (layoutLiveTimer) clearTimeout(layoutLiveTimer);
    layoutLiveTimer = setTimeout(() => {
      layoutLiveTimer = null;
      runLayoutScan();
    }, delay);
  }

  function layoutRegionPoints(region) {
    if (!region) return [];
    if (Array.isArray(region.points) && region.points.length >= 3) return region.points;
    const bounds = region.bounds;
    if (!bounds) return [];
    return [
      { x: bounds.x, y: bounds.y },
      { x: bounds.x + bounds.width, y: bounds.y },
      { x: bounds.x + bounds.width, y: bounds.y + bounds.height },
      { x: bounds.x, y: bounds.y + bounds.height },
    ];
  }

  function appendLayoutRegion(svg, region, className) {
    const points = layoutRegionPoints(region);
    if (!svg || points.length < 3) return;
    const polygon = document.createElementNS('http://www.w3.org/2000/svg', 'polygon');
    polygon.setAttribute('class', `df-layout-region ${className}`);
    polygon.setAttribute('points', points
      .map((point) => `${point.x - rootOffsetX},${point.y - rootOffsetY}`)
      .join(' '));
    svg.appendChild(polygon);
  }

  function renderLayoutOverlays(finding) {
    clearLayoutOverlays();
    if (!layoutOverlays || !finding || !finding.evidence) return;
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', `0 0 ${parseFloat(viewport.dataset.width) || 1} ${parseFloat(viewport.dataset.height) || 1}`);
    svg.setAttribute('preserveAspectRatio', 'none');
    appendLayoutRegion(svg, finding.evidence.fullRegion, 'df-layout-region-full');
    appendLayoutRegion(svg, finding.evidence.visibleRegion, 'df-layout-region-visible');
    appendLayoutRegion(svg, finding.evidence.contentRegion, 'df-layout-region-content');
    for (const clip of finding.evidence.clipChain || [])
      appendLayoutRegion(svg, clip.region, 'df-layout-region-clip');
    appendLayoutRegion(svg, finding.evidence.overlap?.intersectionRegion, 'df-layout-region-overlap');
    if (!svg.childNodes.length) return;
    layoutOverlays.appendChild(svg);
    const legend = elh('div', { class: 'df-layout-overlay-legend' });
    const entries = [
      ['df-layout-legend-full', 'Affected bounds', finding.evidence.fullRegion],
      ['df-layout-legend-visible', 'Visible region', finding.evidence.visibleRegion],
      ['df-layout-legend-content', 'Content region', finding.evidence.contentRegion],
      ['df-layout-legend-clip', 'Clip', finding.evidence.clipChain?.length],
      ['df-layout-legend-overlap', 'Overlap', finding.evidence.overlap?.intersectionRegion],
    ];
    for (const [className, label, present] of entries) {
      if (!present) continue;
      legend.append(elh('span', null,
        elh('i', { class: className }),
        elh('span', { text: label })));
    }
    layoutOverlays.appendChild(legend);
  }

  function layoutSelect(label, value, choices, onChange) {
    const wrapper = elh('label', { class: 'df-layout-field' }, elh('span', { text: label }));
    const select = elh('select', { 'aria-label': label });
    for (const [optionValue, optionLabel] of choices) {
      const option = elh('option', { value: optionValue, text: optionLabel });
      option.selected = optionValue === value;
      select.append(option);
    }
    select.addEventListener('change', () => onChange(select.value));
    wrapper.append(select);
    return wrapper;
  }

  function layoutFindingsForElement(elementId) {
    if (!latestLayoutReport || !elementId) return [];
    return formatLayoutReport(latestLayoutReport, {
      outcome: 'all',
      minimumSeverity: 'info',
      minimumConfidence: 'low',
      includeSuppressed: true,
    }).findings.filter((finding) => finding.elementId === elementId);
  }

  function selectLayoutFinding(finding) {
    selectedLayoutFindingId = finding.id;
    for (const row of dockBodyEl.querySelectorAll('[data-layout-finding-id]'))
      row.classList.toggle('df-selected', row.dataset.layoutFindingId === finding.id);
    renderLayoutOverlays(finding);
    if (!finding.elementId) return;
    const target = elById(finding.elementId);
    if (!target) {
      setStatus('The affected element is no longer present in the current frame. Rescan Layout.');
      return;
    }
    selectElement(finding.elementId);
    propertyGrid.open(target);
  }

  function openLayoutFindingDetail(finding) {
    selectedLayoutFindingId = finding.id;
    renderLayoutOverlays(finding);
    showLayoutSubview('detail', '.df-data-action-strip .df-layout-back');
  }

  function showLayoutSubview(view, focusSelector = null) {
    layoutSubview = view;
    activateDockSubview(view, 'Layout');
    renderLayoutDiagnostics();
    if (focusSelector)
      requestAnimationFrame(() => document.querySelector(focusSelector)?.focus());
  }

  function setLayoutCopilotSnapshot(report, findingId) {
    const payload = createLayoutDataPayload(
      report,
      findingId,
      findingId
        ? null
        : {
            outcome: layoutOptions.outcome,
            minimumSeverity: layoutOptions.minimumSeverity,
            minimumConfidence: layoutOptions.minimumConfidence,
            rule: layoutOptions.rule,
            includeSuppressed: layoutOptions.includeSuppressed,
          });
    const title = findingId ? 'Selected layout finding' : 'Layout diagnostics';
    recordDockSnapshot(
      'layout',
      title,
      payload,
      payload.findings.length,
      {
        snapshotId: payload.snapshot.id || null,
        treeRevision: payload.snapshot.treeRevision || null,
        selectedFindingId: findingId || null,
      });
  }

  function layoutStatusPill(label, tone = 'neutral') {
    return dataStatusPill(label, tone);
  }

  function layoutActionButton(label, icon, className = '') {
    return elh(
      'button',
      { class: `df-layout-button ${className}`.trim(), type: 'button' },
      icon ? svgIcon(icon) : null,
      elh('span', { text: label }));
  }

  function formatLayoutCaptureTime(value) {
    if (!value) return null;
    const captured = new Date(value);
    return Number.isNaN(captured.getTime())
      ? null
      : captured.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit', second: '2-digit' });
  }

  function layoutScopeLabel(view) {
    const target = view.scopeRootId ? elById(view.scopeRootId) : null;
    const label = target ? elementLabel(target) : 'Current page';
    return `${layoutOptions.selectedScope ? 'Selected subtree' : 'Current page'} · ${label}`;
  }

  function layoutSummaryCard(label, value, tone, detail) {
    const card = elh('div', { class: `df-summary-card df-summary-card-${tone || 'neutral'}` });
    card.append(
      elh('span', { class: 'df-summary-label', text: label }),
      elh('strong', { class: 'df-summary-value', text: String(value) }));
    if (detail) card.append(elh('span', { class: 'df-summary-detail', text: detail }));
    return card;
  }

  function layoutEvidenceLine(finding) {
    const evidence = finding.evidence;
    if (!evidence) return null;
    const bounds = evidence.fullRegion?.bounds || evidence.visibleRegion?.bounds;
    const parts = [];
    if (bounds) {
      parts.push(
        `${Math.round(bounds.width * 10) / 10} × ${Math.round(bounds.height * 10) / 10}` +
        ` at ${Math.round(bounds.x * 10) / 10}, ${Math.round(bounds.y * 10) / 10}`);
    }
    if (evidence.lostAreaRatio != null)
      parts.push(`${Math.round(evidence.lostAreaRatio * 100)}% outside the visible region`);
    if (evidence.overlap?.overlapAreaRatio != null)
      parts.push(`${Math.round(evidence.overlap.overlapAreaRatio * 100)}% overlap`);
    if (evidence.text?.isTruncated === true)
      parts.push('Native text metrics report truncation');
    return parts.length ? parts.join(' · ') : null;
  }

  function resetLayoutFilters() {
    layoutOptions.outcome = 'all';
    layoutOptions.minimumSeverity = 'info';
    layoutOptions.minimumConfidence = 'low';
    layoutOptions.rule = '';
    layoutOptions.includeSuppressed = false;
    layoutFocusedElementId = null;
    if (latestLayoutReport)
      renderLayoutDiagnostics();
  }

  function renderLayoutCoverage(view) {
    const details = elh('details', { class: 'df-disclosure df-layout-coverage' });
    details.open = layoutOptions.coverageOpen;
    details.addEventListener('toggle', () => { layoutOptions.coverageOpen = details.open; });
    const tone = view.coverageOverall === 'full'
      ? 'success'
      : view.coverageOverall === 'partial' ? 'warning' : 'neutral';
    const supported = view.rules.filter((rule) => rule.support !== 'unavailable');
    const unavailable = view.rules.filter((rule) => rule.support === 'unavailable');
    const summary = elh('summary', null,
      elh('span', { class: 'df-disclosure-title', text: 'Coverage and limitations' }),
      elh('span', {
        class: 'df-disclosure-meta',
        text: `${supported.length} available · ${unavailable.length} unavailable`,
      }),
      layoutStatusPill(view.coverageLabel, tone));
    details.append(summary);

    const body = elh('div', { class: 'df-disclosure-body' });
    body.append(elh('p', {
      class: 'df-layout-guidance',
      text: view.coverageOverall === 'full'
        ? 'Every enabled check had the evidence it needed in this scope.'
        : 'Missing evidence is reported as incomplete or unavailable, never as a clean pass.',
    }));

    if (supported.length) {
      body.append(elh('h5', { class: 'df-layout-subheading', text: 'Available checks' }));
      const list = elh('div', { class: 'df-layout-rule-list' });
      for (const rule of supported) {
        list.append(elh('div', { class: 'df-layout-rule-row' },
          elh('div', { class: 'df-layout-rule-name' },
            elh('strong', { text: rule.label }),
            elh('code', { text: rule.ruleId })),
          layoutStatusPill(rule.supportLabel, rule.support === 'full' ? 'success' : 'warning'),
          elh('span', { class: 'df-layout-rule-count', text: rule.detail })));
      }
      body.append(list);
    }

    if (unavailable.length) {
      const unavailableDetails = elh('details', { class: 'df-disclosure df-layout-nested-disclosure' });
      unavailableDetails.append(elh('summary', null,
        elh('span', { class: 'df-disclosure-title', text: `${unavailable.length} unavailable checks` }),
        elh('span', { class: 'df-disclosure-meta', text: 'Waiting for native or WebView evidence' })));
      const list = elh('div', { class: 'df-layout-rule-list' });
      for (const rule of unavailable) {
        const row = elh('div', { class: 'df-layout-rule-row df-layout-rule-unavailable' },
          elh('div', { class: 'df-layout-rule-name' },
            elh('strong', { text: rule.label }),
            elh('code', { text: rule.ruleId })),
          layoutStatusPill('Unavailable', 'neutral'));
        if (rule.limitations.length)
          row.append(elh('span', { class: 'df-layout-rule-reason', text: rule.limitations[0] }));
        list.append(row);
      }
      unavailableDetails.append(elh('div', { class: 'df-disclosure-body' }, list));
      body.append(unavailableDetails);
    }

    if (view.limitations.length || view.neverCaptured.length) {
      const limits = elh('details', { class: 'df-disclosure df-layout-nested-disclosure' });
      limits.append(elh('summary', null,
        elh('span', { class: 'df-disclosure-title', text: 'Known limitations and privacy' })));
      const limitsBody = elh('div', { class: 'df-disclosure-body' });
      if (view.limitations.length) {
        const list = elh('ul', { class: 'df-layout-list' });
        for (const limitation of view.limitations) list.append(elh('li', { text: limitation }));
        limitsBody.append(list);
      }
      if (view.neverCaptured.length) {
        limitsBody.append(
          elh('h5', { class: 'df-layout-subheading', text: 'Never captured' }),
          elh('p', { class: 'df-layout-guidance', text: view.neverCaptured.join(', ') }));
      }
      limits.append(limitsBody);
      body.append(limits);
    }

    body.append(elh('p', {
      class: 'df-layout-technical',
      text: `${view.counts.passes} evaluated passes · ${view.counts.notApplicable} not applicable · ${view.version}`,
    }));
    details.append(body);
    return details;
  }

  // No Inspector host in this layer can mutate the project's layout suppression policy, so an
  // approval-required result is always terminal here: copy the exact proposal for review instead.
  async function completeLayoutSuppression(result) {
    if (result?.type !== 'WorkspaceApprovalRequired' || !result.proposal)
      return result;
    await copyText(JSON.stringify(result.proposal, null, 2));
    setStatus('Copied the exact suppression proposal. Applying it to the project is not available in this Inspector.');
    return null;
  }

  function renderLayoutFinding(finding, report) {
    const tone = finding.outcome === 'violation'
      ? 'error'
      : finding.outcome === 'observation' ? 'warning' : 'neutral';
    const card = elh('article', {
      class: `df-card df-layout-finding-card df-layout-${finding.outcome}` +
        (finding.suppressed ? ' df-suppressed' : '') +
        (finding.id === selectedLayoutFindingId ? ' df-selected' : ''),
    });
    card.dataset.layoutFindingId = finding.id;

    const heading = elh('div', { class: 'df-layout-finding-heading' });
    const title = elh('div', { class: 'df-layout-finding-title' });
    title.append(
      layoutStatusPill(finding.outcomeLabel, tone),
      elh('h5', { text: finding.ruleLabel }));
    heading.append(
      title,
      elh('span', {
        class: 'df-layout-severity',
        text: `${finding.severityLabel} · ${finding.confidenceLabel}`,
      }));
    card.append(
      heading,
      elh('p', { class: 'df-layout-finding-message', text: finding.message }));
    if (finding.context)
      card.append(elh('p', { class: 'df-layout-finding-context', text: finding.context }));

    const actions = elh('div', { class: 'df-action-row df-layout-finding-actions' });
    if (finding.elementId) {
      const show = layoutActionButton('Show in app', 'i-inspect', 'df-layout-button-primary');
      show.addEventListener('click', () => selectLayoutFinding(finding));
      actions.append(show);
    }

    const why = elh('details', { class: 'df-disclosure df-layout-finding-details' });
    why.append(elh('summary', null,
      elh('span', { class: 'df-disclosure-title', text: 'Why and evidence' })));
    const whyBody = elh('div', { class: 'df-disclosure-body' });
    whyBody.append(elh('p', { class: 'df-layout-guidance', text: finding.explanation }));
    const evidenceLine = layoutEvidenceLine(finding);
    if (evidenceLine)
      whyBody.append(elh('p', { class: 'df-layout-evidence-line', text: evidenceLine }));
    if (finding.fixCategories.length || finding.actionability) {
      const tags = elh('div', { class: 'df-layout-tags' });
      tags.append(elh('span', { class: 'df-layout-tag', text: finding.actionability }));
      for (const category of finding.fixCategories)
        tags.append(elh('span', { class: 'df-layout-tag', text: category.replace(/-/g, ' ') }));
      if (finding.suppressed)
        tags.append(elh('span', {
          class: 'df-layout-tag',
          text: finding.suppressionReason || 'suppressed',
        }));
      whyBody.append(tags);
    }
    if (finding.relatedElements.length) {
      whyBody.append(elh('h6', { class: 'df-layout-subheading', text: 'Related elements' }));
      const related = elh('div', { class: 'df-action-row df-layout-related' });
      for (const item of finding.relatedElements) {
        const button = layoutActionButton(
          `${item.relation}: ${item.element.automationId || item.element.type}`,
          'i-inspect');
        button.addEventListener('click', () => {
          const target = elById(item.element.id);
          if (!target) {
            setStatus('The related element is no longer present in the current frame.');
            return;
          }
          selectElement(item.element.id);
          propertyGrid.open(target);
        });
        related.append(button);
      }
      whyBody.append(related);
    }
    if (finding.limitations.length) {
      const limits = elh('ul', { class: 'df-layout-list df-layout-finding-limitations' });
      for (const limitation of finding.limitations) limits.append(elh('li', { text: limitation }));
      whyBody.append(limits);
    }
    why.append(whyBody);
    actions.append(why);

    const more = elh('details', { class: 'df-disclosure df-layout-more-actions' });
    more.append(elh('summary', null,
      svgIcon('i-more'),
      elh('span', { class: 'df-disclosure-title', text: 'More' })));
    const moreBody = elh('div', { class: 'df-layout-more-menu' });
    if (finding.elementId && finding.element?.sourceFile) {
      const source = layoutActionButton('Open source', 'i-source');
      source.addEventListener('click', async () => {
        selectLayoutFinding(finding);
        await openSource();
      });
      moreBody.append(source);
    }
    const copilot = layoutActionButton('Add to Copilot', 'i-copilot');
    copilot.addEventListener('click', async () => {
      setLayoutCopilotSnapshot(report, finding.id);
      await attachDockDataToCopilot();
    });
    const copy = layoutActionButton('Copy payload', 'i-data');
    copy.addEventListener('click', async () => {
      const ok = await copyText(JSON.stringify(createLayoutDataPayload(report, finding.id), null, 2));
      setStatus(ok ? 'Copied the selected layout finding.' : 'Could not copy the layout finding.');
    });
    moreBody.append(copilot, copy);
    if (finding.outcome !== 'pass' && finding.id) {
      const suppression = layoutActionButton(
        finding.suppressed ? 'Unsuppress' : 'Suppress',
        finding.suppressed ? 'i-refresh' : 'i-close');
      suppression.addEventListener('click', async () => {
        suppression.disabled = true;
        const endpoint = finding.suppressed
          ? '/api/diagnostics/unsuppress'
          : '/api/diagnostics/suppress';
        let result = await apiPost(endpoint, {
          findingId: finding.id,
          reason: finding.suppressed ? null : 'Suppressed in DevFlow Inspector',
        });
        result = await completeLayoutSuppression(result);
        if (!result || result.ok === false) {
          suppression.disabled = false;
          setStatus((result && (result.error || result.message)) || 'The layout suppression policy could not be changed.');
          return;
        }
        setStatus(finding.suppressed
          ? 'Removed the exact project layout suppression.'
          : 'Saved an exact layout suppression in .mauidevflow.');
        await runLayoutScan();
      });
      moreBody.append(suppression);
    }
    more.append(moreBody);
    actions.append(more);
    card.append(actions);
    return card;
  }

  function compactLayoutCount(view) {
    return layoutFocusedElementId
      ? String(view.findings.length)
      : view.matchingFindings === view.totalFindings
        ? String(view.totalFindings)
        : `${view.matchingFindings} of ${view.totalFindings}`;
  }

  function createLayoutFilterMenu() {
    const filters = elh('details', { class: 'df-layout-filter-menu' });
    filters.append(elh('summary', { class: 'df-data-button', 'aria-label': 'Layout filters' },
      svgIcon('i-more'),
      elh('span', { text: 'Filters' })));
    const body = elh('div', { class: 'df-layout-filter-popover' });
    const search = elh('input', {
      class: 'df-field',
      type: 'search',
      value: layoutOptions.rule,
      placeholder: 'Search findings',
      'aria-label': 'Search layout findings',
    });
    search.addEventListener('change', () => {
      layoutOptions.rule = search.value;
      renderLayoutDiagnostics();
    });
    body.append(
      search,
      layoutSelect('Finding type', layoutOptions.outcome, [
        ['all', 'All findings'],
        ['violations', 'Issues'],
        ['observations', 'Observations'],
        ['incomplete', 'Incomplete'],
      ], (value) => {
        layoutOptions.outcome = value;
        renderLayoutDiagnostics();
      }),
      layoutSelect('Profile for next check', layoutOptions.profile, [
        ['agent', 'Agent'],
        ['strict', 'Strict'],
        ['exhaustive', 'Exhaustive'],
        ['ci', 'CI'],
      ], (value) => { layoutOptions.profile = value; }),
      layoutSelect('Minimum severity', layoutOptions.minimumSeverity, [
        ['info', 'Info+'],
        ['minor', 'Minor+'],
        ['moderate', 'Moderate+'],
        ['serious', 'Serious+'],
        ['critical', 'Critical'],
      ], (value) => {
        layoutOptions.minimumSeverity = value;
        renderLayoutDiagnostics();
      }),
      layoutSelect('Minimum confidence', layoutOptions.minimumConfidence, [
        ['low', 'Low+'],
        ['medium', 'Medium+'],
        ['high', 'High+'],
        ['exact', 'Exact'],
      ], (value) => {
        layoutOptions.minimumConfidence = value;
        renderLayoutDiagnostics();
      }));
    const selectedScope = elh('input', { type: 'checkbox' });
    selectedScope.checked = layoutOptions.selectedScope;
    selectedScope.disabled = !selectedId;
    selectedScope.addEventListener('change', () => {
      layoutOptions.selectedScope = selectedScope.checked;
      setStatus('Layout scope updated for the next check.');
    });
    const suppressed = elh('input', { type: 'checkbox' });
    suppressed.checked = layoutOptions.includeSuppressed;
    suppressed.addEventListener('change', () => {
      layoutOptions.includeSuppressed = suppressed.checked;
      renderLayoutDiagnostics();
    });
    body.append(
      elh('label', { class: 'df-layout-check' },
        selectedScope, elh('span', { text: 'Scan selected subtree next' })),
      elh('label', { class: 'df-layout-check' },
        suppressed, elh('span', { text: 'Show suppressed findings' })));
    filters.append(body);
    return filters;
  }

  function createLayoutLiveToggle() {
    const input = elh('input', { type: 'checkbox', 'aria-label': 'Live layout diagnostics' });
    input.checked = layoutOptions.live;
    const toggle = elh('label', { class: 'df-layout-live-toggle df-layout-live-compact' },
      input,
      elh('span', { class: 'df-layout-switch', 'aria-hidden': 'true' }),
      elh('span', { text: 'Live' }));
    input.addEventListener('change', () => {
      layoutOptions.live = input.checked;
      if (layoutOptions.live) scheduleLayoutLiveScan(0);
      else if (layoutLiveTimer) {
        clearTimeout(layoutLiveTimer);
        layoutLiveTimer = null;
      }
    });
    return toggle;
  }

  function setLayoutFindingsActionStrip(view = null) {
    const context = [];
    if (view) {
      const count = compactLayoutCount(view);
      context.push(layoutStatusPill(`${count} finding${count === '1' ? '' : 's'}`, view.status.tone));
      if (layoutStale || layoutError)
        context.push(layoutStatusPill(layoutError ? 'Refresh failed' : 'Stale', layoutError ? 'error' : 'warning'));
    } else {
      context.push(layoutStatusPill(layoutScanBusy ? 'Checking' : layoutError ? 'Unavailable' : 'Not checked',
        layoutError ? 'error' : 'neutral'));
      context.push(dataActionText(layoutOptions.selectedScope ? 'Selected subtree' : 'Current page'));
    }
    const coverage = view
      ? dataUi.button(`${view.coverageLabel} coverage`, {
          commandId: 'layout.coverage',
          className: 'df-layout-coverage-button',
          onClick: () => showLayoutSubview('coverage', '.df-data-action-strip .df-layout-back'),
        })
      : null;
    const scanLabel = layoutScanBusy ? 'Checking…' : view ? 'Rescan' : layoutError ? 'Retry' : 'Start check';
    setDockActionStrip({
      visible: true,
      variant: 'actions',
      ariaLabel: 'Layout actions',
      context,
      persistentControls: [createLayoutFilterMenu(), createLayoutLiveToggle()],
      secondaryCommands: coverage ? [coverage] : [],
      primaryCommand: dataUi.button(scanLabel, {
        commandId: 'layout.scan',
        icon: 'i-refresh',
        primary: true,
        disabled: layoutScanBusy,
        onClick: () => executeDataCommand('layout.scan'),
      }),
    });
  }

  function compactLayoutCoverage(view) {
    const tone = view.coverageOverall === 'full'
      ? 'success'
      : view.coverageOverall === 'partial' ? 'warning' : 'neutral';
    const back = dataUi.button('Findings', {
      commandId: 'layout.back',
      icon: 'i-chevron',
      className: 'df-data-back df-layout-back',
      onClick: () => showLayoutSubview('findings', '.df-data-action-strip .df-layout-coverage-button'),
    });
    setDockActionStrip({
      visible: true,
      variant: 'navigation',
      ariaLabel: 'Layout coverage navigation',
      context: [
        back,
        dataActionText('Coverage'),
        layoutStatusPill(view.coverageLabel, tone),
        dataActionText(view.scope),
      ],
    });

    const root = elh('div', { class: 'df-layout-subview' });
    const table = elh('table', { class: 'df-layout-compact-table' });
    table.append(elh('thead', null, elh('tr', null,
      elh('th', { text: 'Check' }),
      elh('th', { text: 'Support' }),
      elh('th', { text: 'Evaluated' }))));
    const body = elh('tbody');
    for (const rule of view.rules) {
      body.append(elh('tr', null,
        elh('td', null,
          elh('span', { text: rule.label }),
          elh('code', { class: 'df-layout-rule-code', text: rule.ruleId })),
        elh('td', null, layoutStatusPill(
          rule.supportLabel,
          rule.support === 'full' ? 'success' : rule.support === 'partial' ? 'warning' : 'neutral')),
        elh('td', { text: rule.detail })));
    }
    table.append(body);
    root.append(table);
    const limitations = [...view.limitations, ...view.neverCaptured.map((item) => `Never captured: ${item}`)];
    if (limitations.length) {
      const details = elh('details', { class: 'df-layout-compact-details' });
      details.append(elh('summary', { text: `${limitations.length} limitations and privacy notes` }));
      const list = elh('ul', { class: 'df-layout-list' });
      for (const limitation of limitations) list.append(elh('li', { text: limitation }));
      details.append(list);
      root.append(details);
    }
    return root;
  }

  function compactLayoutFindingDetail(finding, report) {
    const tone = finding.outcome === 'violation'
      ? 'error'
      : finding.outcome === 'observation' ? 'warning' : 'neutral';
    const view = formatLayoutReport(report);
    const back = dataUi.button('Findings', {
      commandId: 'layout.back',
      icon: 'i-chevron',
      className: 'df-data-back df-layout-back',
      onClick: () => {
        showLayoutSubview('findings');
        requestAnimationFrame(() => {
          dockBodyEl.querySelector(`[data-layout-finding-id="${CSS.escape(finding.id)}"]`)?.focus();
        });
      },
    });
    setDockActionStrip({
      visible: true,
      variant: 'navigation',
      ariaLabel: 'Layout finding actions',
      context: [
        back,
        layoutStatusPill(finding.outcomeLabel, tone),
        dataActionText(finding.ruleLabel),
      ],
      secondaryCommands: [
        dataUi.button(`${view.coverageLabel} coverage`, {
          commandId: 'layout.coverage',
          className: 'df-layout-coverage-button',
          onClick: () => showLayoutSubview('coverage', '.df-data-action-strip .df-layout-back'),
        }),
      ],
      primaryCommand: dataUi.button('Show in app', {
        commandId: 'layout.show',
        icon: 'i-inspect',
        primary: true,
        onClick: () => selectLayoutFinding(finding),
      }),
    });

    const root = elh('div', { class: 'df-layout-subview' });
    const detail = elh('div', { class: 'df-layout-detail' },
      elh('p', { class: 'df-layout-finding-message', text: finding.message }),
      finding.context ? elh('p', { class: 'df-layout-finding-context', text: finding.context }) : null,
      elh('p', { class: 'df-layout-guidance', text: finding.explanation }));
    const evidence = layoutEvidenceLine(finding);
    if (evidence) detail.append(elh('p', { class: 'df-layout-evidence-line', text: evidence }));
    if (finding.limitations.length) {
      const list = elh('ul', { class: 'df-layout-list' });
      for (const limitation of finding.limitations) list.append(elh('li', { text: limitation }));
      detail.append(list);
    }
    const actions = elh('div', { class: 'df-layout-detail-actions' });
    if (finding.elementId && finding.element?.sourceFile) {
      actions.append(dataUi.button('Open source', {
        icon: 'i-source',
        onClick: async () => {
          selectLayoutFinding(finding);
          await openSource();
        },
      }));
    }
    actions.append(
      dataUi.button('Add to Copilot', {
        icon: 'i-copilot',
        onClick: async () => {
          setLayoutCopilotSnapshot(report, finding.id);
          await attachDockDataToCopilot();
        },
      }),
      dataUi.button('Copy payload', {
        icon: 'i-data',
        onClick: async () => {
          const ok = await copyText(JSON.stringify(createLayoutDataPayload(report, finding.id), null, 2));
          setStatus(ok ? 'Copied the selected layout finding.' : 'Could not copy the layout finding.');
        },
      }));
    if (finding.outcome !== 'pass' && finding.id) {
      actions.append(dataUi.button(finding.suppressed ? 'Unsuppress' : 'Suppress', {
        icon: finding.suppressed ? 'i-refresh' : 'i-close',
        onClick: async (event) => {
          const button = event.currentTarget;
          button.disabled = true;
          let result = await apiPost(
            finding.suppressed ? '/api/diagnostics/unsuppress' : '/api/diagnostics/suppress',
            {
              findingId: finding.id,
              reason: finding.suppressed ? null : 'Suppressed in DevFlow Inspector',
            });
          result = await completeLayoutSuppression(result);
          if (!result || result.ok === false) {
            button.disabled = false;
            setStatus((result && (result.error || result.message)) || 'The layout suppression policy could not be changed.');
            return;
          }
          showLayoutSubview('findings');
          await executeDataCommand('layout.scan');
        },
      }));
    }
    detail.append(actions);
    root.append(detail);
    return root;
  }

  function compactLayoutFindingRow(finding) {
    const tone = finding.outcome === 'violation'
      ? 'error'
      : finding.outcome === 'observation' ? 'warning' : 'neutral';
    const row = elh('button', {
      class: `df-layout-finding-row df-layout-${finding.outcome}` +
        (finding.suppressed ? ' df-suppressed' : ''),
      type: 'button',
      'data-layout-finding-id': finding.id,
      'aria-label': `${finding.outcomeLabel}: ${finding.ruleLabel}. ${finding.message}`,
    });
    row.addEventListener('click', () => openLayoutFindingDetail(finding));
    row.append(
      layoutStatusPill(finding.outcomeLabel, tone),
      elh('span', { class: 'df-layout-row-main' },
        elh('strong', { text: finding.ruleLabel }),
        elh('span', { text: finding.message })),
      elh('span', {
        class: 'df-layout-row-meta',
        text: [finding.context, finding.severityLabel, finding.confidenceLabel].filter(Boolean).join(' · '),
      }),
      svgIcon('i-chevron'));
    return row;
  }

  function renderLayoutDiagnostics() {
    if (!latestLayoutReport) {
      clearDockSnapshot();
      setLayoutFindingsActionStrip(null);
      dockBodyEl.replaceChildren(dataUi.state({
        kind: layoutScanBusy ? 'loading' : layoutError ? 'error' : 'empty',
        icon: layoutScanBusy || layoutError ? 'i-refresh' : 'i-layout',
        title: layoutScanBusy ? 'Checking layout' : layoutError ? 'Layout check unavailable' : 'Layout not checked',
        message: layoutScanBusy
          ? 'Scanning the current page for clipping, overflow, overlap, and sizing issues.'
          : layoutError || 'Start a check for clipping, overflow, overlap, and sizing issues.',
      }));
      return;
    }

    const reportFinding = selectedLayoutFindingId
      ? latestLayoutReport.findings?.find((finding) => finding?.id === selectedLayoutFindingId)
      : null;
    if (layoutSubview === 'detail' && !reportFinding) {
      layoutSubview = 'findings';
      activateDockSubview('findings', 'Layout');
      selectedLayoutFindingId = null;
      clearLayoutOverlays();
      setStatus('The selected Layout finding is no longer present after the latest check.');
    }

    const view = formatLayoutReport(latestLayoutReport, {
      outcome: layoutOptions.outcome,
      minimumSeverity: layoutOptions.minimumSeverity,
      minimumConfidence: layoutOptions.minimumConfidence,
      rule: layoutOptions.rule,
      includeSuppressed: layoutOptions.includeSuppressed,
    });
    if (layoutFocusedElementId) {
      view.findings = view.findings.filter((finding) => finding.elementId === layoutFocusedElementId);
      view.findingsTruncated = false;
    }

    if (layoutSubview === 'coverage') {
      dockBodyEl.replaceChildren(compactLayoutCoverage(view));
      setLayoutCopilotSnapshot(latestLayoutReport, null);
      return;
    }
    if (layoutSubview === 'detail' && reportFinding) {
      const finding = formatLayoutReport({ ...latestLayoutReport, findings: [reportFinding] }).findings[0];
      dockBodyEl.replaceChildren(compactLayoutFindingDetail(finding, latestLayoutReport));
      setLayoutCopilotSnapshot(latestLayoutReport, finding.id);
      return;
    }

    setLayoutFindingsActionStrip(view);
    const root = elh('div', { class: 'df-layout-root df-layout-compact' });
    if (layoutFocusedElementId) {
      const focusedElement = elById(layoutFocusedElementId);
      root.append(elh('div', { class: 'df-layout-focus df-layout-focus-compact' },
        elh('span', { text: `Selected element · ${focusedElement ? elementLabel(focusedElement) : 'Element no longer present'}` }),
        dataUi.button('Clear', {
          icon: 'i-close',
          onClick: () => {
            layoutFocusedElementId = null;
            selectedLayoutFindingId = null;
            clearLayoutOverlays();
            renderLayoutDiagnostics();
          },
        })));
    }

    const list = elh('div', { class: 'df-layout-finding-list df-layout-compact-list' });
    if (view.systemEvidence) {
      const system = view.systemEvidence;
      root.append(elh('p', {
        class: 'df-layout-limit-note',
        text: system.status === 'complete'
          ? `Device evidence complete · ${system.externalElementCount} external element${system.externalElementCount === 1 ? '' : 's'}${system.keyboardVisible ? ' · keyboard visible' : ''}.`
          : `Device evidence ${system.status}: ${system.limitations[0] || 'system UI could not be correlated safely.'}`,
      }));
    }
    if (view.findingsTruncated) {
      root.append(elh('p', {
        class: 'df-layout-limit-note',
        text: `Showing ${view.findings.length} of ${view.matchingFindings} matching findings. Narrow the filters to inspect the rest.`,
      }));
    }
    if (!view.findings.length) {
      const action = view.totalFindings
        ? dataUi.button('Reset filters', { icon: 'i-refresh', onClick: resetLayoutFilters })
        : view.coverageOverall !== 'full'
          ? dataUi.button('Review coverage', {
              icon: 'i-data',
              onClick: () => showLayoutSubview('coverage', '.df-data-action-strip .df-layout-back'),
            })
          : null;
      list.append(dataUi.state({
        kind: 'empty',
        icon: view.totalFindings ? 'i-inspect' : 'i-check',
        title: view.totalFindings ? 'No findings match these filters' : 'No layout findings',
        message: view.totalFindings
          ? 'Reset the filters to see the complete result.'
          : view.coverageOverall === 'full'
            ? 'Every enabled check completed without a finding.'
            : `No findings in the evaluated checks. Coverage is ${view.coverageLabel.toLowerCase()}.`,
        action,
      }));
    } else {
      for (const finding of view.findings) list.append(compactLayoutFindingRow(finding));
    }
    root.append(list);
    dockBodyEl.replaceChildren(root);
    setLayoutCopilotSnapshot(latestLayoutReport, null);
  }
  async function runLayoutScan() {
    if (!inspectorSurfaces.layoutDiagnostics) return;
    if (layoutScanBusy) {
      layoutScanPending = true;
      return;
    }
    const renderedElements = [...viewport.querySelectorAll('.devflow-element')];
    const activeVisualRootId = chooseLayoutScopeRoot(renderedElements.map((element) => ({
      id: element.getAttribute('data-id'),
      type: element.getAttribute('data-type'),
    })));
    const scopedElementId = layoutOptions.selectedScope ? selectedId : activeVisualRootId;
    if (layoutOptions.selectedScope && !scopedElementId) {
      setStatus('Select an element before scanning its subtree.');
      return;
    }

    layoutScanBusy = true;
    layoutScanPending = false;
    layoutError = null;
    clearLayoutOverlays();
    updateLayoutSurfaces();
    if (dockActiveTab === 'layout' && !dockEl.classList.contains('df-hidden'))
      renderLayoutDiagnostics();
    let result = null;
    const previous = latestLayoutReport;
    try {
      result = await apiPost('/api/diagnostics/layout', {
        schemaVersion: '2.0',
        profile: layoutOptions.profile,
        rules: layoutRuleSet,
        scope: {
          rootElementId: scopedElementId,
          includeDescendants: true,
          includeNativeElements: true,
          includeBlazorElements: true,
          maxDepth: 0,
        },
        minimumSeverity: 'info',
        includeEvidence: true,
        includePasses: false,
        stability: { mode: 'wait', stableFrames: 2, quietPeriodMs: 100, timeoutMs: 2500 },
        occlusion: { mode: 'interactiveTargets', maxSamplesPerElement: 81, coverageError: 0.05, minimumOverlapRatio: 0.02 },
        privacy: { text: 'none' },
        maxElements: 2000,
      });
    } finally {
      layoutScanBusy = false;
    }
    const succeeded = result && result.ok !== false && result.report;
    const delta = succeeded ? diffLayoutReports(previous, result.report) : null;
    if (succeeded) {
      latestLayoutReport = result.report;
      layoutError = null;
      layoutStale = false;
      delete document.body.dataset.layoutStaleReason;
      elementTree.setDiagnostics(Array.isArray(result.report.findings) ? result.report.findings : []);
      if (selectedLayoutFindingId &&
          !result.report.findings?.some((finding) => finding?.id === selectedLayoutFindingId)) {
        selectedLayoutFindingId = null;
      }
      if (layoutFocusedElementId) {
        const focusedFinding = layoutFindingsForElement(layoutFocusedElementId)
          .find((finding) =>
            finding.suppressed !== true &&
            (finding.outcome === 'violation' || finding.outcome === 'observation'));
        selectedLayoutFindingId = focusedFinding?.id || null;
        if (focusedFinding) renderLayoutOverlays(focusedFinding);
      }
      if (delta) {
        setStatus(
          `Layout updated: ${delta.added.length} added, ${delta.updated.length} changed, ${delta.removed.length} removed.`);
      } else {
        const completed = formatLayoutReport(result.report);
        setStatus(`Layout check complete: ${completed.status.label}. ${completed.coverage}.`);
      }
    } else {
      layoutError = (result && (result.error || result.message))
        || 'Layout diagnostics are unavailable for this agent.';
      layoutStale = !!latestLayoutReport;
      if (!latestLayoutReport) {
        selectedLayoutFindingId = null;
        elementTree.setDiagnostics([]);
      }
    }
    updateLayoutSurfaces();
    if (dockActiveTab === 'layout' && !dockEl.classList.contains('df-hidden'))
      renderLayoutDiagnostics();
    if (layoutScanPending) {
      layoutScanPending = false;
      runLayoutScan();
    }
  }

  // ── Performance triage tab ──
  // Start/Stop are explicit. While recording, a slow poll refreshes only this panel — never the
  // frame, never a screenshot. Performance results are NOT offered as Copilot data context.
  function renderPerformance(j) {
    clearDockSnapshot();
    if (!j || j.ok === false || !j.summary) {
      performanceRecording = false;
      performanceOwned = false;
      hideDockActionStrip();
      dockEmpty((j && j.error) || 'This agent does not expose a performance triage session.', {
        kind: j && j.error ? 'error' : 'unavailable',
        title: j && j.error ? 'Could not refresh performance data' : 'Performance triage unavailable',
      });
      return;
    }

    const view = formatPerformanceSummary(j.summary);
    performanceRecording = view.active;
    performanceOwned = !!j.owned;

    const fragment = document.createDocumentFragment();
    const context = [
      layoutStatusPill(view.active ? 'Recording' : 'Stopped', view.active ? 'warning' : 'neutral'),
      dataActionText(`${view.session} · ${view.mode}`),
    ];
    if (view.active && !performanceOwned)
      context.push(layoutStatusPill('Read-only owner', 'warning'));
    setDockActionStrip({
      visible: true,
      variant: 'actions',
      ariaLabel: 'Performance actions',
      context,
      primaryCommand: dataUi.button(view.active ? 'Stop' : 'Start', {
        commandId: view.active ? 'performance.stop' : 'performance.start',
        icon: view.active ? 'i-close' : 'i-record',
        primary: !view.active,
        disabled: performanceBusy || capturedTraceMode || (view.active && !performanceOwned),
        title: view.active && !performanceOwned ? 'Another client owns this performance session.' : null,
        onClick: () => executeDataCommand(view.active ? 'performance.stop' : 'performance.start'),
      }),
    });

    if (view.perturbed)
      fragment.append(elh('div', { class: 'df-data-inline-warning', text: view.perturbationNote }));

    const table = elh('table', { class: 'df-diag-metrics' });
    for (const metric of view.metrics) {
      table.append(elh('tr', null,
        elh('td', { class: 'df-kv-key', text: metric.label }),
        elh('td', null,
          elh('div', { text: metric.value }),
          metric.detail ? elh('div', { class: 'df-diag-meta', text: metric.detail }) : null)));
    }
    fragment.append(table);

    if (view.hotspots.length) {
      fragment.append(elh('div', { class: 'df-diag-subtitle', text: 'Top hotspots (p95)' }));
      const hot = elh('table', { class: 'df-diag-hotspots' });
      hot.append(elh('tr', null,
        elh('th', { text: 'Operation' }), elh('th', { text: 'p95' }), elh('th', { text: 'max' }), elh('th', { text: 'n' }), elh('th', { text: 'errors' })));
      for (const hotspot of view.hotspots) {
        hot.append(elh('tr', null,
          elh('td', { text: hotspot.screen ? `${hotspot.name} @ ${hotspot.screen}` : hotspot.name }),
          elh('td', { text: hotspot.p95 }),
          elh('td', { text: hotspot.max }),
          elh('td', { text: String(hotspot.count) }),
          elh('td', { text: String(hotspot.errorCount) })));
      }
      fragment.append(hot);
    }

    const notes = [...view.warnings, ...view.limitations,
      'Use a native profiler for call-stack attribution.'];
    if (notes.length) {
      const limits = elh('details', { class: 'df-performance-notes' });
      limits.append(elh('summary', { text: `${notes.length} warnings and limitations` }));
      const list = elh('ul');
      for (const note of notes) list.append(elh('li', { text: note }));
      limits.append(list);
      fragment.append(limits);
    }

    dockBodyEl.replaceChildren(fragment);
  }

  async function controlPerformance(action) {
    if (performanceBusy || capturedTraceMode) {
      if (capturedTraceMode) setStatus('Captured trace mode disables effectful performance controls.');
      return;
    }
    performanceBusy = true;
    setStatus(action === 'start' ? 'Starting performance triage…' : 'Stopping performance triage…');
    let j = null;
    try {
      j = await apiPost(`/api/performance/${action}`, {});
    } finally {
      performanceBusy = false;
    }
    if (dockActiveTab === 'performance') renderPerformance(j);
    setStatus(j && j.ok === false
      ? (j.error || 'Performance triage is unavailable.')
      : (action === 'start' ? 'Recording performance triage.' : 'Performance triage stopped.'));
  }

  function performancePollIsActive() {
    return performanceRecording
      && !performanceBusy
      && dockActiveTab === 'performance'
      && !dockEl.classList.contains('df-hidden')
      && !document.body.classList.contains('df-dock-collapsed')
      && !document.hidden;
  }

  const tabLoaders = {
    problems: async (generation) => {
      const j = await apiPost('/api/problems', { limit: 200 });
      if (dockLoadIsCurrent('problems', generation)) renderProblems(j);
    },
    layout: async (generation, options = {}) => {
      if (options.forceScan === true)
        await runLayoutScan();
      else if (dockLoadIsCurrent('layout', generation))
        renderLayoutDiagnostics();
    },
    performance: async (generation) => {
      const j = await apiPost('/api/performance/snapshot', {});
      if (dockLoadIsCurrent('performance', generation)) renderPerformance(j);
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
      managedDeviceView = 'overview';
      const [appResult] = await Promise.all([
        apiPost('/api/device', {}),
        loadManagedDeviceHost(),
      ]);
      if (dockLoadIsCurrent('device', generation)) renderDevice(appResult, deviceHostState);
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
      currentAlert = null;
      currentAlertRevision = null;
      hideDockActionStrip();
      dockEmpty((result && result.error) || 'This target does not expose native alert control.', {
        kind: 'unavailable',
        title: 'Native alerts unavailable',
      });
      return;
    }
    if (!result.ok && !result.alert) {
      hideDockActionStrip();
      dockEmpty(result.error || 'Try refreshing this tab.', {
        kind: 'error',
        title: 'Could not inspect native alerts',
      });
      return;
    }
    if (!result.alert) {
      currentAlert = null;
      currentAlertRevision = null;
      hideDockActionStrip();
      dockEmpty('A visible native alert and its choices will appear here.', {
        icon: 'i-check',
        title: 'No native alert is visible',
      });
      return;
    }
    currentAlert = result.alert;
    currentAlertRevision = result.revision || null;
    recordDockSnapshot('alerts', result.alert.title || 'Native alert', result.alert, 1);

    const buttons = Array.isArray(result.alert.buttons) ? result.alert.buttons : [];
    const choices = buttons.map((alertButton) => {
      const label = String(alertButton.label || 'Dismiss');
      const choice = dataUi.button(label, {
        commandId: `alerts.choose.${label}`,
        className: 'df-alert-choice',
        disabled: !isWriter || !connected || capturedTraceMode,
        title: !isWriter || !connected || capturedTraceMode
          ? (capturedTraceMode ? 'Captured trace mode cannot dismiss native alerts.' : 'Take control before choosing a native alert action.')
          : `Choose ${label}`,
        onClick: () => executeDataCommand('alerts.choose', {
          buttonLabel: label,
          alertRevision: currentAlertRevision,
        }),
      });
      choice.dataset.alertAction = 'dismiss';
      return choice;
    });
    const persistentControls = [];
    const secondaryCommands = [];
    if (choices.length > 2) {
      const menu = elh('details', { class: 'df-alert-choice-menu' });
      menu.append(elh('summary', { class: 'df-data-button' },
        svgIcon('i-more'),
        elh('span', { text: 'Choose…' })));
      const menuBody = elh('div', { class: 'df-alert-choice-menu-body', role: 'group', 'aria-label': 'Native alert choices' });
      menuBody.append(...choices);
      menu.append(menuBody);
      persistentControls.push(menu);
    } else {
      persistentControls.push(...choices);
    }
    setDockActionStrip({
      visible: true,
      variant: 'actions',
      ariaLabel: 'Native alert actions',
      context: [
        layoutStatusPill('Alert visible', 'warning'),
        dataActionText(result.alert.title || 'Native alert'),
      ],
      persistentControls,
      secondaryCommands,
    });

    if (!buttons.length) {
      dockBodyEl.replaceChildren(dataUi.state({
        kind: 'empty',
        icon: 'i-check',
        title: 'No actionable choices',
        message: 'This native alert does not expose a button DevFlow can choose.',
      }));
    } else {
      dockBodyEl.replaceChildren(elh('p', {
        class: result.ok ? 'df-data-inline-note' : 'df-data-inline-warning',
        text: result.ok
          ? 'Choose an action above to affect the visible native alert in the live app.'
          : (result.error || 'The alert changed. Review the current choices and choose again.'),
      }));
    }
  }

  async function chooseAlert(buttonLabel, alertRevision) {
    if (!ensureCanDrive()) return;
    const state = dataDockController.current();
    const response = await apiPost('/api/alerts/dismiss', { buttonLabel, alertRevision });
    if (!dataDockController.isCurrent('alerts', state.generation, state.view)) return;
    if (response && response.ok && response.dismissed) {
      setStatus(`Chose ${buttonLabel} on the native alert.`);
      await loadTab('alerts', { refresh: true });
      return;
    }
    if (response && response.alert) {
      renderAlerts(response);
      setStatus(response.error || 'The native alert changed. Review it and choose again.');
      return;
    }
    setStatus((response && response.error) || 'Could not choose the native alert action.');
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
    if (!wvs.length) {
      hideDockActionStrip();
      dockEmpty((j && j.error) || 'WebView tools appear when the app exposes a Blazor WebView.', {
        kind: j && j.error ? 'error' : 'empty',
        title: j && j.error ? 'Could not inspect WebViews' : 'No Blazor WebViews in this app',
      });
      return;
    }
    const targetStillAvailable = cdpWebviewId && wvs.some((w) => w.id === cdpWebviewId);
    if (!targetStillAvailable) {
      cdpWebviewId = wvs[0].id;
      cdpOutputValue = null;
      cdpOutputError = null;
    }
    const target = dataActionSelect(
      'df-cdp-webview',
      'WebView target',
      cdpWebviewId,
      wvs.map((item) => [item.id, item.label]),
      (value) => {
        cdpWebviewId = value;
        cdpOutputValue = null;
        cdpOutputError = null;
        renderCdpOutput();
      });
    setDockActionStrip({
      visible: true,
      variant: 'scope',
      ariaLabel: 'WebView scope and actions',
      context: [layoutStatusPill(`${wvs.length} WebView${wvs.length === 1 ? '' : 's'}`, 'neutral')],
      persistentControls: [target],
      secondaryCommands: [
        dataUi.button('View source', {
          icon: 'i-source',
          onClick: () => executeDataCommand('webview.source.read'),
        }),
      ],
    });

    const frag = document.createDocumentFragment();
    const evalRow = elh('div', { class: 'df-data-control-row' });
    const inp = elh('input', {
      class: 'df-field df-data-expression',
      id: 'df-cdp-expr',
      'aria-label': 'JavaScript expression',
      placeholder: 'JS expression, e.g. document.title',
      value: cdpExpressionDraft,
    });
    inp.addEventListener('input', () => { cdpExpressionDraft = inp.value; });
    // Wait for key release before focusing the confirmation action so this Enter cannot approve it.
    let enterArmed = false;
    inp.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && !e.repeat) enterArmed = true;
    });
    inp.addEventListener('keyup', (e) => {
      if (e.key !== 'Enter' || !enterArmed) return;
      enterArmed = false;
      executeDataCommand('webview.javascript.run');
    });
    inp.addEventListener('blur', () => { enterArmed = false; });
    evalRow.append(inp, dataUi.button('Run', {
      icon: 'i-replay',
      primary: true,
      onClick: () => executeDataCommand('webview.javascript.run'),
    }));
    frag.append(evalRow);
    frag.append(elh('div', { id: 'df-cdp-out' }));
    dockBodyEl.replaceChildren(frag);
    renderCdpOutput();
  }

  function renderCdpOutput() {
    const out = document.getElementById('df-cdp-out');
    if (!out) return;
    if (cdpOutputError) {
      out.replaceChildren(dataUi.state({
        kind: 'error',
        icon: 'i-refresh',
        title: 'WebView command failed',
        message: cdpOutputError,
      }));
    } else if (cdpOutputValue !== null && cdpOutputValue !== undefined) {
      out.replaceChildren(typeof cdpOutputValue === 'string'
        ? elh('pre', { class: 'df-log-row', text: cdpOutputValue })
        : jsonView(cdpOutputValue));
    } else {
      out.replaceChildren();
    }
  }

  async function cdpViewSource() {
    const state = dataDockController.current();
    const targetWebViewId = cdpWebviewId;
    const out = document.getElementById('df-cdp-out');
    if (out) out.textContent = 'Loading…';
    const j = await apiPost('/api/cdp/source', { webviewId: targetWebViewId });
    if (!dataDockController.isCurrent('webview', state.generation, state.view) ||
        cdpWebviewId !== targetWebViewId) return;
    cdpOutputValue = j && j.ok && j.source != null ? String(j.source) : null;
    cdpOutputError = cdpOutputValue === null ? ((j && j.error) || 'No source.') : null;
    renderCdpOutput();
  }

  async function cdpEval() {
    const state = dataDockController.current();
    const inp = document.getElementById('df-cdp-expr');
    const out = document.getElementById('df-cdp-out');
    const expr = inp ? inp.value : cdpExpressionDraft;
    if (!expr) return;
    if (!ensureCanDrive()) return;
    const targetWebViewId = cdpWebviewId;
    const confirmed = await confirmModal(
      'Run this JavaScript in the selected LIVE WebView? It can read or change application data.',
      'Run JavaScript');
    if (!confirmed) return;
    if (!dataDockController.isCurrent('webview', state.generation, state.view)) return;
    if (cdpWebviewId !== targetWebViewId) {
      cdpWebviewId = targetWebViewId;
      const target = document.getElementById('df-cdp-webview');
      if (target) target.value = targetWebViewId;
    }
    if (out) out.textContent = 'Running…';
    const j = await apiPost('/api/cdp/eval', { expression: expr, webviewId: targetWebViewId });
    if (!dataDockController.isCurrent('webview', state.generation, state.view)) return;
    cdpOutputValue = j && j.ok ? j.result : null;
    cdpOutputError = j && j.ok ? null : ((j && j.error) || 'Evaluate failed.');
    renderCdpOutput();
  }

  async function executeDataCommand(command, payload = {}) {
    switch (command) {
      case 'layout.scan':
        return runLayoutScan();
      case 'performance.refresh':
        return loadTab('performance', { refresh: true, view: 'default' });
      case 'performance.start':
        return controlPerformance('start');
      case 'performance.stop':
        return controlPerformance('stop');
      case 'sensors.refresh':
        sensorsView = 'list';
        return loadTab('sensors', { refresh: true, view: 'list' });
      case 'sensors.location.read':
        return readGeolocation();
      case 'files.refresh':
        return loadFiles({ refresh: true });
      case 'files.root.change':
        filesRoot = payload.root;
        filesPath = '';
        updateFilesActionStrip();
        return loadFiles();
      case 'webview.refresh':
        return loadTab('webview', { refresh: true, view: 'default' });
      case 'webview.source.read':
        return cdpViewSource();
      case 'webview.javascript.run':
        return cdpEval();
      case 'alerts.refresh':
        return loadTab('alerts', { refresh: true, view: 'default' });
      case 'alerts.choose':
        return chooseAlert(payload.buttonLabel, payload.alertRevision);
      case 'network.list.refresh':
        return loadTab('network', { refresh: true, view: 'list' });
      case 'network.detail.open':
        return loadNetworkDetail(payload.id);
      case 'network.detail.refresh':
        return loadNetworkDetail(networkDetailId, { refresh: true });
      case 'network.detail.back':
        return restoreNetworkList();
      default:
        throw new Error(`Unknown Data command '${command}'.`);
    }
  }

  function refreshActiveDataView() {
    if (dockActiveTab === 'layout') return executeDataCommand('layout.scan');
    if (dockActiveTab === 'performance') return executeDataCommand('performance.refresh');
    if (dockActiveTab === 'sensors')
      return executeDataCommand(sensorsView === 'location' ? 'sensors.location.read' : 'sensors.refresh');
    if (dockActiveTab === 'files') return executeDataCommand('files.refresh');
    if (dockActiveTab === 'webview') return executeDataCommand('webview.refresh');
    if (dockActiveTab === 'alerts') return executeDataCommand('alerts.refresh');
    if (dockActiveTab === 'network')
      return executeDataCommand(networkDetailId ? 'network.detail.refresh' : 'network.list.refresh');
    return loadTab(dockActiveTab, { refresh: true, view: dockActiveView });
  }

  async function loadTab(name, options = {}) {
    // The dock only has tabs this broker can fill. Refusing an unserved tab here means no caller —
    // a startup hint, a keyboard shortcut, a stale saved tab — can blank the dock onto nothing.
    if (name === 'layout' && !inspectorSurfaces.layoutDiagnostics) return;
    const generation = ++dockViewGeneration;
    const view = options.view || (
      name === 'layout' ? layoutSubview :
      name === 'sensors' ? sensorsView :
      name === 'network' ? 'list' :
      'default');
    const refreshing = options.refresh === true && dockBodyEl.childElementCount > 0;
    const retained = refreshing ? {
      nodes: [...dockBodyEl.childNodes],
      scrollTop: dockBodyEl.scrollTop,
      snapshot: dockSnapshot,
      stripModel: dataDockController.current().stripModel,
      meta: dockMetaEl?.textContent || '',
      note: dockMetaNoteEl?.textContent || '',
    } : null;
    dockActiveTab = name;
    setDockView(view);
    if (name !== 'layout') {
      clearLayoutOverlays();
    }
    networkDetailId = null;
    if (!refreshing) clearDockSnapshot();
    for (const b of dockTabsEl.querySelectorAll('.df-dock-tab')) {
      const active = b.getAttribute('data-tab') === name;
      b.classList.toggle('df-active', active);
      b.setAttribute('aria-selected', String(active));
      b.tabIndex = active ? 0 : -1;
      if (active && b.id) {
        requestAnimationFrame(() => b.scrollIntoView({ block: 'nearest', inline: 'nearest' }));
      }
    }
    updateLayoutStatus();
    dataDockController.begin(name, {
      view,
      generation,
      label: name.charAt(0).toUpperCase() + name.slice(1),
      refresh: options.refresh === true,
    });
    setDockMeta('loading…');
    setDockMetaNote('');
    try {
      await tabLoaders[name](generation, options);
      if (!dockLoadIsCurrent(name, generation)) return;
      if (retained && dockBodyEl.querySelector('.df-data-state-error')) {
        dockBodyEl.replaceChildren(...retained.nodes);
        dockBodyEl.scrollTop = retained.scrollTop;
        dockSnapshot = retained.snapshot;
        updateDataAttachButton();
        updateHostButtons();
        setDockActionStrip(retained.stripModel, generation, view);
        setDockMetaNote(retained.note);
        dataDockController.fail(name, 'The latest refresh failed.', {
          generation,
          retain: true,
          view,
        });
        return;
      }
      dataDockController.complete(
        name,
        name === 'layout' && !latestLayoutReport
          ? ''
          : (name === 'network' ? 'live · updated ' : name === 'problems' ? 'auto-captured · updated ' : name === 'layout' ? 'last checked ' : 'captured ') +
            new Date().toLocaleTimeString(),
        dockMetaNoteEl?.textContent || '',
        { generation, view });
    }
    catch (e) {
      if (!dockLoadIsCurrent(name, generation)) return;
      if (retained) {
        dockBodyEl.replaceChildren(...retained.nodes);
        dockBodyEl.scrollTop = retained.scrollTop;
        dockSnapshot = retained.snapshot;
        updateDataAttachButton();
        updateHostButtons();
        setDockActionStrip(retained.stripModel, generation, view);
        setDockMeta(retained.meta);
        setDockMetaNote(retained.note);
      }
      dataDockController.fail(name, 'Try refreshing this tab.', {
        generation,
        retain: options.refresh === true,
        view,
      });
    }
  }

  function openDock(targetTab = null, options = {}) {
    const active = document.activeElement;
    if (active instanceof HTMLElement && active !== document.body && !dockEl.contains(active))
      dockReturnFocus = active;
    if (isTransientPaneLayout()) {
      testWorkbench?.close();
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
      setToolbarToggleState(toggleDockBtn, true);
    }
    if (!dockLoaded) {
      dockLoaded = true;
      loadTab(targetTab || dockActiveTab, options);
    } else if (targetTab) {
      loadTab(targetTab, options);
    }
    updateLayoutStatus();
    syncPaneChrome();
  }
  function closeDock(restore = false, preserveLayoutOverlay = false) {
    if (layoutLiveTimer) {
      clearTimeout(layoutLiveTimer);
      layoutLiveTimer = null;
    }
    if (!preserveLayoutOverlay) clearLayoutOverlays();
    dockEl.classList.add('df-hidden');
    document.body.classList.remove('df-dock-open', 'df-dock-collapsed');
    if (toggleDockBtn) {
      toggleDockBtn.classList.remove('df-active');
      setToolbarToggleState(toggleDockBtn, false);
    }
    updateLayoutStatus();
    syncPaneChrome();
    if (restore) restoreFocus(dockReturnFocus, tb.more || toggleDockBtn);
  }
  function toggleDockCollapsed() {
    const collapsed = document.body.classList.toggle('df-dock-collapsed');
    if (dockCollapseBtn) {
      dockCollapseBtn.setAttribute('aria-expanded', String(!collapsed));
      dockCollapseBtn.title = collapsed ? 'Expand data panel' : 'Collapse data panel';
    }
  }

  function openLayoutDiagnostics(options = {}) {
    // Nothing may strand the Data dock on a tab this broker cannot fill.
    if (!inspectorSurfaces.layoutDiagnostics) return;
    const elementId = options.elementId || null;
    const forceScan = options.forceScan === true;
    layoutFocusedElementId = elementId;
    selectedLayoutFindingId = null;
    layoutSubview = 'findings';
    clearLayoutOverlays();

    if (latestLayoutReport && elementId) {
      const finding = layoutFindingsForElement(elementId)
        .find((candidate) =>
          candidate.suppressed !== true &&
          (candidate.outcome === 'violation' || candidate.outcome === 'observation'));
      if (finding) {
        selectedLayoutFindingId = finding.id;
        renderLayoutOverlays(finding);
      }
    }

    openDock('layout', { forceScan });
  }
  function parseAuthoringFlow(markdown) {
    if (typeof markdown !== 'string') return null;
    const match = /```json maui-test\s*\r?\n([\s\S]*?)\r?\n```/.exec(markdown);
    if (!match) return null;
    try { return JSON.parse(match[1]); } catch { return null; }
  }

  function normalizeLoadedWorkflowMarkdown(markdown) {
    if (typeof markdown !== 'string' || !markdown.trim()) {
      return { ok: false, error: 'Empty workflow document.' };
    }
    const match = /```json maui-test\s*\r?\n([\s\S]*?)\r?\n```/.exec(markdown);
    if (!match) {
      return { ok: false, error: 'No ```json maui-test``` block found in the test file.' };
    }

    let flow = null;
    try {
      flow = JSON.parse(match[1]);
    } catch {
      return { ok: false, error: 'Invalid JSON in the maui-test block.' };
    }
    if (!flow || typeof flow !== 'object') return { ok: false, error: 'The maui-test block must contain a JSON object.' };

    let migrated = false;
    let normalizedFlow = flow;
    if (!Number.isInteger(flow.schema)) {
      if (Number.isInteger(flow.schemaVersion)) {
        normalizedFlow = { ...flow, schema: flow.schemaVersion };
        delete normalizedFlow.schemaVersion;
        migrated = true;
      } else if (Object.prototype.hasOwnProperty.call(flow, 'schemaVersion')) {
        return {
          ok: false,
          error: 'Legacy maui-test blocks using schemaVersion must be migrated to an integer schema field before loading. Replace schemaVersion with schema or re-save the flow from a current DevFlow host.',
        };
      } else {
        return { ok: false, error: 'The maui-test block requires an integer schema.' };
      }
    }

    if (!Array.isArray(normalizedFlow.steps)) {
      return { ok: false, error: 'The maui-test block requires a steps[] array.' };
    }

    return {
      ok: true,
      flow: normalizedFlow,
      markdown: migrated ? replaceAuthoringFlow(markdown, normalizedFlow) : markdown,
      migrated,
    };
  }

  function replaceAuthoringFlow(markdown, flow) {
    const payload = JSON.stringify(flow, null, 2);
    const expression = /(```json maui-test\s*\r?\n)[\s\S]*?(\r?\n```)/;
    if (typeof markdown === 'string' && expression.test(markdown))
      return markdown.replace(expression, `$1${payload}$2`);
    return `# Scenario: ${flow?.name || 'scenario'}\n\n\`\`\`json maui-test\n${payload}\n\`\`\`\n`;
  }

  function activeAuthoringStepSelector(step) {
    const selector = step?.args?.selector;
    return selector && typeof selector === 'object' && Object.keys(selector).length
      ? selector
      : step?.target && typeof step.target === 'object'
        ? step.target
        : null;
  }

  function selectorKindForAmbiguity(selector) {
    if (!selector || typeof selector !== 'object') return 'unknown';
    if (typeof selector.automationId === 'string' && selector.automationId.trim()) return 'automationId';
    if (typeof selector.text === 'string' && selector.text) return 'text';
    if (selector.typeIndex || selector.selectorKind === 'typeIndex') return 'typeIndex';
    if (typeof selector.id === 'string' && selector.id) return 'runtimeId';
    return 'unknown';
  }

  function sequenceFromStepId(stepId) {
    const value = String(stepId || '').trim();
    const match = /^(?:step-)?(\d+)$/.exec(value);
    return match ? Number(match[1]) : null;
  }

  function authoringStepIndexForFailure(flow, stepId, stepSequence) {
    const steps = Array.isArray(flow?.steps) ? flow.steps : [];
    const sequence = Number.isInteger(Number(stepSequence)) && Number(stepSequence) > 0
      ? Number(stepSequence)
      : sequenceFromStepId(stepId);
    if (!sequence) return -1;
    const matches = steps
      .map((step, index) => ({ step, index }))
      .filter((entry) => Number(entry.step?.seq) === sequence);
    return matches.length === 1 ? matches[0].index : -1;
  }

  function safeAuthoringName(value) {
    const name = String(value || '').trim();
    if (/^[^\\/]{1,255}\.md$/i.test(name)) return name;
    const fallback = (lastMarkdownName || 'scenario.md').replace(/[\\/]/g, '_');
    return /\.md$/i.test(fallback) ? fallback : `${fallback}.md`;
  }

  function createAuthoringPlan() {
    const flowName = safeAuthoringName(authoringDraft.flowName);
    return {
      schema: 1,
      planId: `plan_${Math.random().toString(36).slice(2, 12)}`,
      revision: 1,
      flow: { path: flowName, digest: authoringDraft.flowDigest || '' },
      title: flowName.replace(/\.md$/i, ''),
      goal: '',
      scenarios: [{ scenarioId: 'scenario-1', description: '', acceptanceCriterionIds: [], risks: [] }],
      assumptions: [],
      risks: [],
      preconditions: [],
      reset: { required: true, strategy: '', resetIdentity: '', seedFingerprint: '', backendStateFingerprint: '' },
      acceptanceCriteria: [],
      requirements: { requiredCapabilities: [], requiredSemantics: [] },
      requiredPlatforms: [],
      explorationBudget: { maxActions: 0, maxDurationSeconds: 0, allowedScopes: [] },
      prohibitedActionClasses: [],
      provenance: {
        actorKind: 'human',
        channel: 'inspector',
        provider: '',
        intent: 'human-authored',
        recordedAt: new Date().toISOString(),
      },
      reviews: [],
      approvals: [],
      sideEffectPolicy: 'none',
      businessOracles: [],
      independentBusinessOracles: [],
      checkpoint: {},
    };
  }

  function ensureAuthoringPlan() {
    if (!authoringDraft.plan) {
      authoringDraft.plan = createAuthoringPlan();
      authoringDraft.planJson = JSON.stringify(authoringDraft.plan, null, 2);
      authoringDraft.planDirty = true;
    }
    return authoringDraft.plan;
  }

  function updateAuthoringPlanFlowReference(name, digest = '') {
    if (!authoringDraft.plan) return;
    authoringDraft.plan.flow = {
      ...(authoringDraft.plan.flow || {}),
      path: safeAuthoringName(name),
      digest,
    };
    authoringDraft.planJson = JSON.stringify(authoringDraft.plan, null, 2);
    authoringDraft.planDirty = true;
  }

  function hasAuthoringGoal() {
    return !!String(authoringDraft.plan?.goal || '').trim();
  }

  function authoringReadiness() {
    const flow = authoringDraft.flow || parseAuthoringFlow(authoringDraft.markdown);
    const steps = Array.isArray(flow?.steps) ? flow.steps : [];
    const hardOutcomeCheck = steps.some((step) => Array.isArray(step?.asserts) &&
      step.asserts.some((assertion) => assertion?.verify !== false && assertion?.kind !== 'pageChanged'));
    return {
      goal: hasAuthoringGoal(),
      recordedSteps: steps.length > 0,
      hardOutcomeCheck,
      savedBundle: !!authoringDraft.committedFlowDigest &&
        !!authoringDraft.committedPlanDigest &&
        !authoringDraft.flowDirty &&
        !authoringDraft.planDirty &&
        !authoringDraft.bindingStale,
    };
  }

  function openGoalForRecovery(message) {
    ensureAuthoringPlan();
    updateAuthoringPlanFlowReference(authoringDraft.flowName || lastMarkdownName || 'scenario.md');
    authoringDraft.guidanceMessage = message;
    authoringDraft.errors = [];
    notifyAuthoring();
    testWorkbench?.focusGoal?.();
    workbenchAnnouncement(message, true);
  }

  function cloneAuthoring(value) {
    return value == null ? value : JSON.parse(JSON.stringify(value));
  }

  function notifyAuthoring(rerender = true) {
    if (!testWorkbench) return;
    const authoringState = recordingId
      ? 'recording'
      : authoringDraft.saving
        ? 'validating'
        : authoringDraft.stale
          ? 'stale'
          : authoringDraft.planDirty || authoringDraft.flowDirty
            ? 'draft'
            : authoringDraft.plan || authoringDraft.flow
              ? 'saved'
              : 'none';
    testWorkbench.updateState(
      {
        authoring: authoringState,
        draft: {
          dirty: !!(authoringDraft.planDirty || authoringDraft.flowDirty),
          saving: !!authoringDraft.saving,
          stale: !!authoringDraft.stale,
          readiness: authoringReadiness(),
        },
      },
      { preservePanel: !rerender }
    );
  }

  function applyAuthoringResponse(response, committed) {
    if (!response) {
      authoringDraft.errors = ['The local authoring service did not respond.'];
      return;
    }
    const preserveLocalDraft = response.stale === true && response.ok !== true;
    authoringDraft.workspaceAvailable = response.supported === false ? false : true;
    authoringDraft.stale = response.stale === true;
    authoringDraft.errors = Array.isArray(response.errors)
      ? response.errors.map(String)
      : response.error ? [String(response.error)] : [];
    authoringDraft.warnings = Array.isArray(response.warnings) ? response.warnings.map(String) : [];
    authoringDraft.issues = Array.isArray(response.issues) ? response.issues : [];
    authoringDraft.attentionStepSequence =
      authoringDraft.issues.find((issue) => issue?.blocking === true && Number.isInteger(issue?.stepSequence))
        ?.stepSequence ?? null;
    const bindingStale = authoringDraft.warnings.some((warning) =>
      /older flow digest|flow\.digest must match|plan.*flow.*digest/i.test(warning));
    authoringDraft.bindingStale = bindingStale;
    if (bindingStale) {
      authoringDraft.guidanceMessage =
        'The recorded steps changed after this plan was saved. Review and save the test to update their binding.';
    }
    authoringDraft.diff = typeof response.diff === 'string' ? response.diff : null;
    if (response.flow && !preserveLocalDraft) {
      const canonicalMarkdown = response.flow.document &&
          typeof response.flow.markdown === 'string' &&
          response.flow.markdown.includes('"schemaVersion"')
        ? replaceAuthoringFlow(response.flow.markdown, response.flow.document) || response.flow.markdown
        : response.flow.markdown || authoringDraft.markdown;
      authoringDraft.flowName = response.flow.name || authoringDraft.flowName;
      authoringDraft.markdown = canonicalMarkdown;
      authoringDraft.flow = response.flow.document || parseAuthoringFlow(authoringDraft.markdown);
      authoringDraft.flowDigest = response.flow.digest || authoringDraft.flowDigest;
    }
    if (response.plan && !preserveLocalDraft) {
      authoringDraft.planJson = response.plan.json || null;
      authoringDraft.plan = response.plan.document || (response.plan.json ? JSON.parse(response.plan.json) : null);
      authoringDraft.planDigest = response.plan.digest || null;
      authoringDraft.planRevision = response.plan.revision || authoringDraft.plan?.revision || null;
    }
    if (committed && response.ok === true) {
      authoringDraft.committedFlowDigest = authoringDraft.flowDigest;
      authoringDraft.committedPlanDigest = authoringDraft.planDigest;
      authoringDraft.committedPlanRevision = authoringDraft.planRevision;
      authoringDraft.committedPlan = cloneAuthoring(authoringDraft.plan);
      authoringDraft.flowDirty = false;
      authoringDraft.planDirty = bindingStale;
      authoringDraft.stale = response.stale === true;
    }
  }

  function syncAuthoringFlow(markdown, name, source, loadGeneration = authoringLoadGeneration) {
    const flow = parseAuthoringFlow(markdown);
    const retainedPlan = source === 'recording' ? cloneAuthoring(authoringDraft.plan) : null;
    const retainedCommittedFlowDigest = source === 'recording' ? authoringDraft.committedFlowDigest : null;
    authoringDraft.flowName = safeAuthoringName(name);
    authoringDraft.markdown = markdown;
    authoringDraft.flow = flow;
    authoringDraft.flowDigest = null;
    authoringDraft.committedFlowDigest = null;
    authoringDraft.flowDirty = source === 'recording';
    authoringDraft.bindingStale = false;
    authoringDraft.diff = null;
    authoringDraft.errors = [];
    authoringDraft.warnings = [];
    authoringDraft.issues = [];
    authoringDraft.checkPassed = false;
    authoringDraft.diffReviewed = false;
    authoringDraft.attentionStepSequence = null;
    if (source !== 'recording') {
      authoringDraft.rawDraft = false;
      authoringDraft.recordingDraft = false;
      authoringDraft.recordingDraftStepCount = 0;
    }
    if (retainedPlan) {
      authoringDraft.plan = retainedPlan;
      authoringDraft.plan.flow = {
        ...(authoringDraft.plan.flow || {}),
        path: authoringDraft.flowName,
        digest: '',
      };
      authoringDraft.planJson = JSON.stringify(authoringDraft.plan, null, 2);
      authoringDraft.planDigest = null;
      authoringDraft.planDirty = true;
      authoringDraft.committedFlowDigest = retainedCommittedFlowDigest;
    } else if (source !== 'project') {
      authoringDraft.plan = null;
      authoringDraft.planJson = null;
      authoringDraft.planDigest = null;
      authoringDraft.planRevision = null;
      authoringDraft.committedPlan = null;
      authoringDraft.committedPlanDigest = null;
      authoringDraft.committedPlanRevision = null;
      authoringDraft.planDirty = false;
    }
    if (source === 'project') {
      const requestedName = authoringDraft.flowName;
      postAuthoring('/api/plans/load', { name: requestedName }).then((response) => {
        if (loadGeneration === authoringLoadGeneration &&
            authoringDraft.flowName === requestedName &&
            response && response.ok === true) {
          applyAuthoringResponse(response, true);
          notifyAuthoring();
        }
      });
    }
    notifyAuthoring();
  }

  async function postAuthoring(path, body) {
    const response = await inspectorApi.postDetailed(path, body);
    const payload = response.body && typeof response.body === 'object'
      ? response.body
      : { ok: false, error: response.error || `Request failed (${response.status || 0}).` };
    if (!response.ok) payload.ok = false;
    return payload;
  }

  function planSidecarName(name) {
    return String(name || 'scenario.md').replace(/\.md$/i, '') + '.maui-plan.json';
  }

  function canonicalAuthoringJson(value) {
    if (Array.isArray(value)) return value.map(canonicalAuthoringJson);
    if (value && typeof value === 'object') {
      return Object.fromEntries(Object.keys(value).sort().map((key) => [key, canonicalAuthoringJson(value[key])]));
    }
    return value;
  }

  async function authoringSha256(value) {
    if (!window.crypto?.subtle || typeof TextEncoder === 'undefined') return null;
    const bytes = new TextEncoder().encode(JSON.stringify(canonicalAuthoringJson(value)));
    const digest = await window.crypto.subtle.digest('SHA-256', bytes);
    return [...new Uint8Array(digest)].map((value) => value.toString(16).padStart(2, '0')).join('');
  }

  async function hostBundle(name) {
    const flow = authoringDraft.flow || parseAuthoringFlow(authoringDraft.markdown);
    const flowDigest = await authoringSha256(flow);
    if (!flowDigest || !authoringDraft.plan) return null;
    const plan = cloneAuthoring(authoringDraft.plan);
    plan.flow = Object.assign({}, plan.flow || {}, { path: name, digest: flowDigest });
    const planDigest = await authoringSha256(plan);
    authoringDraft.flowDigest = flowDigest;
    authoringDraft.plan = plan;
    authoringDraft.planJson = JSON.stringify(plan, null, 2);
    authoringDraft.planDigest = planDigest;
    return {
      name,
      markdown: authoringDraft.markdown,
      planJson: authoringDraft.planJson,
      flowDigest,
      planDigest,
    };
  }

  function recordStudyEvent(kind, details) {
    return prototypeStudyJournal.record(kind, details);
  }

  function studyProvenance(value) {
    const actor = value ?? authoringDraft.plan?.provenance?.actorKind;
    return actor === 'agent' || actor === 'mixed' ? actor : 'human';
  }

  function studySelectorQuality(selector) {
    if (!selector || typeof selector !== 'object') return 'unknown';
    if (selector.quality === 'durable' || selector.quality === 'fragile') return selector.quality;
    if (typeof selector.automationId === 'string' && selector.automationId.trim()) return 'durable';
    return selector.id || selector.text || selector.type || selector.css ? 'fragile' : 'unknown';
  }

  function studyFlowFacts(flow) {
    const steps = Array.isArray(flow?.steps) ? flow.steps : [];
    let hardAssertionCount = 0;
    let durableSelectorCount = 0;
    let fragileSelectorCount = 0;
    const countSelector = (selector) => {
      const quality = studySelectorQuality(selector);
      if (quality === 'durable') durableSelectorCount += 1;
      if (quality === 'fragile') fragileSelectorCount += 1;
    };
    for (const step of steps) {
      countSelector(step?.args?.selector || step?.selector);
      for (const assertion of Array.isArray(step?.asserts) ? step.asserts : []) {
        if (assertion?.verify !== false && assertion?.kind !== 'pageChanged') hardAssertionCount += 1;
        countSelector(assertion?.selector);
      }
    }
    return {
      stepCount: steps.length,
      hardAssertionCount,
      durableSelectorCount,
      fragileSelectorCount,
    };
  }

  function studyRunDuration(snapshot) {
    const startedAt = Date.parse(snapshot?.startedAt || snapshot?.createdAt || '');
    const endedAt = Date.parse(snapshot?.endedAt || '');
    if (!Number.isFinite(startedAt) || !Number.isFinite(endedAt) || endedAt < startedAt) return null;
    return endedAt - startedAt;
  }

  function studyFailureClass(snapshot) {
    return snapshot?.report?.failure?.class ||
      snapshot?.report?.failure?.code ||
      snapshot?.report?.failureClass ||
      snapshot?.failureClass ||
      null;
  }

  function createPrototypeStudyController() {
    return Object.freeze({
      summary: () => prototypeStudyJournal.summary(),
      workbenchOpened() {
        return recordStudyEvent('workbench-opened', { provenance: 'human' });
      },
      goalDefined(value) {
        if (!String(value || '').trim()) return false;
        return recordStudyEvent('goal-defined', { provenance: 'human' });
      },
      assertionAdded(assertion) {
        return recordStudyEvent('assertion-added', {
          hard: assertion?.verify !== false && assertion?.kind !== 'pageChanged',
          selectorQuality: studySelectorQuality(assertion?.selector),
          provenance: 'human',
        });
      },
      testSaved() {
        const flow = authoringDraft.flow || parseAuthoringFlow(authoringDraft.markdown);
        return recordStudyEvent('test-saved', {
          ...studyFlowFacts(flow),
          provenance: studyProvenance(),
        });
      },
      runStarted(snapshot) {
        const flow = authoringDraft.flow || parseAuthoringFlow(authoringDraft.markdown);
        return recordStudyEvent('run-started', {
          runId: snapshot?.runId,
          stepCount: snapshot?.totalSteps ?? studyFlowFacts(flow).stepCount,
          provenance: 'human',
        });
      },
      runTerminal(snapshot) {
        return recordStudyEvent('run-terminal', {
          runId: snapshot?.runId,
          state: snapshot?.state,
          durationMs: studyRunDuration(snapshot),
          stepCount: snapshot?.totalSteps,
          failureClass: studyFailureClass(snapshot),
        });
      },
      resultsOpened(snapshot) {
        if (!snapshot?.terminal || !snapshot?.runId) return false;
        return recordStudyEvent('results-opened', {
          runId: snapshot.runId,
          state: snapshot.state,
        });
      },
      improveScanned(findingCount) {
        return recordStudyEvent('improve-scanned', { findingCount });
      },
      agentApprovalTransition(kind, data) {
        const provenance = kind === 'agent-requested' || kind === 'agent-consumed'
          ? 'agent'
          : kind === 'agent-approved' || kind === 'agent-rejected'
            ? 'human'
            : 'mixed';
        return recordStudyEvent(kind, {
          approvalRequestId: data?.approvalRequestId,
          durationMs: data?.durationMs,
          provenance,
        });
      },
      repairTransition(state, proposalId) {
        const event = {
          proposed: 'repair-proposed',
          approved: 'repair-approved',
          rejected: 'repair-rejected',
          applied: 'repair-applied',
          verified: 'repair-verified',
          reverted: 'repair-reverted',
          'rollback-required': 'repair-rollback-required',
          'rollback-failed': 'repair-rollback-failed',
        }[state];
        return event && proposalId
          ? recordStudyEvent(event, { proposalId, provenance: state === 'proposed' ? studyProvenance() : 'human' })
          : false;
      },
      downloadSessionEvidence() {
        try {
          const downloaded = downloadText(
            'devflow-prototype-study-evidence.json',
            JSON.stringify(prototypeStudyJournal.exportEvidence(), null, 2),
            'application/json',
          );
          if (!downloaded) return false;
          setStatus('Downloaded file-only local prototype-study evidence.');
          return true;
        } catch {
          setStatus('Could not create local prototype-study evidence download.');
          return false;
        }
      },
      clearLocalSessionEvidence() {
        const cleared = prototypeStudyJournal.clear();
        setStatus(cleared
          ? 'Local prototype-study evidence cleared.'
          : 'Local prototype-study evidence could not be cleared.');
        if (cleared) testWorkbench?.updateState({});
        return cleared;
      },
    });
  }

  function createAuthoringController() {
    const controller = {
      state: () => ({
        ...authoringDraft,
        readiness: authoringReadiness(),
        canDrive: isWriter && connected && !capturedTraceMode && !replaying,
        recording: !!recordingId,
        appendingRecording: !!recordingId && !!appendRecordingBase,
        recordingId,
        recordingSteps: recStepCount,
        recordingStopping,
      }),
      update(patch, rerender = true) {
        Object.assign(authoringDraft, patch || {});
        if (authoringDraft.plan && !authoringDraft.planJson)
          authoringDraft.planJson = JSON.stringify(authoringDraft.plan, null, 2);
        notifyAuthoring(rerender);
      },
      noteGoalDefined(value) {
        return studyController?.goalDefined(value) || false;
      },
      noteAssertionAdded(assertion) {
        return studyController?.assertionAdded(assertion) || false;
      },
      message: setStatus,
      selectedElement: () => {
        const selected = selectedElement();
        return selected ? elementInfo(selected) : null;
      },
      hasSelectedSource: () => {
        const selected = selectedElement();
        return !!(selected && selected.getAttribute('data-hasSource') === 'true');
      },
      openSelectedSource: () => openSource(),
      async openSavedTest() {
        authoringDraft.savedTestPickerOpen = true;
        authoringDraft.savedTestsLoading = true;
        authoringDraft.savedTestsError = null;
        notifyAuthoring();
        const result = await apiPost('/api/flows/files/list', {});
        authoringDraft.savedTestsLoading = false;
        authoringDraft.workspaceAvailable = result?.supported !== false;
        if (!result || result.ok !== true) {
          authoringDraft.savedTests = [];
          authoringDraft.savedTestsError = result?.error || 'Could not list saved project tests.';
        } else {
          authoringDraft.savedTests = (result.tests || [])
            .filter((test) => test && typeof test.name === 'string')
            .map((test) => ({ name: test.name, modifiedAt: test.modifiedAt || null }));
          authoringDraft.savedTestsError = null;
        }
        notifyAuthoring();
      },
      closeSavedTestPicker() {
        authoringDraft.savedTestPickerOpen = false;
        authoringDraft.savedTestsError = null;
        notifyAuthoring();
      },
      async loadSavedTest(name) {
        if (!name) return;
        authoringDraft.savedTestsLoading = true;
        notifyAuthoring();
        try {
          await loadProjectWorkflow(name);
        } finally {
          authoringDraft.savedTestsLoading = false;
          notifyAuthoring();
        }
      },
      chooseSavedTestFile: () => chooseWorkflowFile(),
      async reloadSavedTest() {
        const name = safeAuthoringName(authoringDraft.flowName || lastMarkdownName || 'scenario.md');
        if (lastMarkdownSource === 'project' || authoringDraft.workspaceAvailable !== false) {
          await loadProjectWorkflow(name);
        } else {
          await controller.loadPlan();
        }
        authoringDraft.stale = false;
        authoringDraft.guidanceMessage = 'Reloaded the saved test. Review any local changes before continuing.';
        notifyAuthoring();
      },
      async newPlan() {
        const plan = createAuthoringPlan();
        authoringDraft.flowName = safeAuthoringName(authoringDraft.flowName);
        authoringDraft.plan = plan;
        authoringDraft.planJson = JSON.stringify(plan, null, 2);
        authoringDraft.planDirty = true;
        authoringDraft.stale = false;
        authoringDraft.bindingStale = false;
        authoringDraft.errors = [];
        authoringDraft.warnings = [];
        notifyAuthoring();
      },
      async loadPlan() {
        const name = safeAuthoringName(authoringDraft.flowName);
        authoringDraft.saving = true;
        notifyAuthoring();
        try {
          const response = await postAuthoring('/api/plans/load', { name });
          applyAuthoringResponse(response, true);
          if (response.supported === false) {
            const hostResult = await hostBridge.request('loadTestBundle', {});
            const bundle = hostResult?.value;
            if (hostResult?.ok && bundle?.markdown) {
              syncAuthoringFlow(bundle.markdown, bundle.name || name, 'file');
              if (bundle.planJson) {
                try {
                  authoringDraft.plan = JSON.parse(bundle.planJson);
                  authoringDraft.planJson = bundle.planJson;
                  authoringDraft.planRevision = authoringDraft.plan.revision || null;
                  authoringDraft.committedPlan = cloneAuthoring(authoringDraft.plan);
                  authoringDraft.committedPlanRevision = authoringDraft.planRevision;
                  authoringDraft.planDirty = false;
                } catch {
                  authoringDraft.errors = ['The host returned an invalid plan sidecar.'];
                }
              }
              setStatus(`Loaded ${bundle.name || name} through the host bridge.`);
            } else {
              setStatus(hostResult?.error || 'Workspace persistence is unavailable. Create a plan and download it, or use a host bridge.');
            }
          }
          else if (response.ok)
            setStatus(response.plan?.document ? `Loaded ${name} and its plan sidecar.` : `Loaded ${name}; no plan sidecar exists yet.`);
          else
            setStatus(response.error || 'Could not load the plan sidecar.');
        } finally {
          authoringDraft.saving = false;
          notifyAuthoring();
        }
      },
      async savePlan(confirmOverwrite) {
        if (!authoringDraft.plan) {
          setStatus('Create or load a plan before saving.');
          return;
        }
        const name = safeAuthoringName(authoringDraft.flowName);
        authoringDraft.saving = true;
        notifyAuthoring();
        try {
          const response = await postAuthoring('/api/plans/save', {
            name,
            planJson: authoringDraft.planJson || JSON.stringify(authoringDraft.plan, null, 2),
            expectedPlanRevision: authoringDraft.committedPlanRevision,
            expectedPlanDigest: authoringDraft.committedPlanDigest,
            expectedFlowDigest: authoringDraft.committedFlowDigest,
            confirmOverwrite: confirmOverwrite === true,
          });
          applyAuthoringResponse(response, response.ok === true && response.supported !== false);
          if (response.supported === false || response.code === 'flow-not-found') {
            controller.downloadPlan();
            setStatus('A canonical workspace flow is unavailable, so the plan sidecar was downloaded instead.');
          } else if (response.ok) {
            setStatus(`Saved ${planSidecarName(name)}. No app action was started.`);
          } else {
            setStatus(response.error || 'Could not save the plan sidecar.');
          }
        } finally {
          authoringDraft.saving = false;
          notifyAuthoring();
        }
      },
      discardPlan() {
        authoringDraft.plan = cloneAuthoring(authoringDraft.committedPlan);
        authoringDraft.planJson = authoringDraft.plan ? JSON.stringify(authoringDraft.plan, null, 2) : null;
        authoringDraft.planDirty = false;
        authoringDraft.stale = false;
        authoringDraft.errors = [];
        authoringDraft.warnings = [];
        notifyAuthoring();
        setStatus('Plan draft discarded.');
      },
      downloadPlan() {
        if (!authoringDraft.plan) return;
        const name = planSidecarName(safeAuthoringName(authoringDraft.flowName));
        downloadText(name, authoringDraft.planJson || JSON.stringify(authoringDraft.plan, null, 2));
        setStatus(`Downloaded ${name}.`);
      },
      downloadTestDraft() {
        const name = safeAuthoringName(authoringDraft.flowName || lastMarkdownName || 'scenario.md');
        if (authoringDraft.markdown) downloadText(name, authoringDraft.markdown);
        if (authoringDraft.plan) {
          downloadText(
            planSidecarName(name),
            authoringDraft.planJson || JSON.stringify(authoringDraft.plan, null, 2)
          );
        }
        setStatus('Downloaded the current test draft.');
      },
      async validateFlow() {
        if (!authoringDraft.markdown) {
          setStatus('Load or record a flow before validating.');
          return;
        }
        authoringDraft.saving = true;
        notifyAuthoring();
        try {
          const response = await postAuthoring('/api/flows/validate', {
            name: safeAuthoringName(authoringDraft.flowName),
            markdown: authoringDraft.markdown,
            planJson: authoringDraft.planJson,
          });
          applyAuthoringResponse(response, false);
          authoringDraft.checkPassed = response.ok === true && authoringDraft.errors.length === 0;
          authoringDraft.diffReviewed = false;
          setStatus(response.ok && !authoringDraft.errors.length
            ? 'Test check passed. No app action was started.'
            : response.error || 'Validation found issues.');
        } finally {
          authoringDraft.saving = false;
          notifyAuthoring();
        }
      },
      async diffFlow() {
        if (!authoringDraft.markdown) return;
        authoringDraft.saving = true;
        notifyAuthoring();
        try {
          const response = await postAuthoring('/api/flows/diff', {
            name: safeAuthoringName(authoringDraft.flowName),
            markdown: authoringDraft.markdown,
            planJson: authoringDraft.planJson,
          });
          applyAuthoringResponse(response, false);
          authoringDraft.diffReviewed = response.ok === true && authoringDraft.errors.length === 0;
          setStatus(response.ok ? 'Generated deterministic draft diff.' : response.error || 'Could not generate a diff.');
        } finally {
          authoringDraft.saving = false;
          notifyAuthoring();
        }
      },
      async commitBundle(confirmOverwrite) {
        if (!authoringDraft.markdown) {
          setStatus('Record or load steps before saving a test.');
          return;
        }
        if (!authoringDraft.plan || !hasAuthoringGoal()) {
          openGoalForRecovery('A Goal is required to save this test. Your recorded draft is still here.');
          return;
        }
        const name = safeAuthoringName(authoringDraft.flowName);
        authoringDraft.saving = true;
        notifyAuthoring();
        try {
          const payload = {
            name,
            markdown: authoringDraft.markdown,
            planJson: authoringDraft.planJson || JSON.stringify(authoringDraft.plan, null, 2),
            expectedPlanRevision: authoringDraft.committedPlanRevision,
            expectedPlanDigest: authoringDraft.committedPlanDigest,
            expectedFlowDigest: authoringDraft.committedFlowDigest,
            confirmOverwrite: confirmOverwrite === true,
          };
          const response = await postAuthoring('/api/flows/commit', payload);
          applyAuthoringResponse(response, response.ok === true && response.supported !== false);
          if (!response.ok) {
            authoringDraft.checkPassed = false;
            authoringDraft.diffReviewed = false;
          }
          if (response.ok) {
            lastMarkdown = authoringDraft.markdown;
            lastMarkdownName = name;
            lastMarkdownSource = 'project';
            authoringDraft.rawDraft = false;
            authoringDraft.recordingDraft = false;
            authoringDraft.recordingDraftStepCount = 0;
            authoringDraft.guidanceMessage = 'Test saved — ready to run.';
            authoringDraft.checkPassed = true;
            authoringDraft.diffReviewed = true;
            authoringDraft.attentionStepSequence = null;
            if (response.supported !== false) studyController?.testSaved();
            setStatus('Test saved — ready to run.');
            return;
          }
          if (response.supported === false) {
            const bundle = await hostBundle(name);
            if (!bundle) {
              downloadText(name, authoringDraft.markdown);
              downloadText(planSidecarName(name), payload.planJson);
              setStatus('Could not bind a digest for host persistence; downloaded flow and plan instead.');
              return;
            }
            const hostResult = await hostBridge.request('saveTestBundle', { bundle });
            if (hostResult?.ok) {
              authoringDraft.flowDirty = false;
              authoringDraft.planDirty = false;
              authoringDraft.committedPlan = cloneAuthoring(authoringDraft.plan);
              authoringDraft.committedFlowDigest = bundle.flowDigest;
              authoringDraft.committedPlanDigest = bundle.planDigest;
              authoringDraft.committedPlanRevision = authoringDraft.planRevision;
              authoringDraft.errors = [];
              authoringDraft.warnings = [];
              authoringDraft.stale = false;
              authoringDraft.bindingStale = false;
              authoringDraft.rawDraft = false;
              authoringDraft.recordingDraft = false;
              authoringDraft.recordingDraftStepCount = 0;
              authoringDraft.guidanceMessage = 'Test saved — ready to run.';
              studyController?.testSaved();
              setStatus(hostResult.message || 'Test saved — ready to run.');
            } else {
              downloadText(name, authoringDraft.markdown);
              downloadText(planSidecarName(name), payload.planJson);
              setStatus((hostResult && hostResult.error) || 'Workspace persistence is unavailable; downloaded flow and plan instead.');
            }
          } else {
            setStatus(response.error || 'Bundle commit failed; no partial success was reported.');
          }
        } finally {
          authoringDraft.saving = false;
          notifyAuthoring();
        }
      },
      async verifySelector(selector) {
        const response = await postAuthoring('/api/flows/selector/verify', { selector });
        return response;
      },
      clearAttention() {
        authoringDraft.attentionStepSequence = null;
      },
      selectLiveElement(id) {
        if (!id || !elById(id)) return false;
        selectElement(id);
        return true;
      },
      async applyHumanSelectedSelector({ stepId, stepSequence, selector } = {}) {
        const automationId = typeof selector?.automationId === 'string' ? selector.automationId.trim() : '';
        const stableItemKey = typeof selector?.stableItemKey === 'string' ? selector.stableItemKey.trim() : '';
        const collectionScope = typeof selector?.collectionScope === 'string' ? selector.collectionScope.trim() : '';
        if (!automationId) {
          setStatus('Choose a non-empty AutomationId before updating the draft.');
          return { ok: false, error: 'A human-selected AutomationId is required.' };
        }
        if ((stableItemKey && !collectionScope) || (!stableItemKey && collectionScope)) {
          setStatus('A repeated item needs both a stable item key and its collection scope.');
          return { ok: false, error: 'stableItemKey and collectionScope must be supplied together.' };
        }

        // The bounded ambiguity card only proves that this ID was distinct among displayed
        // candidates. Always use the canonical endpoint to prove global uniqueness immediately
        // before changing the local draft.
        const candidateSelector = {
          automationId,
          ...(stableItemKey && collectionScope ? { stableItemKey, collectionScope } : {}),
        };
        const verification = await controller.verifySelector(candidateSelector);
        if (!verification?.ok || verification.matchCount !== 1) {
          const error = verification?.error || 'The selected AutomationId no longer resolves exactly one live element.';
          setStatus(`${error} The draft was not changed.`);
          return { ok: false, error, verification };
        }

        const flow = currentAuthoringFlow();
        const stepIndex = authoringStepIndexForFailure(flow, stepId, stepSequence);
        if (!flow || stepIndex < 0) {
          const error = 'The failed flow step could not be mapped to one unique draft step. The draft was not changed.';
          setStatus(error);
          return { ok: false, error, verification };
        }

        const next = cloneAuthoring(flow);
        const step = next.steps[stepIndex];
        if (!activeAuthoringStepSelector(step)) {
          const error = 'The mapped failed step has no active selector to replace. The draft was not changed.';
          setStatus(error);
          return { ok: false, error, verification };
        }
        const nextSelector = {
          ...candidateSelector,
          matchCount: 1,
          quality: verification.quality || 'durable',
        };
        if (step?.args?.selector) step.args.selector = nextSelector;
        else step.target = nextSelector;
        step.fragile = false;

        // Only the active selector and its selector-derived fragility flag on the mapped failed
        // step change. The cloned flow retains every action, assertion, expected value, and step
        // position verbatim.
        authoringDraft.flow = next;
        authoringDraft.markdown = replaceAuthoringFlow(authoringDraft.markdown, next);
        authoringDraft.flowDigest = null;
        authoringDraft.flowDirty = true;
        authoringDraft.stale = false;
        authoringDraft.diff = null;
        authoringDraft.errors = [];
        authoringDraft.warnings = [];
        authoringDraft.issues = [];
        authoringDraft.checkPassed = false;
        authoringDraft.diffReviewed = false;
        authoringDraft.attentionStepSequence = null;
        authoringDraft.guidanceMessage =
          'Selector updated in the draft only. Save test, then rerun it; DevFlow did not commit or run anything.';
        notifyAuthoring();
        setStatus(authoringDraft.guidanceMessage);
        return { ok: true, verification, stepIndex, requireSaveAndRerun: true };
      },
      async verifyAssertion(assertion) {
        return await postAuthoring('/api/flows/assert/verify', { assertion });
      },
      async startRecording() {
        const started = await startRecording();
        notifyAuthoring();
        if (started) testWorkbench?.openStage?.('record', true);
      },
      async startAppendingRecording() {
        const started = await startRecording({ append: true });
        notifyAuthoring();
        if (started) testWorkbench?.openStage?.('record', true);
      },
      async quickRecord() {
        await startRecording({ quick: true });
        notifyAuthoring();
      },
      async stopRecording() {
        await stopRecording();
        notifyAuthoring();
      },
      async cancelRecording() {
        await cancelRecording();
        notifyAuthoring();
      },
      async recordingStatus() {
        await syncRecordingStatus();
        notifyAuthoring();
      },
      downloadRecordingDraft() {
        if (!authoringDraft.markdown) {
          setStatus('There is no recording draft to download yet.');
          return;
        }
        const name = safeAuthoringName(authoringDraft.flowName || lastMarkdownName || 'recording.md');
        downloadText(name, authoringDraft.markdown);
        setStatus(`Downloaded recording draft ${name}.`);
      },
      saveRecordingDraftFallback() {
        if (!authoringDraft.markdown) {
          setStatus('There is no recording draft to save yet.');
          return;
        }
        const name = safeAuthoringName(authoringDraft.flowName || lastMarkdownName || 'recording.md');
        if (hostBridge.notify('saveRecording', {
          name: name.replace(/\.md$/i, ''),
          steps: authoringDraft.recordingDraftStepCount,
          markdown: authoringDraft.markdown,
        })) {
          setStatus('Asked the host to save the raw recording draft.');
          return;
        }
        controller.downloadRecordingDraft();
      },
    };
    return Object.freeze(controller);
  }

  function workbenchAnnouncement(message, failure = false) {
    const status = document.getElementById('df-workbench-status');
    const alert = document.getElementById('df-workbench-alert');
    if (status) status.textContent = message || '';
    if (failure && alert) alert.textContent = message || '';
    if (message) setStatus(message);
  }

  function workbenchUpdate(patch, tabs = ['run', 'trace']) {
    if (!testWorkbench) return;
    const selected = testWorkbench.state().selectedTab;
    if (tabs.includes(selected)) testWorkbench.updateState(patch);
  }

  function workbenchRunStorageKey() {
    const agent = inspectorAgent.id || 'default';
    const instance = inspectorAgent.instanceId || 'unknown';
    return `maui-devflow-workbench-run:${agent}:${instance}`;
  }

  function readWorkbenchRunStorage() {
    try {
      const raw = sessionStorage.getItem(workbenchRunStorageKey());
      const parsed = raw ? JSON.parse(raw) : null;
      return parsed && typeof parsed.runId === 'string' && typeof parsed.capabilityToken === 'string' ? parsed : null;
    } catch {
      return null;
    }
  }

  function writeWorkbenchRunStorage(runId, capabilityToken) {
    try {
      if (!runId || !capabilityToken) return;
      sessionStorage.setItem(workbenchRunStorageKey(), JSON.stringify({ runId, capabilityToken }));
    } catch {
      // Session restoration is best effort; the broker-side Inspector journal still covers host handoff.
    }
  }

  function clearWorkbenchRunStorage() {
    try { sessionStorage.removeItem(workbenchRunStorageKey()); } catch {}
  }

  function failureAgentPrompt(context) {
    const testName = context?.testName || authoringDraft.flowName || currentAuthoringFlow()?.name || 'the selected test';
    const failureRequest = context?.failureRequest;
    const improvementsEnvelope = context?.improvementsEnvelope;
    const patchEnvelope = context?.patchEnvelope;
    if (!failureRequest || !improvementsEnvelope || !patchEnvelope)
      throw new Error('The Inspector did not return a complete restricted diagnostic handoff.');

    return [
      'Use only the restricted DevFlow test-agent tools.',
      `Diagnose the exact failed local run for "${testName}". Do not search for or choose a different "latest" run.`,
      `Call maui_test_failure exactly once and pass this exact object as its request argument: ${JSON.stringify(failureRequest)}.`,
      'Do not call maui_test_author begin, status, abandon, or migrate-preview, and do not call maui_test_run or maui_test_trace to rediscover context.',
      'Explain the response plainLanguage in concise user language and recommend its nextSafeAction. Treat omitted checkpoint or route facts as unknown, never as a mismatch.',
      `Only when selectorRepair.status is exactly "eligible", call maui_test_improvements with this exact envelope: ${JSON.stringify(improvementsEnvelope)}. Then create at most one inert selector-only maui_test_patch proposal using this exact object as request.envelope: ${JSON.stringify(patchEnvelope)}. Preserve every action, assertion, value, and step order.`,
      'When selectorRepair.status is not "eligible", stop without a proposal.',
      'Do not approve, apply, run, abandon the handoff, edit source, or change the app.',
    ].join(' ');
  }

  function createOpaqueIdempotencyKey() {
    if (window.crypto?.randomUUID) return `inspector-${crypto.randomUUID()}`;
    return `inspector-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
  }

  function currentAuthoringFlow() {
    return authoringDraft.flow || parseAuthoringFlow(authoringDraft.markdown);
  }

  function currentRunPlanSignature() {
    return JSON.stringify({
      markdown: authoringDraft.markdown || '',
      plan: authoringDraft.plan || null,
      target: runController?.state?.().target?.agentInstanceId || inspectorAgent.instanceId || '',
    });
  }

  function triggerBlobDownload(blob, filename) {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    document.body.append(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  let runController = null;
  let traceController = null;
  let improveController = null;
  let repairController = null;
  let sourceProposalController = null;
  let studyController = null;

  function createRunController() {
    const state = {
      target: null,
      brokerCapabilities: null,
      preflight: null,
      preflighting: false,
      approved: false,
      manualOneShot: false,
      evidence: { includeScreenshot: false, includeWorkflow: false, includeDeviceRecording: false },
      stalePlan: false,
      idempotencyKey: null,
      requestSignature: null,
      preflightSignature: null,
      starting: false,
      cancelConfirm: false,
      cancelPending: false,
      run: null,
      pollTimer: null,
      pollAttempt: 0,
      reproduction: null,
      restoreWriterAfterRun: false,
      runViewStartedAt: 0,
      resultsTimer: null,
    };

    function flowAndPlan() {
      const flow = currentAuthoringFlow();
      return {
        flow,
        plan: authoringDraft.plan || null,
        flowName: authoringDraft.flowName || flow?.name || lastMarkdownName || null,
      };
    }

    function deriveProgress() {
      const report = state.run?.report;
      const flow = currentAuthoringFlow();
      const reportSteps = Array.isArray(report?.steps) ? report.steps : [];
      const flowSteps = Array.isArray(flow?.steps) ? flow.steps : [];
      const latest = Array.isArray(state.run?.events) && state.run.events.length
        ? state.run.events[state.run.events.length - 1]
        : null;
      const completed = Number.isFinite(Number(state.run?.completedSteps))
        ? Math.max(0, Number(state.run.completedSteps))
        : reportSteps.filter((step) => !step?.failureClass).length;
      const currentId = state.run?.currentStepId || latest?.stepId ||
        reportSteps[reportSteps.length - 1]?.stepId || null;
      const failedId = report?.divergenceStepId || report?.failure?.stepId ||
        reportSteps.find((step) => step?.failureClass)?.stepId || null;
      const progressSteps = flowSteps.map((step, index) => {
        const sequence = Number(step?.seq) || index + 1;
        const matching = reportSteps.find((item) =>
          item?.stepId === step?.stepId ||
          Number(item?.sequence) === sequence);
        const stateName = (failedId && (step?.stepId === failedId || matching?.stepId === failedId)) || matching?.failureClass
          ? 'failed'
          : matching || index < completed
            ? 'done'
            : currentId && (step?.stepId === currentId || matching?.stepId === currentId)
              ? 'current'
              : index === completed && state.run && !state.run.terminal
                ? 'current'
                : 'pending';
        return {
          sequence,
          action: step?.label || step?.action || matching?.action || 'step',
          stepId: step?.stepId || matching?.stepId || null,
          state: stateName,
        };
      });
      const currentStep = progressSteps.find((step) => step.state === 'current') ||
        progressSteps.find((step) => step.state === 'failed') || null;
      return {
        currentStep: currentStep?.stepId || currentId || null,
        currentAction: currentStep?.action || latest?.action || latest?.message || null,
        completed,
        total: state.run?.totalSteps ?? flowSteps.length,
        progressSteps,
        latestEvent: latest?.message || state.run?.message || null,
      };
    }

    function notify(message, failure = false) {
      const runState = state.run?.state || (state.preflighting ? 'preflight' : state.preflight ? 'preflight' : 'idle');
      const traceState = traceController?.state?.().mode === 'imported'
        ? 'provenance-warning'
        : state.run?.report ? 'ready' : 'none';
      workbenchUpdate({ run: runState, trace: traceState }, ['run', 'trace']);
      if (message) workbenchAnnouncement(message, failure);
    }

    async function loadTarget() {
      const response = await inspectorApi.getDetailed('/api/workbench/target');
      if (!response.ok || !response.body?.ok) {
        state.target = null;
        state.brokerCapabilities = null;
        return { ok: false, error: response.body?.error || response.error || 'Could not read the broker target.' };
      }
      state.target = response.body.target || null;
      state.brokerCapabilities = response.body.broker || null;
      return { ok: true };
    }

    async function buildRequest() {
      const current = flowAndPlan();
      if (!current.flow || !authoringDraft.markdown) {
        return { error: 'Load or commit a semantic Markdown flow before running it.' };
      }
      const targetResult = await loadTarget();
      if (!targetResult.ok) return targetResult;
      const target = state.target || {};
      if (!target.agentId || !target.agentInstanceId) {
        return { error: 'The broker did not provide an exact live agent instance.' };
      }

      const digest = authoringDraft.flowDigest || await authoringSha256(current.flow);
      if (digest && !authoringDraft.flowDigest) authoringDraft.flowDigest = digest;
      const planDigest = authoringDraft.planDigest || (current.plan ? await authoringSha256(current.plan) : null);
      if (planDigest && !authoringDraft.planDigest) authoringDraft.planDigest = planDigest;
      state.stalePlan = !!(current.plan?.flow?.digest && digest &&
        String(current.plan.flow.digest).toLowerCase() !== digest.toLowerCase());

      const signature = currentRunPlanSignature();
      if (!state.idempotencyKey || state.requestSignature !== signature) {
        state.idempotencyKey = createOpaqueIdempotencyKey();
        state.requestSignature = signature;
      }

      const request = {
        agentId: target.agentId,
        agentInstanceId: target.agentInstanceId,
        idempotencyKey: state.idempotencyKey,
        markdown: authoringDraft.markdown,
        timeoutMs: 120000,
        plan: current.plan || undefined,
        context: {
          manualOneShotAuthorization: state.manualOneShot === true,
        },
      };

      // Browser/Canvas cannot safely calculate a source fingerprint. A reproduction run remains
      // separate and explicit, but matching trust stays unavailable until a trusted host can supply
      // all current flow/build/source/package/target facts.
      if (state.reproduction?.expectation) request.reproductionExpectation = state.reproduction.expectation;
      return { request, flow: current.flow, plan: current.plan };
    }

    function applySnapshot(snapshot, message) {
      if (!snapshot || typeof snapshot !== 'object') return;
      const previousStep = state.run?.currentStepId;
      const previousCompleted = state.run?.completedSteps;
      const previousEvent = Array.isArray(state.run?.events) && state.run.events.length
        ? state.run.events[state.run.events.length - 1]?.eventId ||
          state.run.events[state.run.events.length - 1]?.at ||
          state.run.events.length
        : null;
      state.run = snapshot;
      state.cancelPending = snapshot.cancellationRequested === true && snapshot.terminal !== true;
      const progress = deriveProgress();
      const currentEvent = Array.isArray(snapshot.events) && snapshot.events.length
        ? snapshot.events[snapshot.events.length - 1]?.eventId ||
          snapshot.events[snapshot.events.length - 1]?.at ||
          snapshot.events.length
        : null;
      if (snapshot.report || snapshot.terminal) traceController?.setLiveRun?.(snapshot, state.reproduction);
      if (!snapshot.terminal && (
        previousStep !== snapshot.currentStepId ||
        previousCompleted !== snapshot.completedSteps ||
        previousEvent !== currentEvent)) {
        // Reuse the normal coalesced Inspector refresh so screenshot and tree updates stay safe
        // while the broker advances semantic steps.
        scheduleRefresh(125);
      }
      if (snapshot.terminal) {
        studyController?.runTerminal(snapshot);
        if (state.pollTimer) {
          clearTimeout(state.pollTimer);
          state.pollTimer = null;
        }
        state.pollAttempt = 0;
        state.cancelPending = false;
        if (snapshot.state === 'passed') {
          workbenchAnnouncement(message || 'Test passed. Results are ready.', false);
        } else {
          const terminalMessage = message || `Broker run finished: ${snapshot.state || 'unknown'}.`;
          workbenchAnnouncement(terminalMessage, true);
        }
        const showResults = () => {
          state.resultsTimer = null;
          testWorkbench?.openStage?.('results', false);
          setTimeout(() => traceController?.focusResults?.(), 0);
        };
        if (state.resultsTimer) clearTimeout(state.resultsTimer);
        const visibleFor = state.runViewStartedAt ? Date.now() - state.runViewStartedAt : 900;
        const remaining = Math.max(0, 900 - visibleFor);
        if (remaining > 0) state.resultsTimer = setTimeout(showResults, remaining);
        else showResults();
        if (state.restoreWriterAfterRun) {
          state.restoreWriterAfterRun = false;
          setTimeout(() => {
            if (!capturedTraceMode) control('claim');
          }, 0);
        }
      }
      workbenchUpdate({
        run: snapshot.state || 'idle',
        trace: snapshot.report ? 'ready' : 'none',
        selectedTraceStep: traceController?.state?.().selectedStepId || null,
      }, ['run', 'trace']);
      return progress;
    }

    function schedulePoll() {
      if (!state.run?.runId || state.run.terminal || state.pollTimer) return;
      const delay = Math.min(5000, 400 * (2 ** Math.min(state.pollAttempt, 4)));
      state.pollTimer = setTimeout(async () => {
        state.pollTimer = null;
        await refreshStatus();
        if (!state.run?.terminal) schedulePoll();
      }, delay);
      state.pollAttempt += 1;
    }

    async function refreshStatus() {
      if (!state.run?.runId) return;
      const saved = readWorkbenchRunStorage();
      const capabilityToken = saved?.runId === state.run.runId ? saved.capabilityToken : state.run.capabilityToken;
      const response = await inspectorApi.postDetailed(
        `/api/workbench/run/${encodeURIComponent(state.run.runId)}/status`,
        { capabilityToken }
      );
      if (!response.ok || response.body?.ok !== true || !response.body?.run) {
        const error = response.body?.error || response.error || 'Could not refresh broker run status.';
        if (response.status === 403 || response.status === 404) {
          clearWorkbenchRunStorage();
          const journal = await inspectorApi.getDetailed('/api/workbench/run/journal');
          if (journal.ok && journal.body?.run) {
            applySnapshot(journal.body.run, 'Recovered broker run state from the Inspector journal.');
          } else {
            applySnapshot({
              ...state.run,
              state: 'orphaned',
              terminal: true,
              message: 'The saved run capability is no longer valid and no broker journal entry could be recovered.',
            });
          }
          return;
        }
        notify(error, true);
        return;
      }
      applySnapshot(response.body.run);
      if (!response.body.run.terminal) schedulePoll();
    }

    return Object.freeze({
      state() {
        const current = flowAndPlan();
        const progress = deriveProgress();
        return {
          ...state,
          flow: current.flow,
          plan: current.plan,
          flowName: current.flowName,
          flowDigest: authoringDraft.flowDigest,
          planDigest: authoringDraft.planDigest,
          brokerCapabilities: state.brokerCapabilities,
          readiness: authoringReadiness(),
          agent: inspectorAgent,
          importedMode: traceController?.state?.().mode === 'imported',
          hasTrace: !!state.run?.report,
          ...progress,
        };
      },
      async openPreflight() {
        if (traceController?.state?.().mode === 'imported') {
          workbenchAnnouncement('Imported result mode is read-only. Use Reproduce locally to open a separate live run check.', true);
          return;
        }
        if (!state.reproduction && !authoringReadiness().savedBundle) {
          if (!hasAuthoringGoal()) openGoalForRecovery('Add a Goal and save the test before running it.');
          else {
            testWorkbench?.openStage?.('review', true);
            workbenchAnnouncement('Save the test before running it. Your recorded draft is still here.', true);
          }
          return;
        }
        if (state.run?.terminal) {
          state.run = null;
          state.preflight = null;
          state.preflightSignature = null;
          state.approved = false;
          state.manualOneShot = false;
        }
        await this.refreshPreflight();
      },
      async refreshPreflight() {
        if (traceController?.state?.().mode === 'imported') return;
        if (!state.reproduction && !authoringReadiness().savedBundle) {
          if (!hasAuthoringGoal()) openGoalForRecovery('Add a Goal and save the test before running it.');
          else {
            testWorkbench?.openStage?.('review', true);
            workbenchAnnouncement('Save the test before running it. Your recorded draft is still here.', true);
          }
          return;
        }
        state.preflighting = true;
        notify('Checking whether the test is ready to run…');
        try {
          const built = await buildRequest();
          if (!built.request) {
            state.preflight = { ok: false, errors: [built.error || 'Could not build a run request.'] };
            notify(built.error || 'Could not build a run request.', true);
            return;
          }
          const response = await inspectorApi.postDetailed('/api/workbench/run/preflight', {
            run: built.request,
            evidence: state.evidence,
          });
          state.preflight = response.body || { ok: false, error: response.error || 'No run-check response.' };
          state.preflightSignature = state.requestSignature;
          if (!response.ok || state.preflight.ok !== true) {
            notify(state.preflight.error || 'The test is not ready to run.', true);
          } else {
            authoringDraft.flowDigest = state.preflight.flowDigest || authoringDraft.flowDigest;
            notify('The test is ready. Review the summary before starting.');
          }
        } finally {
          state.preflighting = false;
          workbenchUpdate({ run: 'preflight' }, ['run']);
        }
      },
      setApproval(value) {
        state.approved = value === true;
        workbenchUpdate({ run: 'preflight' }, ['run']);
      },
      setManualOneShot(value) {
        state.manualOneShot = value === true;
        state.preflight = null;
        state.preflightSignature = null;
        workbenchUpdate({ run: 'preflight' }, ['run']);
      },
      setEvidenceConsent(patch) {
        state.evidence = { ...state.evidence, ...(patch || {}) };
        state.preflight = null;
        state.preflightSignature = null;
        state.approved = false;
        workbenchUpdate({ run: 'preflight' }, ['run']);
      },
      async reviewAndRun() {
        if (traceController?.state?.().mode === 'imported') {
          notify('Imported trace mode cannot start a run.', true);
          return;
        }
        if (state.preflight?.ok !== true) {
          notify('Choose Check run before reviewing and starting.', true);
          return;
        }
        if (flowAndPlan().plan?.sideEffectPolicy === 'non-replayable' && state.manualOneShot !== true) {
          notify('Authorize the one human run in Run details before continuing.', true);
          return;
        }
        const deviceEffects = Array.isArray(state.preflight?.deviceEffects)
          ? [...state.preflight.deviceEffects]
          : [];
        if (state.evidence?.includeDeviceRecording === true &&
            !deviceEffects.some((effect) => /record the surrounding device screen/i.test(String(effect)))) {
          deviceEffects.push('Record the surrounding device screen for the full run');
        }
        const deviceReview = deviceEffects.length
          ? `\n\nExact device mutations:\n${deviceEffects.map((effect) => `• ${String(effect).slice(0, 320)}`).join('\n')}`
          : '';
        const confirmed = await confirmModal(
          `Run this saved test against the live app? It may drive the app and change test data.${deviceReview}`,
          'Run test'
        );
        if (!confirmed) {
          notify('Run was not started.');
          return;
        }
        state.approved = true;
        await this.start();
      },
      async start() {
        if (traceController?.state?.().mode === 'imported') {
          notify('Imported trace mode cannot start a run.', true);
          return;
        }
        if (state.approved !== true || state.preflight?.ok !== true) {
          notify('Review the current run check and explicitly approve it before starting.', true);
          return;
        }
        state.starting = true;
        state.runViewStartedAt = Date.now();
        testWorkbench?.openStage?.('run', false);
        notify('Starting broker-owned workflow run…');
        let handedOffWriter = false;
        try {
          const built = await buildRequest();
          if (!built.request) {
            notify(built.error || 'Could not build a run request.', true);
            return;
          }
          if (state.preflightSignature !== state.requestSignature) {
            state.preflight = null;
            notify('The test or target changed after the run check. Check it again before starting.', true);
            return;
          }
          handedOffWriter = isWriter;
          state.restoreWriterAfterRun = handedOffWriter;
          const startAttemptedAt = Date.now();
          const response = await inspectorApi.postDetailed('/api/workbench/run/start', {
            run: built.request,
            evidence: state.evidence,
            reviewedDeviceEffectsDigest: state.preflight.deviceEffectsDigest || null,
          });
          const result = response.body;
          if (!response.ok || result?.ok !== true || !result.run || !result.capabilityToken) {
            const ambiguous = response.status === 0 || response.status >= 500 || response.ok === true;
            if (handedOffWriter && ambiguous) {
              const journalPath = state.idempotencyKey
                ? `/api/workbench/run/journal?idempotencyKey=${encodeURIComponent(state.idempotencyKey)}`
                : '/api/workbench/run/journal';
              let recovered = null;
              let delay = 0;
              let observedPending = false;
              const registrationDeadline = Date.now() + 45000;
              while (true) {
                if (delay) await new Promise((resolve) => setTimeout(resolve, delay));
                const journal = await inspectorApi.getDetailed(journalPath);
                const candidate = journal.ok && journal.body?.ok === true ? journal.body.run : null;
                const createdAt = Date.parse(candidate?.createdAt || '');
                const sameFlow = !state.preflight?.flowDigest ||
                  candidate?.flowDigest === state.preflight.flowDigest;
                const recent = !Number.isFinite(createdAt) || createdAt >= startAttemptedAt - 5000;
                if (candidate?.runId && sameFlow && recent) {
                  recovered = candidate;
                  break;
                }
                if (journal.ok && journal.body?.ok === true) {
                  if (journal.body.pending === true) {
                    observedPending = true;
                  } else if (journal.body.pending === false &&
                    (observedPending || Date.now() >= registrationDeadline)) {
                    break;
                  }
                }
                delay = Math.min(delay ? delay * 2 : 100, 2000);
              }
              if (recovered?.runId) {
                setWriterUi(false, true, 'Broker workflow run', null);
                state.run = recovered;
                state.cancelConfirm = false;
                state.cancelPending = false;
                state.pollAttempt = 0;
                applySnapshot(recovered, 'Recovered the broker run after the start response was interrupted.');
                if (!recovered.terminal) schedulePoll();
                return;
              }
            }
            state.preflight = result || state.preflight;
            notify(result?.error || response.error || 'Broker did not start the run.', true);
            if (handedOffWriter && !capturedTraceMode) {
              state.restoreWriterAfterRun = false;
              await control('claim');
            }
            return;
          }
          if (result.existing !== true) studyController?.runStarted(result.run);
          if (handedOffWriter)
            setWriterUi(false, true, 'Broker workflow run', null);
          state.run = { ...result.run, capabilityToken: result.capabilityToken };
          writeWorkbenchRunStorage(result.run.runId, result.capabilityToken);
          state.cancelConfirm = false;
          state.cancelPending = false;
          state.pollAttempt = 0;
          applySnapshot(state.run, result.existing ? 'Restored the existing idempotent broker run.' : 'Broker run queued.');
          schedulePoll();
        } finally {
          if (handedOffWriter && !state.run?.runId && state.restoreWriterAfterRun) {
            state.restoreWriterAfterRun = false;
            if (!capturedTraceMode) await control('claim');
          }
          if (!state.run?.runId) state.runViewStartedAt = 0;
          state.starting = false;
          workbenchUpdate({ run: state.run?.state || 'preflight' }, ['run']);
        }
      },
      requestCancel() {
        if (!state.run?.runId || state.run.terminal) {
          notify('There is no active broker run to cancel.');
          return;
        }
        state.cancelConfirm = true;
        workbenchUpdate({ run: state.run.state }, ['run']);
      },
      dismissCancel() {
        state.cancelConfirm = false;
        workbenchUpdate({ run: state.run?.state || 'idle' }, ['run']);
      },
      async cancel() {
        if (!state.run?.runId) return;
        state.cancelPending = true;
        state.cancelConfirm = false;
        notify('Cancellation requested. An in-flight command may already complete.');
        const saved = readWorkbenchRunStorage();
        const token = saved?.runId === state.run.runId ? saved.capabilityToken : state.run.capabilityToken;
        const response = await inspectorApi.postDetailed(
          `/api/workbench/run/${encodeURIComponent(state.run.runId)}/cancel`,
          { capabilityToken: token }
        );
        if (!response.ok || response.body?.ok !== true) {
          state.cancelPending = false;
          notify(response.body?.error || response.error || 'The broker could not accept cancellation.', true);
          return;
        }
        if (response.body.run) applySnapshot(response.body.run);
        if (!state.run?.terminal) schedulePoll();
      },
      async restore() {
        const stored = readWorkbenchRunStorage();
        if (stored) {
          state.run = { runId: stored.runId, capabilityToken: stored.capabilityToken, state: 'queued', terminal: false };
          state.restoreWriterAfterRun = true;
          await refreshStatus();
          return;
        }
        const response = await inspectorApi.getDetailed('/api/workbench/run/journal');
        if (response.ok && response.body?.run) {
          state.run = response.body.run;
          state.restoreWriterAfterRun = response.body.run.terminal !== true;
          applySnapshot(response.body.run, response.body.restored ? 'Restored active broker run from the Inspector journal.' : undefined);
          if (!response.body.run.terminal) schedulePoll();
        }
      },
      async openReproduction(imported) {
        state.reproduction = imported
          ? {
            artifactId: imported.status?.identity?.id,
            capabilityToken: imported.capabilityToken,
            current: null,
            expectation: null,
            message: 'Current source fingerprint is not available to this browser. The separate run remains diagnostic-only until a trusted host supplies all matching facts.',
          }
          : null;
        state.preflight = null;
        state.preflightSignature = null;
        state.approved = false;
        state.manualOneShot = false;
        await setCapturedTraceMode(false);
        await this.refreshPreflight();
        workbenchUpdate({ run: 'preflight', trace: 'provenance-warning' }, ['run', 'trace']);
        workbenchAnnouncement('A separate local run check is open. It has not started a run.', false);
      },
      focusDivergence() {
        traceController?.focusSelectedStep?.();
      },
      focusResults() {
        traceController?.focusResults?.();
      },
      async runAgain() {
        if (state.resultsTimer) {
          clearTimeout(state.resultsTimer);
          state.resultsTimer = null;
        }
        state.run = null;
        state.runViewStartedAt = 0;
        state.preflight = null;
        state.preflightSignature = null;
        state.idempotencyKey = null;
        state.requestSignature = null;
        state.approved = false;
        state.manualOneShot = false;
        testWorkbench?.openStage?.('run', false);
        await this.refreshPreflight();
        workbenchAnnouncement('Run check opened. The test has not started.', false);
      },
      async legacyQuickReplay() {
        await legacyQuickReplay();
      },
      async prepareFailureAgentPrompt() {
        const current = flowAndPlan();
        const run = state.run;
        if (!run?.runId || !run.terminal || !run.report?.failure)
          throw new Error('Open a terminal failed local result before preparing an agent handoff.');
        if (!current.flow)
          throw new Error('The loaded semantic test is unavailable.');

        const stored = readWorkbenchRunStorage();
        const capabilityToken = stored?.runId === run.runId
          ? stored.capabilityToken
          : run.capabilityToken;
        const response = await inspectorApi.postDetailed('/api/workbench/agent-handoff', {
          runId: run.runId,
          capabilityToken,
          flowName: current.flowName,
          markdown: authoringDraft.markdown,
          flow: current.flow,
          plan: current.plan,
        });
        if (!response.ok || response.body?.ok !== true || !response.body?.context)
          throw new Error(response.body?.error || response.error || 'Could not prepare the restricted agent handoff.');
        return failureAgentPrompt(response.body.context);
      },
      async bindReproduction() {
        const imported = state.reproduction;
        if (!imported?.artifactId || !imported?.capabilityToken || !state.run?.terminal) {
          notify('A completed separate local run and imported artifact are required before matching.', true);
          return;
        }
        if (!imported.expectation) {
          notify(imported.message || 'Current source matching facts are unavailable in this host.', true);
          return;
        }
        const response = await inspectorApi.postDetailed(
          `/api/workbench/artifacts/${encodeURIComponent(imported.artifactId)}/bind-local-reproduction`,
          {
            capabilityToken: imported.capabilityToken,
            localRunId: state.run.runId,
            current: imported.expectation,
          }
        );
        traceController?.setReproduction?.(response.body);
        notify(response.ok && response.body?.ok
          ? 'Local reproduction facts were bound to the imported diagnostic artifact.'
          : response.body?.error || response.error || 'Local reproduction binding was not established.',
          !(response.ok && response.body?.ok));
      },
    });
  }

  function createTraceController() {
    const state = {
      mode: 'none',
      run: null,
      report: null,
      selectedStepId: null,
      importing: false,
      imported: null,
      reproductionOpening: false,
      reproduction: null,
    };

    function currentSteps() {
      return Array.isArray(state.report?.steps) ? state.report.steps : [];
    }

    function firstDivergence(report) {
      return report?.divergenceStepId || report?.failure?.stepId ||
        currentSteps().find((step) => step?.failureClass)?.stepId || null;
    }

    function notify() {
      const trace = state.mode === 'imported' ? 'provenance-warning' : state.report ? 'ready' : 'none';
      workbenchUpdate({ trace, selectedTraceStep: state.selectedStepId }, ['trace', 'run']);
    }

    function fileKind(name) {
      const normalized = String(name || '').toLowerCase();
      if (normalized.endsWith('.mauitrace')) return 'mauitrace';
      if (normalized.endsWith('.json')) return 'flow-run';
      return null;
    }

    function browserPickTrace(returnFocus = null) {
      return new Promise((resolve) => {
        const maxFlowRunBytes = 1024 * 1024;
        const maxEvidenceBytes = 64 * 1024 * 1024;
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = '.json,.mauitrace,application/json,application/vnd.maui.evidence+zip';
        input.className = 'df-sr-only';
        input.tabIndex = -1;
        input.setAttribute('aria-hidden', 'true');
        let settled = false;
        let cancelTimer = null;
        const cleanup = () => {
          input.remove();
          window.removeEventListener('focus', onWindowFocus);
          if (cancelTimer !== null) window.clearTimeout(cancelTimer);
        };
        const finish = (result, restoreFocus = false) => {
          if (settled) return;
          settled = true;
          cleanup();
          if (restoreFocus && returnFocus?.isConnected && typeof returnFocus.focus === 'function') {
            window.setTimeout(() => {
              if (returnFocus.isConnected) returnFocus.focus({ preventScroll: true });
            }, 0);
          }
          resolve(result);
        };
        const onWindowFocus = () => {
          cancelTimer = window.setTimeout(() => {
            if (!input.files?.length) finish(null, true);
          }, 500);
        };
        input.addEventListener('change', async () => {
          const file = input.files?.[0] || null;
          if (!file) {
            finish(null, true);
            return;
          }
          const kind = fileKind(file.name);
          const maximumBytes = kind === 'flow-run' ? maxFlowRunBytes : maxEvidenceBytes;
          if (!kind || file.size > maximumBytes) {
            finish({
              error: !kind
                ? 'Choose a .json flow result or .mauitrace evidence bundle.'
                : 'The selected artifact exceeds the supported size limit.',
            }, true);
            return;
          }
          try {
            finish({ name: file.name, bytes: new Uint8Array(await file.arrayBuffer()) });
          } catch {
            finish({ error: 'The selected artifact could not be read.' });
          }
        }, { once: true });
        input.addEventListener('cancel', () => finish(null, true), { once: true });
        document.body.append(input);
        window.addEventListener('focus', onWindowFocus, { once: true });
        try {
          input.click();
        } catch {
          finish({ error: 'The result-file picker could not be opened.' }, true);
        }
      });
    }

    function decodeBase64(value) {
      if (typeof value !== 'string' || value.length > 90 * 1024 * 1024) return null;
      try {
        const decoded = atob(value);
        const bytes = new Uint8Array(decoded.length);
        for (let index = 0; index < decoded.length; index++) bytes[index] = decoded.charCodeAt(index);
        return bytes;
      } catch {
        return null;
      }
    }

    async function importBytes(name, bytes) {
      const kind = fileKind(name);
      if (!kind) {
        workbenchAnnouncement('Choose a .json flow-run report or a .mauitrace v1 bundle.', true);
        return;
      }
      const maximum = kind === 'flow-run' ? 1024 * 1024 : 64 * 1024 * 1024;
      if (!(bytes instanceof Uint8Array) || !bytes.byteLength || bytes.byteLength > maximum) {
        workbenchAnnouncement(`The selected ${kind} artifact exceeds its supported bounded size.`, true);
        return;
      }
      state.importing = true;
      notify();
      try {
        const response = await inspectorApi.postBinary(
          `/api/workbench/artifacts/import?kind=${encodeURIComponent(kind)}`,
          bytes,
          kind === 'flow-run' ? 'application/json' : 'application/vnd.maui.evidence+zip'
        );
        const result = response ? await response.json().catch(() => null) : null;
        if (!response?.ok || !result?.ok || !result.status?.identity?.id || !result.capabilityToken) {
          workbenchAnnouncement(result?.error || `The broker rejected the imported ${kind} artifact.`, true);
          return;
        }
        const artifactId = result.status.identity.id;
        const capabilityToken = result.capabilityToken;
        const [statusResponse, projectionResponse] = await Promise.all([
          inspectorApi.postDetailed(`/api/workbench/artifacts/${encodeURIComponent(artifactId)}/status`, { capabilityToken }),
          inspectorApi.postDetailed(`/api/workbench/artifacts/${encodeURIComponent(artifactId)}/projection`, { capabilityToken }),
        ]);
        if (!statusResponse.ok || !projectionResponse.ok || !statusResponse.body?.status || !projectionResponse.body?.projection) {
          workbenchAnnouncement('The imported artifact was retained, but its safe diagnostic projection is unavailable.', true);
          return;
        }
        state.mode = 'imported';
        state.imported = {
          name: String(name || 'imported artifact').slice(0, 255),
          capabilityToken,
          status: statusResponse.body.status,
          projection: projectionResponse.body.projection,
        };
        state.run = null;
        state.report = null;
        state.selectedStepId = null;
        state.reproduction = null;
        await setCapturedTraceMode(true);
        notify();
        workbenchAnnouncement('Imported trace opened in captured read-only mode. No app action was started.');
      } finally {
        state.importing = false;
        notify();
      }
    }

    return Object.freeze({
      state: () => ({ ...state, importEnabled: previewFeatures.traceImport }),
      async pickTrace() {
        if (state.importing) return;
        if (!previewFeatures.traceImport) {
          workbenchAnnouncement('Opening result files is disabled in this Inspector. No file picker was opened.', true);
          return;
        }
        const pickerReturnFocus = document.activeElement;
        let picked = null;
        if (hostBridge.has('pickTrace')) {
          const host = await hostBridge.request('pickTrace', {}, 60000);
          if (!host?.ok) {
            workbenchAnnouncement(host?.error || 'The host did not provide a trace artifact.', true);
            return;
          }
          const value = host.value || {};
          const bytes = decodeBase64(value.bytesBase64);
          picked = bytes ? { name: value.name, bytes } : { error: 'The host returned an invalid bounded trace artifact.' };
        } else {
          picked = await browserPickTrace(pickerReturnFocus);
        }
        if (!picked) return;
        if (picked.error) {
          workbenchAnnouncement(picked.error, true);
          return;
        }
        await importBytes(picked.name, picked.bytes);
      },
      setLiveRun(snapshot, reproduction) {
        state.mode = 'local';
        state.run = snapshot;
        state.report = snapshot?.report || null;
        state.selectedStepId = firstDivergence(state.report);
        if (reproduction) {
          state.reproduction = {
            ...state.reproduction,
            candidateRunId: snapshot?.runId,
            canBind: !!reproduction.expectation,
            unavailableReason: reproduction.expectation ? null : reproduction.message,
            source: reproduction,
          };
        }
        notify();
      },
      selectStep(stepId) {
        if (!stepId) return;
        state.selectedStepId = stepId;
        notify();
      },
      canMove(direction) {
        const steps = currentSteps();
        const index = steps.findIndex((step) => step?.stepId === state.selectedStepId);
        return direction < 0 ? index > 0 : index >= 0 && index < steps.length - 1;
      },
      previousStep() {
        const steps = currentSteps();
        const index = steps.findIndex((step) => step?.stepId === state.selectedStepId);
        if (index > 0) this.selectStep(steps[index - 1]?.stepId);
      },
      nextStep() {
        const steps = currentSteps();
        const index = steps.findIndex((step) => step?.stepId === state.selectedStepId);
        if (index >= 0 && index < steps.length - 1) this.selectStep(steps[index + 1]?.stepId);
      },
      focusSelectedStep() {
        const id = state.selectedStepId;
        if (!id) return;
        setTimeout(() => {
          const button = [...document.querySelectorAll('[data-trace-step]')]
            .find((candidate) => candidate.dataset.traceStep === id);
          button?.focus({ preventScroll: true });
        }, 0);
      },
      focusResults() {
        const id = state.selectedStepId || firstDivergence(state.report);
        setTimeout(() => {
          if (id) {
            const button = [...document.querySelectorAll('[data-trace-step]')]
              .find((candidate) => candidate.dataset.traceStep === id);
            if (button) {
              button.focus({ preventScroll: true });
              return;
            }
          }
          document.getElementById('df-results-summary')?.focus({ preventScroll: true });
        }, 0);
      },
      runAgain() {
        return runController?.runAgain?.();
      },
      hasDownloadableEvidence() {
        return !!state.run?.runId && Array.isArray(state.report?.artifacts) &&
          state.report.artifacts.some((artifact) => artifact?.kind === 'mauitrace');
      },
      async downloadEvidence() {
        const run = state.run;
        if (!run?.runId) return;
        const stored = readWorkbenchRunStorage();
        const capabilityToken = stored?.runId === run.runId ? stored.capabilityToken : run.capabilityToken;
        const response = await inspectorApi.postBlob(
          `/api/workbench/run/${encodeURIComponent(run.runId)}/evidence`,
          { capabilityToken }
        );
        if (!response?.ok) {
          const error = response ? await response.json().catch(() => null) : null;
          workbenchAnnouncement(error?.error || 'No linked .mauitrace evidence is available for this run.', true);
          return;
        }
        triggerBlobDownload(await response.blob(), `devflow-${run.runId}.mauitrace`);
        workbenchAnnouncement('Downloaded linked redacted .mauitrace v1 evidence.');
      },
      hasDownloadableDeviceRecording() {
        return !!state.run?.runId && Array.isArray(state.report?.artifacts) &&
          state.report.artifacts.some((artifact) => artifact?.kind === 'device-recording');
      },
      async downloadDeviceRecording() {
        const run = state.run;
        if (!run?.runId) return;
        const stored = readWorkbenchRunStorage();
        const capabilityToken = stored?.runId === run.runId ? stored.capabilityToken : run.capabilityToken;
        const response = await inspectorApi.postBlob(
          `/api/workbench/run/${encodeURIComponent(run.runId)}/device-recording`,
          { capabilityToken }
        );
        if (!response?.ok) {
          const error = response ? await response.json().catch(() => null) : null;
          workbenchAnnouncement(error?.error || 'No retained device recording is available for this run.', true);
          return;
        }
        triggerBlobDownload(await response.blob(), `devflow-${run.runId}-device.mp4`);
        workbenchAnnouncement('Downloaded the explicitly approved device recording.');
      },
      async reproduceLocally() {
        if (!state.imported || state.reproductionOpening) return;
        state.reproductionOpening = true;
        // Keep the imported projection isolated, but leave captured mode before opening the
        // separate live preflight. This action still does not start a run.
        state.mode = 'reproduction';
        notify();
        try {
          await runController?.openReproduction?.(state.imported);
          testWorkbench?.openStage?.('run', true);
        } finally {
          state.reproductionOpening = false;
          notify();
        }
      },
      async showImported() {
        if (!state.imported) return;
        state.mode = 'imported';
        await setCapturedTraceMode(true);
        notify();
      },
      setReproduction(result) {
        state.reproduction = result
          ? { ...state.reproduction, ...result }
          : null;
        if (state.imported) state.imported.status = result?.status || state.imported.status;
        notify();
      },
      verifyReproduction() {
        runController?.bindReproduction?.();
      },
    });
  }

  function createRepairController() {
    const state = {
      classifying: false,
      proposing: false,
      eligibility: null,
      classificationToken: null,
      checkpoint: null,
      proposal: null,
      validationAvailable: null,
      generation: null,
      error: null,
      history: [],
    };

    function current() {
      const flow = currentAuthoringFlow();
      const plan = authoringDraft.plan || null;
      const trace = traceController?.state?.();
      const run = trace?.run || runController?.state?.().run || null;
      const report = trace?.report || trace?.run?.report || null;
      const failedStep = Array.isArray(report?.steps)
        ? report.steps.find((step) => step?.stepId === (report?.failure?.stepId || report?.divergenceStepId)) || null
        : null;
      return { flow, plan, run, report, failedStep, reproduction: trace?.reproduction || null };
    }

    function setRepairState(message, failure = false) {
      const proposal = state.proposal?.proposal || state.proposal || null;
      const repair = proposal?.state ||
        (state.classifying ? 'classifying' : state.eligibility?.eligible ? 'proposed' : 'unavailable');
      studyController?.repairTransition(repair, proposal?.proposalId);
      workbenchUpdate({ repair }, ['repair']);
      if (message) workbenchAnnouncement(message, failure);
    }

    function applyValidationAvailability(body) {
      if (body && Object.prototype.hasOwnProperty.call(body, 'repairValidationAvailable')) {
        state.validationAvailable = body.repairValidationAvailable === true;
      }
    }

    function browserApprovalAvailable() {
      return runController?.state?.().brokerCapabilities?.browserApprovalAvailable === true;
    }

    function repairDisabled() {
      if (previewFeatures.repair) return false;
      state.error = 'Selector repair is disabled in this Inspector.';
      setRepairState(state.error, true);
      return true;
    }

    return Object.freeze({
      state: () => ({
        ...state,
        history: [...state.history],
        current: current(),
        validationAvailable:
          state.validationAvailable === false ||
          runController?.state?.().brokerCapabilities?.repairValidationAvailable === false
            ? false
            : state.validationAvailable === true ||
                runController?.state?.().brokerCapabilities?.repairValidationAvailable === true
              ? true
              : null,
        canMutate: isWriter && connected && !capturedTraceMode,
        enabled: previewFeatures.repair,
        approvalAvailable:
          runController?.state?.().brokerCapabilities?.browserApprovalAvailable === true,
      }),
      async classify() {
        if (repairDisabled()) return;
        const { flow, run, report, reproduction } = current();
        if (!flow || !run?.runId || !report?.failure) {
          state.error = 'Open a failed local flow run before classifying selector repair eligibility.';
          setRepairState(state.error, true);
          return;
        }
        const stored = readWorkbenchRunStorage();
        const runCapabilityToken = stored?.runId === run.runId
          ? stored.capabilityToken
          : run.capabilityToken;
        if (!runCapabilityToken) {
          state.error = 'The broker-owned run capability is unavailable. Run the saved test again before classifying repair.';
          setRepairState(state.error, true);
          return;
        }
        state.classifying = true;
        state.error = null;
        state.classificationToken = null;
        setRepairState('Classifying repair eligibility without changing the flow.');
        try {
          const response = await inspectorApi.postDetailed('/api/workbench/repair/classify', {
            runId: run.runId,
            runCapabilityToken,
            ...(reproduction?.status?.verification?.state === 'locally-reproduced' &&
                reproduction?.artifactId &&
                reproduction?.capabilityToken
              ? {
                artifactId: reproduction.artifactId,
                artifactCapabilityToken: reproduction.capabilityToken,
              }
              : {}),
          });
          if (!response.ok || !response.body?.ok) {
            state.error = response.body?.error || response.error || 'Repair eligibility classification failed.';
            return;
          }
          applyValidationAvailability(response.body);
          state.eligibility = response.body.eligibility || null;
          state.classificationToken = response.body.classificationToken || null;
          state.checkpoint = response.body.currentCheckpoint || null;
          state.error = null;
          const allowed = state.eligibility?.eligible === true;
          setRepairState(
            allowed
              ? 'Repair eligibility passed. Review deterministic candidates before requesting a proposal.'
              : 'Repair is unavailable. Review every explicit ineligibility reason.',
            !allowed);
        } finally {
          state.classifying = false;
          setRepairState();
        }
      },
      async propose() {
        if (repairDisabled()) return;
        const { flow, report, failedStep } = current();
        if (!state.eligibility?.eligible || !flow || !report || !failedStep) {
          state.error = 'Eligibility, a failed local run, and a semantic flow are required before proposing a repair.';
          setRepairState(state.error, true);
          return;
        }
        state.proposing = true;
        state.error = null;
        try {
          const response = await inspectorApi.postDetailed('/api/workbench/repair/propose', {
            classificationToken: state.classificationToken,
            input: {
              priorFingerprint: null,
              // The browser never claims a fresh live uniqueness/fingerprint proof. A capable
              // lifecycle host supplies these facts; without them the deterministic core abstains.
              currentResolutions: [],
            },
            agentOriginated: false,
          });
          applyValidationAvailability(response.body);
          state.generation = response.body?.generation || null;
          const proposal = Array.isArray(response.body?.proposals) ? response.body.proposals[0] : null;
          state.proposal = proposal || null;
          if (!response.ok || !response.body?.ok || !proposal) {
            state.error = response.body?.error ||
              'No safe repair proposal was generated. Ambiguous, unproven, or unsafe candidates remain diagnostic-only.';
            setRepairState(state.error, true);
            return;
          }
          state.history = [...state.history, { state: proposal.state, at: new Date().toISOString() }];
          setRepairState('A selector-only proposal is available for human preview. Nothing has been applied.');
        } finally {
          state.proposing = false;
          setRepairState();
        }
      },
      async preview() {
        if (repairDisabled()) return;
        const id = state.proposal?.proposal?.proposalId || state.proposal?.proposalId;
        if (!id) return;
        const response = await inspectorApi.postDetailed(`/api/workbench/repair/${encodeURIComponent(id)}/preview`, {});
        if (!response.ok || !response.body?.ok) {
          state.error = response.body?.error || response.error || 'Repair preview could not be loaded.';
          setRepairState(state.error, true);
          return;
        }
        applyValidationAvailability(response.body);
        state.proposal = response.body.proposal;
        setRepairState('Selector-only diff preview loaded. Assertions, actions, values, and order are unchanged.');
      },
      async refresh() {
        if (repairDisabled()) return;
        const id = state.proposal?.proposal?.proposalId || state.proposal?.proposalId;
        if (!id) return;
        const response = await inspectorApi.postDetailed(`/api/workbench/repair/${encodeURIComponent(id)}/status`, {});
        if (response.ok && response.body?.proposal) {
          applyValidationAvailability(response.body);
          state.proposal = response.body.proposal;
          setRepairState();
        } else {
          state.error = response.body?.error || response.error || 'Repair status could not be refreshed.';
          setRepairState(state.error, true);
        }
      },
      async reject() {
        if (repairDisabled()) return;
        const id = state.proposal?.proposal?.proposalId || state.proposal?.proposalId;
        if (!id) return;
        const response = await inspectorApi.postDetailed(`/api/workbench/repair/${encodeURIComponent(id)}/reject`, {
          reviewer: 'workbench-user',
          reasonCode: 'human-rejected',
        });
        if (response.ok && response.body?.proposal) {
          applyValidationAvailability(response.body);
          state.proposal = response.body.proposal;
          setRepairState('The proposal was rejected. The flow and source remain unchanged.');
        } else {
          state.error = response.body?.error || response.error || 'The proposal could not be rejected.';
          setRepairState(state.error, true);
        }
      },
      async requestApproval() {
        if (repairDisabled()) return;
        // This records an explicit human review request only. It cannot apply; validation and the
        // single-use approval grant are still required by the broker.
        const id = state.proposal?.proposal?.proposalId || state.proposal?.proposalId;
        if (!id) return;
        if (!browserApprovalAvailable()) {
          state.error = 'Native approval is unavailable in this preview. Browser or chat text cannot issue the broker grant.';
          setRepairState(state.error, true);
          return;
        }
        const response = await inspectorApi.postDetailed(`/api/workbench/repair/${encodeURIComponent(id)}/approve`, {
          reviewer: 'workbench-user',
          humanConfirmed: true,
          policy: 'repair-policy-v1',
        });
        if (response.ok && response.body?.proposal) {
          applyValidationAvailability(response.body);
          state.proposal = response.body.proposal;
          state.proposal.grant = response.body.grant;
          setRepairState('Human approval was recorded. Apply remains a separate explicit action.');
        } else {
          state.error = response.body?.error || response.error ||
            'Approval requires a successful transient validation and a current base flow.';
          setRepairState(state.error, true);
        }
      },
      async validate() {
        if (repairDisabled()) return;
        const id = state.proposal?.proposal?.proposalId || state.proposal?.proposalId;
        if (!id) return;
        if (!browserApprovalAvailable()) {
          state.error = 'Native approval is unavailable in this preview, so transient validation cannot obtain its single-use grant.';
          setRepairState(state.error, true);
          return;
        }
        if (state.validationAvailable === false) {
          state.error = 'Transient validation is unavailable until a lifecycle-capable host is connected.';
          setRepairState(state.error, true);
          return;
        }
        const issued = await inspectorApi.postDetailed('/api/workbench/repair/grant', {
          proposalId: id,
          kind: 'validation',
          reviewer: 'workbench-user',
          humanConfirmed: true,
          policy: 'repair-policy-v1',
        });
        if (!issued.ok || !issued.body?.grant || !issued.body?.proposal) {
          state.error = issued.body?.error || issued.error || 'A human validation grant could not be issued.';
          setRepairState(state.error, true);
          return;
        }
        applyValidationAvailability(issued.body);
        state.proposal = issued.body.proposal;
        const result = await inspectorApi.postDetailed(`/api/workbench/repair/${encodeURIComponent(id)}/validate`, {
          validationGrant: issued.body.grant,
          replaySafety: current().report?.replayEligibility || null,
        });
        if (result.ok && result.body?.proposal) {
          applyValidationAvailability(result.body);
          state.proposal = result.body.proposal;
          setRepairState('Transient validation completed without committing a flow change.', result.body.validation?.passed !== true);
        } else {
          state.error = result.body?.error || result.error ||
            'Transient validation is unavailable until a lifecycle-capable host is connected.';
          setRepairState(state.error, true);
        }
      },
      async apply() {
        if (repairDisabled()) return;
        const runState = runController?.state?.().run?.state || traceController?.state?.().run?.state;
        if (['unknown-completion', 'orphaned'].includes(runState)) {
          state.error = 'Repair is unavailable until the uncertain run completion is resolved.';
          setRepairState(state.error, true);
          return;
        }
        if (!isWriter || !connected || capturedTraceMode) {
          state.error = capturedTraceMode
            ? 'Repair cannot be applied from an imported result. Reproduce the failure locally first.'
            : 'Take control of the connected app before applying a repair.';
          setRepairState(state.error, true);
          return;
        }
        const snapshot = state.proposal;
        const id = snapshot?.proposal?.proposalId || snapshot?.proposalId;
        if (!id || snapshot?.agentOriginated === true || snapshot?.state !== 'approved' || !snapshot?.grant) {
          state.error = 'Apply is unavailable until a human-approved, non-agent-originated proposal has a current grant.';
          setRepairState(state.error, true);
          return;
        }
        const response = await inspectorApi.postDetailed(`/api/workbench/repair/${encodeURIComponent(id)}/apply`, {
          approvalGrant: snapshot.grant,
          policy: 'repair-policy-v1',
        });
        if (response.ok && response.body?.proposal) {
          state.proposal = response.body.proposal;
          setRepairState('The selector-only flow revision was applied. Three clean verification replays are still required.');
        } else {
          state.error = response.body?.error || response.error || 'The approved repair could not be applied.';
          setRepairState(state.error, true);
        }
      },
    });
  }

  function createXamlSourceProposalController() {
    const state = {
      language: 'Xaml',
      csharpSource: null,
      analyzing: false,
      proposing: false,
      proposedAutomationId: '',
      eligibility: null,
      preview: null,
      proposal: null,
      error: null,
    };

    function selectedElement() {
      const current = selectedId ? elById(selectedId) : null;
      const info = elementInfo(current);
      if (!info?.id) return null;
      const integerAttribute = (name) => {
        const value = Number(current.getAttribute(name));
        return Number.isInteger(value) && value > 0 ? value : null;
      };
      return {
        id: info.id,
        type: info.type,
        hasSource: info.hasSource,
        sourceFile: current.getAttribute('data-sourceFile') || null,
        sourceLine: integerAttribute('data-sourceLine'),
        sourceColumn: integerAttribute('data-sourceColumn'),
        sourceHash: current.getAttribute('data-sourceHash') || null,
        sourceConfidence: current.getAttribute('data-sourceConfidence') || null,
      };
    }

    function isCSharp() {
      return state.language === 'CSharp';
    }

    function sourceRoute(path = '') {
      return isCSharp()
        ? `/api/workbench/source/csharp${path}`
        : `/api/workbench/source${path}`;
    }

    function proposalRoute(id, action) {
      return isCSharp()
        ? `/api/workbench/source/csharp/${encodeURIComponent(id)}/${action}`
        : `/api/workbench/source/${encodeURIComponent(id)}/${action}`;
    }

    function capability() {
      return {
        hostKind: hostBridge.hostId(),
        canOpenNativeDiff: hostBridge.has('openSourceDiff'),
        // Embedded hosts sandbox the iframe without `allow-downloads`, so a patch download there
        // would silently do nothing. Only a standalone browser tab can actually deliver the file.
        canDownloadPatch: hostBridge.canDownload(),
      };
    }

    function proposalId() {
      return state.proposal?.proposal?.proposalId || state.proposal?.proposalId || state.preview?.proposalId || null;
    }

    function setSourceState(message, failure = false) {
      const proposal = state.proposal?.proposal || state.proposal || state.preview;
      const source = proposal?.state ||
        (state.analyzing ? 'analyzing' : state.eligibility?.eligible ? 'proposed' : 'unavailable');
      workbenchUpdate({ source }, ['source']);
      if (message) workbenchAnnouncement(message, failure);
    }

    function proposalPayload() {
      const selected = selectedElement();
      const payload = {
        elementId: selected?.id || null,
        proposedAutomationId: state.proposedAutomationId,
        // Flow selector changes are intentionally not included. Source follow-up is advisory only.
        affectedFlows: [],
      };
      if (isCSharp() && state.csharpSource) {
        payload.sourceFile = state.csharpSource.sourceFile;
        payload.sourceLine = state.csharpSource.sourceLine;
        payload.sourceColumn = state.csharpSource.sourceColumn;
        payload.sourceHash = state.csharpSource.sourceHash;
        payload.sourceConfidence = state.csharpSource.sourceConfidence;
      }
      return payload;
    }

    // Source proposals are inert in this layer: nothing here can hand the page a source-writing
    // authority, so a C# proposal needs a C# declaration the runtime source map already proves.
    async function resolveCSharpSource() {
      const selected = selectedElement();
      const mapped = selected?.sourceFile && /\.cs$/i.test(selected.sourceFile) &&
        Number.isInteger(selected.sourceLine) && Number.isInteger(selected.sourceColumn) &&
        typeof selected.sourceHash === 'string' && /^[0-9a-f]{16}$/i.test(selected.sourceHash);
      if (mapped) {
        state.csharpSource = {
          sourceFile: selected.sourceFile,
          sourceLine: selected.sourceLine,
          sourceColumn: selected.sourceColumn,
          sourceHash: selected.sourceHash,
          sourceConfidence: selected.sourceConfidence || 'mapped',
        };
        return true;
      }
      state.error = 'C# source analysis requires a mapped C# runtime declaration. Select a control whose declaration the source map resolved.';
      return false;
    }

    async function refresh() {
      const id = proposalId();
      if (!id) return false;
      const response = await inspectorApi.postDetailed(proposalRoute(id, 'status'), {});
      if (!response.ok || !response.body?.proposal) {
        state.error = response.body?.error || response.error || 'Source proposal status could not be refreshed.';
        setSourceState(state.error, true);
        return false;
      }
      const grant = state.proposal?.grant;
      state.proposal = response.body.proposal;
      if (grant) state.proposal.grant = grant;
      state.error = null;
      setSourceState();
      return true;
    }

    return Object.freeze({
      state: () => ({
        ...state,
        selectedElement: selectedElement(),
        hostCapability: capability(),
        canMutate: isWriter && connected && !capturedTraceMode,
        approvalAvailable:
          runController?.state?.().brokerCapabilities?.browserApprovalAvailable === true,
      }),
      setLanguage(value) {
        const language = value === 'CSharp' ? 'CSharp' : 'Xaml';
        if (state.language === language) return;
        state.language = language;
        state.eligibility = null;
        state.preview = null;
        state.proposal = null;
        state.csharpSource = null;
        state.error = null;
        setSourceState(`${language === 'CSharp' ? 'C#' : 'XAML'} source proposal mode selected. No source has changed.`);
      },
      setProposedAutomationId(value) {
        state.proposedAutomationId = String(value || '').slice(0, 128);
        state.eligibility = null;
        state.preview = null;
        state.proposal = null;
        state.error = null;
      },
      async analyze() {
        if (!selectedElement()?.id || !state.proposedAutomationId.trim()) {
          state.error = `Select a mapped ${isCSharp() ? 'C#' : 'XAML'} element and enter a static AutomationId before analyzing.`;
          setSourceState(state.error, true);
          return;
        }
        if (isCSharp() && !await resolveCSharpSource()) {
          setSourceState(state.error, true);
          return;
        }
        state.analyzing = true;
        state.error = null;
        setSourceState(`Analyzing ${isCSharp() ? 'Roslyn-proven C#' : 'XAML'} source eligibility without writing source.`);
        try {
          const response = await inspectorApi.postDetailed(sourceRoute('/analyze'), proposalPayload());
          state.eligibility = response.body?.eligibility || null;
          state.preview = response.body?.preview || null;
          if (!response.ok || !response.body?.ok) {
            state.error = response.body?.error || response.error || 'The selected declaration is not eligible for a source proposal.';
            setSourceState(state.error, true);
            return;
          }
          setSourceState(`${isCSharp() ? 'C#' : 'XAML'} eligibility passed. Review the exact diff before creating a proposal.`);
        } finally {
          state.analyzing = false;
          setSourceState();
        }
      },
      async propose() {
        if (state.eligibility?.eligible !== true) {
          state.error = `Analyze a currently eligible ${isCSharp() ? 'C#' : 'XAML'} declaration before creating a proposal.`;
          setSourceState(state.error, true);
          return;
        }
        state.proposing = true;
        state.error = null;
        setSourceState(`Creating a reviewed ${isCSharp() ? 'C#' : 'XAML'} source proposal. No source or flow is changed.`);
        try {
          const response = await inspectorApi.postDetailed(sourceRoute('/propose'), proposalPayload());
          if (!response.ok || !response.body?.ok || !response.body?.proposal) {
            state.error = response.body?.error || response.error || `The ${isCSharp() ? 'C#' : 'XAML'} source proposal could not be created.`;
            setSourceState(state.error, true);
            return;
          }
          state.proposal = response.body.proposal;
          state.preview = state.proposal.proposal || null;
          setSourceState('A reviewed source proposal is available. Source approval is distinct from flow repair approval.');
        } finally {
          state.proposing = false;
          setSourceState();
        }
      },
      async preview() {
        const id = proposalId();
        if (!id) return;
        const response = await inspectorApi.postDetailed(proposalRoute(id, 'preview'), {});
        if (!response.ok || !response.body?.proposal) {
          state.error = response.body?.error || response.error || 'Source proposal preview could not be loaded.';
          setSourceState(state.error, true);
          return;
        }
        state.proposal = response.body.proposal;
        setSourceState(`Exact ${isCSharp() ? 'C#' : 'XAML'} diff preview refreshed. No source or flow has changed.`);
      },
      async reject() {
        const id = proposalId();
        if (!id) return;
        const response = await inspectorApi.postDetailed(proposalRoute(id, 'reject'), {
          reviewer: 'workbench-user',
          reasonCode: 'human-rejected',
        });
        if (response.ok && response.body?.proposal) {
          state.proposal = response.body.proposal;
          setSourceState(`Source proposal rejected. ${isCSharp() ? 'C#' : 'XAML'} and flows remain unchanged.`);
        } else {
          state.error = response.body?.error || response.error || 'The source proposal could not be rejected.';
          setSourceState(state.error, true);
        }
      },
      async openNativeDiff() {
        const proposal = state.proposal?.proposal || state.proposal || state.preview;
        if (!proposal?.diff) return;
        const host = await hostBridge.request('openSourceDiff', {
          proposalId: proposal.proposalId,
          fileRelativePath: proposal.operation?.fileRelativePath,
          diff: proposal.diff,
          patchDigest: proposal.patchDigest,
          intent: `Open reviewed ${isCSharp() ? 'C#' : 'XAML'} AutomationId diff`,
        });
        setStatus(host?.ok
          ? (host.message || `Opened the reviewed ${isCSharp() ? 'C#' : 'XAML'} diff in the local host.`)
          : (host?.error || 'A native source diff host is unavailable. Download the patch instead.'));
      },
      downloadPatch() {
        const proposal = state.proposal?.proposal || state.proposal || state.preview;
        if (!proposal?.diff) return;
        triggerBlobDownload(
          new Blob([proposal.diff], { type: 'text/x-diff;charset=utf-8' }),
          `${String(proposal.operation?.fileRelativePath || (isCSharp() ? 'csharp' : 'xaml')).replace(/[\\/]/g, '_')}.patch`);
        setStatus(`Downloaded the reviewed ${isCSharp() ? 'C#' : 'XAML'} patch. Downloading never applies source.`);
      },
      refresh,
    });
  }

  function createImproveController() {
    const maxAmbiguityMatches = 20;
    const state = {
      scanning: false,
      resolvingAmbiguity: false,
      error: null,
      analysis: null,
      inputKey: null,
      includeLiveTree: true,
      liveTree: null,
      ambiguity: null,
      filters: {
        severity: null,
        category: null,
        step: null,
        platform: null,
      },
    };

    function currentInput() {
      const flow = authoringDraft.flow || parseAuthoringFlow(authoringDraft.markdown);
      const plan = authoringDraft.plan || null;
      const currentKey = JSON.stringify({ flow: flow || null, plan });
      return { flow, plan, currentKey };
    }

    function notify() {
      testWorkbench?.updateState({});
    }

    function safeAmbiguityString(value, maximum = 256) {
      if (value == null) return null;
      const result = String(value).replace(/[\u0000-\u001f\u007f]/g, ' ').trim().slice(0, maximum);
      return result || null;
    }

    function safeAmbiguityBounds(value) {
      if (!value || typeof value !== 'object') return null;
      const coordinate = (name) => {
        const number = Number(value[name]);
        return Number.isFinite(number) ? number : null;
      };
      const bounds = {
        x: coordinate('x'),
        y: coordinate('y'),
        width: coordinate('width'),
        height: coordinate('height'),
      };
      return Object.values(bounds).some((value) => value !== null) ? bounds : null;
    }

    function safeAmbiguityMatch(value) {
      const match = value && typeof value === 'object' ? value : {};
      const hasSource = match.hasSource === true;
      const sourceLine = Number(match.sourceLine);
      return {
        id: safeAmbiguityString(match.id),
        type: safeAmbiguityString(match.type, 128),
        role: safeAmbiguityString(match.role, 128),
        automationId: safeAmbiguityString(match.automationId),
        isVisible: match.isVisible === true ? true : match.isVisible === false ? false : null,
        isEnabled: match.isEnabled === true ? true : match.isEnabled === false ? false : null,
        bounds: safeAmbiguityBounds(match.bounds),
        windowBounds: safeAmbiguityBounds(match.windowBounds),
        hasSource,
        sourceLine: hasSource && Number.isInteger(sourceLine) && sourceLine > 0 ? sourceLine : null,
      };
    }

    function safeAmbiguityContext(response, target) {
      const ambiguity = response?.ambiguity || response;
      if (!ambiguity || typeof ambiguity !== 'object') return null;
      const received = Array.isArray(ambiguity.matches) ? ambiguity.matches : [];
      const matches = received.slice(0, maxAmbiguityMatches).map(safeAmbiguityMatch);
      const declared = Number(ambiguity.totalCount);
      const totalCount = Number.isInteger(declared) && declared >= matches.length
        ? declared
        : matches.length;
      return {
        stepId: safeAmbiguityString(target.stepId, 128),
        stepSequence: target.stepSequence,
        selectorKind: selectorKindForAmbiguity(target.selector),
        totalCount,
        truncated: ambiguity.truncated === true || received.length > maxAmbiguityMatches || totalCount > matches.length,
        matches,
      };
    }

    function isAmbiguousFailure(report, step) {
      return [
        report?.failure?.class,
        report?.failure?.code,
        report?.failure?.legacyKind,
        report?.failureKind,
        step?.failureClass,
        step?.failureKind,
        step?.targetResolution?.status,
      ].some((value) => {
        const normalized = String(value || '').trim().toLowerCase();
        return normalized === 'locator-ambiguous' || normalized === 'ambiguous';
      });
    }

    function ambiguityTarget() {
      const trace = traceController?.state?.();
      const report = trace?.report || trace?.run?.report || null;
      const terminal = trace?.run?.terminal === true ||
        report?.outcome?.terminal === true ||
        ['failed', 'cancelled', 'timed-out', 'lease-lost', 'infrastructure-error', 'unknown-completion', 'orphaned']
          .includes(report?.outcome?.status || trace?.run?.state);
      const stepId = report?.failure?.stepId || report?.divergenceStepId ||
        (Array.isArray(report?.steps) ? report.steps.find((step) => step?.failureClass)?.stepId : null);
      const step = Array.isArray(report?.steps)
        ? report.steps.find((candidate) => candidate?.stepId === stepId) || null
        : null;
      const flow = currentInput().flow;
      const stepSequence = Number.isInteger(Number(step?.sequence)) && Number(step.sequence) > 0
        ? Number(step.sequence)
        : sequenceFromStepId(stepId);
      const stepIndex = authoringStepIndexForFailure(flow, stepId, stepSequence);
      const draftStep = stepIndex >= 0 ? flow?.steps?.[stepIndex] : null;
      const selector = activeAuthoringStepSelector(draftStep);
      return { trace, report, terminal, stepId, step, flow, stepSequence, stepIndex, draftStep, selector };
    }

    function isUniqueReturnedAutomationId(context, match) {
      const automationId = safeAmbiguityString(match?.automationId);
      if (!automationId || context?.truncated === true) return false;
      return context.matches.filter((candidate) => candidate.automationId === automationId).length === 1;
    }

    function hasUniqueReturnedAutomationId(context) {
      return context?.matches?.some((match) => isUniqueReturnedAutomationId(context, match)) === true;
    }

    function matchFromContext(match) {
      const context = state.ambiguity;
      if (!context || !match) return null;
      return context.matches.find((candidate) =>
        candidate.id === safeAmbiguityString(match.id) &&
        candidate.automationId === safeAmbiguityString(match.automationId)) || null;
    }

    async function analyze(preserveFilters = false) {
      const current = currentInput();
      if (!current.flow) {
        state.error = 'Load or record a flow before running selector-health analysis.';
        notify();
        return;
      }
      state.scanning = true;
      state.error = null;
      notify();
      try {
        const trace = traceController?.state?.();
        const report = trace?.report || trace?.run?.report || null;
        const response = await inspectorApi.postDetailed('/api/workbench/improve/analyze', {
          flow: current.flow,
          plan: current.plan,
          runHistory: report ? [report] : [],
          includeLiveTree: state.includeLiveTree,
        });
        if (!response.ok || !response.body?.ok || !response.body?.analysis) {
          state.error = response.body?.error || response.error ||
            `Selector-health scan failed (${response.status || 0}).`;
          return;
        }
        state.analysis = response.body.analysis;
        state.liveTree = response.body.liveTree || null;
        state.inputKey = current.currentKey;
        studyController?.improveScanned(Array.isArray(state.analysis.findings) ? state.analysis.findings.length : 0);
        if (!preserveFilters) {
          state.filters = {
            severity: null,
            category: null,
            step: null,
            platform: null,
          };
        }
        setStatus('Deterministic selector-health analysis completed. No selector or source was changed.');
      } finally {
        state.scanning = false;
        notify();
      }
    }

    return Object.freeze({
      state() {
        const current = currentInput();
        return {
          ...state,
          filters: { ...state.filters },
          ambiguity: state.ambiguity
            ? { ...state.ambiguity, matches: state.ambiguity.matches.map((match) => ({ ...match })) }
            : null,
          hasFlow: !!current.flow,
          currentKey: current.currentKey,
          stale: !!state.analysis && state.inputKey !== current.currentKey,
        };
      },
      setFilters(patch) {
        Object.assign(state.filters, patch || {});
        notify();
      },
      setLiveTree(value) {
        state.includeLiveTree = value !== false;
        if (state.analysis) state.inputKey = null;
        notify();
      },
      async analyze() {
        await analyze();
      },
      async resolveAmbiguity({ runAnalysis = true } = {}) {
        const target = ambiguityTarget();
        if (!target.terminal || !isAmbiguousFailure(target.report, target.step)) {
          state.error = 'Resolve matches is available only for a terminal locator-ambiguous result.';
          testWorkbench?.open?.('improve', true);
          notify();
          return { ok: false, error: state.error };
        }
        if (!target.selector || target.stepIndex < 0 || !target.stepSequence) {
          state.error = 'The failed flow step or its active selector could not be mapped to one draft step. The draft was not changed.';
          testWorkbench?.open?.('improve', true);
          notify();
          return { ok: false, error: state.error };
        }

        state.resolvingAmbiguity = true;
        state.error = null;
        notify();
        try {
          const response = await authoringController.verifySelector(target.selector);
          const ambiguity = safeAmbiguityContext(response, target);
          if (!ambiguity || response?.matchCount <= 1) {
            state.error = response?.ok
              ? 'The selector no longer reports multiple matches. DevFlow did not change the draft; inspect the current app state before deciding what to edit.'
              : response?.error || 'The ambiguous selector could not be verified against the current app.';
            testWorkbench?.open?.('improve', true);
            return { ok: false, error: state.error };
          }

          state.ambiguity = ambiguity;
          state.filters.step = String(target.stepSequence);
          testWorkbench?.open?.('improve', true);
          if (runAnalysis) await analyze(true);
          workbenchAnnouncement(
            'Opened safe ambiguity details. DevFlow did not choose a match, change the draft, save the test, or rerun it.'
          );
          return { ok: true, ambiguity };
        } finally {
          state.resolvingAmbiguity = false;
          notify();
        }
      },
      highlightMatch(match) {
        const candidate = matchFromContext(match);
        if (!candidate?.id || !elById(candidate.id)) {
          state.error = 'That ephemeral match is no longer present in the current app frame. Refresh Resolve matches before selecting it.';
          notify();
          return false;
        }
        selectElement(candidate.id);
        workbenchAnnouncement('Highlighted the selected match in the app. No selector or source was changed.');
        return true;
      },
      async useAutomationId(match) {
        const candidate = matchFromContext(match);
        if (!candidate || !isUniqueReturnedAutomationId(state.ambiguity, candidate)) {
          state.error = state.ambiguity?.truncated
            ? 'The match list is truncated, so no returned AutomationId can be used as a safe candidate. Highlight the intended control and re-check it globally.'
            : 'This AutomationId is duplicated or unavailable in the returned matches. DevFlow will not guess.';
          notify();
          return { ok: false, error: state.error };
        }
        const result = await authoringController.applyHumanSelectedSelector({
          stepId: state.ambiguity.stepId,
          stepSequence: state.ambiguity.stepSequence,
          selector: { automationId: candidate.automationId },
        });
        if (!result?.ok) {
          state.error = result?.error || 'The selected AutomationId could not be globally verified. The draft was not changed.';
          notify();
          return result || { ok: false, error: state.error };
        }
        state.ambiguity = null;
        state.error = null;
        workbenchAnnouncement(
          'Only the failed step selector changed in the draft. Save test, then rerun it; DevFlow did not commit or run anything.'
        );
        notify();
        return result;
      },
      improveTestability(match) {
        const candidate = matchFromContext(match);
        if (!candidate?.hasSource || hasUniqueReturnedAutomationId(state.ambiguity)) {
          state.error = 'A reviewed source handoff is available only when no safely unique returned AutomationId exists for a mapped match.';
          notify();
          return false;
        }
        if (!candidate.id || !elById(candidate.id)) {
          state.error = 'That mapped element is no longer present in the current app frame. Refresh Resolve matches before opening Source.';
          notify();
          return false;
        }
        if (testWorkbench?.featureEnabled?.('source') !== true) {
          workbenchAnnouncement(
            'Source preview is disabled. Kept focus on this mapped control; use the safe agent handoff for a read-only testability plan.',
            true
          );
          return false;
        }
        selectElement(candidate.id);
        testWorkbench?.open?.('source', true);
        workbenchAnnouncement(
          'Opened a reviewed source proposal for the selected mapped element. No source, selector, test, or run changed automatically.'
        );
        return true;
      },
      async openSource(source) {
        const anchor = String(source || '').slice(0, 360);
        const result = await hostBridge.request('openSourceDiff', {
          sourceAnchor: anchor,
          intent: 'Open read-only selector-health source anchor',
        });
        setStatus(result?.ok
          ? (result.message || `Opened source anchor ${anchor}.`)
          : `${result?.error || 'Source bridge is unavailable.'} Source anchor: ${anchor}. Improve never writes source.`);
      },
    });
  }

  studyController = createPrototypeStudyController();
  const authoringController = createAuthoringController();
  runController = createRunController();
  traceController = createTraceController();
  repairController = createRepairController();
  sourceProposalController = createXamlSourceProposalController();
  improveController = createImproveController();
  testWorkbench = createInspectorWorkbench({
    root: document.getElementById('df-workbench'),
    toggleButton: document.getElementById('df-toggle-workbench'),
    hostBridge: hostBridge,
    authoring: authoringController,
    run: runController,
    trace: traceController,
    repair: repairController,
    source: sourceProposalController,
    improve: improveController,
    study: studyController,
    agent: inspectorAgent,
    featureCapabilities: previewFeatures,
    getLayout: () => ({
      width: hostLayoutWidth,
      height: hostLayoutHeight,
      overlay: isOverlayChrome(),
    }),
    setStatus,
    copyText,
    onOpen: () => {
      studyController.workbenchOpened();
      setMoreOpen(false);
      if (isTransientPaneLayout()) {
        propertyGrid.close();
        if (isTreeDrawerLayout()) setTreeVisible(false, false, true);
        closeDock();
      }
      syncPaneChrome();
    },
    onClose: syncPaneChrome,
  });
  const workbenchToggle = document.getElementById('df-toggle-workbench');
  if (workbenchToggle)
    workbenchToggle.hidden = !previewFeatures.workbench && !previewFeatures.agentAuthoring;
  if (!previewFeatures.workbench) {
    for (const element of document.querySelectorAll(
      '#df-workbench-stage-goal, #df-workbench-stage-record, #df-workbench-stage-review, ' +
      '#df-workbench-stage-run, #df-workbench-stage-results, #df-workbench-tab-improve'))
      element.hidden = true;
  }
  const repairTab = document.getElementById('df-workbench-tab-repair');
  if (repairTab) repairTab.hidden = !previewFeatures.repair;
  const sourceTab = document.getElementById('df-workbench-tab-source');
  if (sourceTab) sourceTab.hidden = !previewFeatures.source;
  agentRequestController = createAgentRequestController({
    inspectorApi,
    hostBridge,
    openPanel: () => testWorkbench?.open?.('requests', false),
    setStatus,
    copyText,
    agentId: inspectorAgent.id,
    agentInstanceId: inspectorAgent.instanceId,
    workbenchToggle,
    baseWorkbenchAvailable: previewFeatures.workbench || previewFeatures.agentAuthoring,
    requestsArePrimary: !previewFeatures.workbench && previewFeatures.agentAuthoring,
    onAvailabilityChanged: () => testWorkbench?.updateState?.({}, { preservePanel: true }),
    onTransition: (kind, data) => studyController?.agentApprovalTransition?.(kind, data),
  });
  agentRequestController.start();
  const startupAgentRequest = testWorkbench.state().startupHints.agentRequest;
  if (startupAgentRequest) {
    agentRequestController.open(startupAgentRequest);
  }
  window.addEventListener('beforeunload', () => agentRequestController?.stop?.(), { once: true });
  runController.restore();
  if (toggleDockBtn) toggleDockBtn.addEventListener('click', () => (dockEl.classList.contains('df-hidden') ? openDock() : closeDock()));
  if (dockCloseBtn) dockCloseBtn.addEventListener('click', () => closeDock(true));
  if (dockCollapseBtn) dockCollapseBtn.addEventListener('click', toggleDockCollapsed);
  if (dockRefreshBtn) dockRefreshBtn.addEventListener('click', refreshActiveDataView);
  if (attachDataBtn) attachDataBtn.addEventListener('click', attachDockDataToCopilot);
  const dockLayoutTab = document.getElementById('df-tab-layout');
  if (dockLayoutTab) dockLayoutTab.hidden = !inspectorSurfaces.layoutDiagnostics;
  const dockTabButtons = [...dockTabsEl.querySelectorAll('.df-dock-tab')].filter((b) => !b.hidden);
  for (const b of dockTabButtons) {
    b.addEventListener('click', () => {
      const tab = b.getAttribute('data-tab');
      if (tab === 'layout') {
        layoutFocusedElementId = null;
        selectedLayoutFindingId = null;
        layoutSubview = 'findings';
        clearLayoutOverlays();
      }
      if (tab === 'sensors') sensorsView = 'list';
      loadTab(tab);
    });
    b.addEventListener('keydown', (e) => {
      const index = dockTabButtons.indexOf(b);
      let target = null;
      if (e.key === 'ArrowRight') target = dockTabButtons[(index + 1) % dockTabButtons.length];
      else if (e.key === 'ArrowLeft') target = dockTabButtons[(index - 1 + dockTabButtons.length) % dockTabButtons.length];
      else if (e.key === 'Home') target = dockTabButtons[0];
      else if (e.key === 'End') target = dockTabButtons[dockTabButtons.length - 1];
      else return;
      e.preventDefault();
      target.focus();
      const tab = target.getAttribute('data-tab');
      if (tab === 'layout') {
        layoutFocusedElementId = null;
        selectedLayoutFindingId = null;
        layoutSubview = 'findings';
        clearLayoutOverlays();
      }
      if (tab === 'sensors') sensorsView = 'list';
      loadTab(tab);
    });
  }
  setInterval(() => {
    if (networkPollIsActive()) loadNetworkList({ automatic: true, generation: dockViewGeneration });
  }, NETWORK_AUTO_REFRESH_MS);

  setInterval(async () => {
    if (!performancePollIsActive()) return;
    const generation = dockViewGeneration;
    const j = await apiPost('/api/performance/snapshot', {});
    if (dockLoadIsCurrent('performance', generation)) renderPerformance(j);
  }, PERFORMANCE_POLL_MS);

  setInterval(() => {
    if (layoutOptions.live) scheduleLayoutLiveScan(0);
  }, LAYOUT_LIVE_POLL_MS);
  setInterval(async () => {
    if (dockActiveTab !== 'device' ||
        dockEl.classList.contains('df-hidden') ||
        document.hidden ||
        !managedDeviceRecording?.isRecording ||
        managedDeviceBusyAction) return;
    await loadManagedRecordingStatus(managedDevice());
    if (dockActiveTab === 'device') renderDevice(deviceAppSnapshot, deviceHostState);
  }, DEVICE_RECORDING_POLL_MS);

  // ── Presence / single-writer coordination ──
  function renderWriterPresence() {
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
    if (!connected) {
      setPresence('i-refresh', 'Disconnected');
      presence.className = 'df-presence df-disconnected';
      presence.title = 'The running app is disconnected.';
      scheduleIfChanged();
      return;
    }
    if (capturedTraceMode) {
      setPresence('i-lock', 'Captured trace · read-only');
      presence.className = 'df-presence df-readonly';
      presence.title = 'Imported trace mode never takes or uses a mutation lease.';
      scheduleIfChanged();
      return;
    }
    if (isWriter) {
      setPresence('i-edit', 'Driving');
      presence.title = 'This Inspector can drive the app.';
    } else if (leaseHeldByOther) {
      const owner = otherLeaseLabel || 'Another session';
      setPresence('i-lock', `Read-only · ${owner}`);
      const seconds = Number.isFinite(otherLeaseExpiresInMs)
        ? Math.max(0, Math.ceil(otherLeaseExpiresInMs / 1000))
        : null;
      presence.title = `${owner} is driving this app.${seconds == null ? '' : ` Lease refreshes in about ${seconds}s.`}`;
    } else {
      presence.replaceChildren();
      presence.title = 'No session currently controls the app.';
    }
    presence.className = 'df-presence' + (isWriter ? ' df-writer' : (leaseHeldByOther ? ' df-readonly' : ''));
    scheduleIfChanged();
  }

  function setWriterUi(writer, heldByOther, holderLabel, expiresInMs) {
    const lostLease = isWriter && !writer;   // we were driving; another session just took over
    isWriter = !!writer;
    leaseHeldByOther = !!heldByOther;
    otherLeaseLabel = leaseHeldByOther && typeof holderLabel === 'string'
      ? holderLabel.trim().slice(0, 80) || null
      : null;
    otherLeaseExpiresInMs = leaseHeldByOther && expiresInMs != null && Number.isFinite(Number(expiresInMs))
      ? Number(expiresInMs)
      : null;
    const workbenchAuthority = capturedTraceMode
      ? 'read-only'
      : (isWriter ? 'writer' : (lostLease ? 'lease-lost' : 'read-only'));
    if (testWorkbench && testWorkbench.state().authority !== workbenchAuthority)
      testWorkbench.updateState({ authority: workbenchAuthority });
    renderWriterPresence();
    const t = document.getElementById('df-take-control');
    if (t) {
      t.classList.toggle('df-hidden', writer || capturedTraceMode);
      t.title = capturedTraceMode
        ? 'Captured trace mode does not allow lease takeover'
        : heldByOther
        ? `${otherLeaseLabel || 'Another session'} is driving this app; take control`
        : 'No session is driving this app, click to take control';
    }
    // Recording is app-scoped: another valid lease holder can continue it after handoff. This tab
    // becomes a passive observer and can resume the existing recording if it takes control again.
    if (lostLease && recordingId) {
      setStatus('Read-only — recording continues under the session that took control.');
    }
    updateFlowButtons();   // read-only / disconnected re-evaluates the drive-actions
    propertyGrid.updateWriterState();
  }
  async function control(action, force) {
    if (capturedTraceMode && action !== 'status' && action !== 'release') {
      setStatus('Captured trace mode does not take or renew a mutation lease.');
      return null;
    }
    const j = await apiPost('/api/control', force ? { action, force: true } : { action });
    if (j) {
      const wasWriter = isWriter;
      setWriterUi(j.youAreWriter, j.heldByOther, j.label, j.expiresInMs);
      if ((j.youAreWriter && (!wasWriter || recordingId)) || (!j.youAreWriter && recordingId))
        await syncRecordingStatus();
    }
    return j;
  }

  async function setCapturedTraceMode(enabled) {
    const next = enabled === true;
    if (capturedTraceMode === next) return;
    capturedTraceMode = next;
    document.body.classList.toggle('df-captured-trace', next);
    if (next) {
      closeEditor(false);
      setMode('inspect');
      if (isWriter) await control('release');
      else await control('status');
      setStatus('Captured trace mode is read-only. No app interaction, mutation, lease takeover, or replay is available.');
    } else {
      await control('status');
      setStatus('The live run check is separate from the imported result. Take control explicitly before any manual interaction.');
    }
    if (testWorkbench)
      testWorkbench.updateState({ authority: next ? 'read-only' : (isWriter ? 'writer' : 'read-only') });
    updateFlowButtons();
    renderWriterPresence();
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
  if (hostBridge.isEmbedded()) {
    // Give an authenticated embedded host time to supply its shared interaction-session identity.
    // This avoids briefly claiming a random lease that would contend with the host's agent actions.
    setTimeout(() => {
      if (!hostInteractionAdopted) control('claim');
    }, 1000);
  } else {
    control('claim');
  }
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
      const next = e.key === 'Home'
        ? tb.interact
        : (e.key === 'End' ? tb.inspect : (button === tb.interact ? tb.inspect : tb.interact));
      setMode(next === tb.interact ? 'interact' : 'inspect');
      next.focus({ preventScroll: true });
    });
  }
  tb.tree.addEventListener('click', () => {
    setTreeVisible(document.body.classList.contains('df-tree-hidden'));
  });
  tb.bounds.addEventListener('click', () => {
    const on = document.body.classList.toggle('df-show-all');
    tb.bounds.classList.toggle('df-active', on);
    setToolbarToggleState(tb.bounds, on);
  });

  // ── Fit-to-container scaling (responsive) ──────────────────────────────────
  // In hosts smaller than the app's logical size (a Copilot canvas side panel, a short VS Code
  // panel) scale #app-viewport down so the whole app stays visible; #df-stage is sized to the
  // scaled box so centering + scrollbars behave. "Fit" (default) never upscales past 1:1 (keeps the
  // screenshot crisp); toggling it off shows 1:1 actual pixels and lets #df-viewport-wrap scroll.
  function applyScale() {
    const dw = parseFloat(viewport.dataset.width) || viewport.offsetWidth || 1;
    const dh = parseFloat(viewport.dataset.height) || viewport.offsetHeight || 1;
    if (!stage || !vpWrap) return;

    // The substrate. Without a device layer the stage IS the app window, exactly as before. With
    // one, the stage becomes the device SCREEN and the app window is inset within it at its real
    // origin — which is what gives the OS chrome, the soft keyboard, and system dialogs somewhere
    // to exist. The overlay renderer is untouched: elements stay anchored to #app-viewport, which
    // still means the app window.
    const dev = deviceScreenBox();
    const boxW = dev ? dev.width : dw;
    const boxH = dev ? dev.height : dh;

    let s = 1;
    if (fitMode) {
      const availW = vpWrap.clientWidth, availH = vpWrap.clientHeight;
      if (availW > 0 && availH > 0) s = Math.min(availW / boxW, availH / boxH, 1);
      if (!(s > 0) || !isFinite(s)) s = 1;
    }
    viewport.style.transform = s === 1 ? 'none' : ('scale(' + s + ')');
    viewport.style.left = (dev ? dev.originX * s : 0) + 'px';
    viewport.style.top = (dev ? dev.originY * s : 0) + 'px';
    stage.style.width = (boxW * s) + 'px';
    stage.style.height = (boxH * s) + 'px';
    stage.classList.toggle('df-device-stage', !!dev);
    if (dev && dev.cornerRadius > 0) {
      stage.style.borderRadius = (dev.cornerRadius * s) + 'px';
    } else {
      stage.style.borderRadius = '';
    }
    stageScale = s;
  }

  // The device screen in app-logical units, or null when there is no usable device layer. Every
  // field must be present and positive: a partial box would place the app window somewhere
  // plausible and wrong, which is harder to notice than not drawing a device at all.
  function deviceScreenBox() {
    if (!deviceContext) return null;
    const width = Number(deviceContext.screenWidth);
    const height = Number(deviceContext.screenHeight);
    const originX = Number(deviceContext.originX);
    const originY = Number(deviceContext.originY);
    if (!(width > 0) || !(height > 0)) return null;
    if (!isFinite(originX) || !isFinite(originY)) return null;
    return { width, height, originX, originY, cornerRadius: Number(deviceContext.cornerRadius) || 0 };
  }
  function scheduleScale() {
    if (scaleRaf) cancelAnimationFrame(scaleRaf);
    scaleRaf = requestAnimationFrame(() => { scaleRaf = 0; applyScale(); });
  }
  const fitBtn = document.getElementById('df-toggle-fit');
  if (fitBtn) {
    fitBtn.addEventListener('click', () => {
      fitMode = !fitMode;
      fitBtn.classList.toggle('df-active', fitMode);
      fitBtn.setAttribute('aria-pressed', String(fitMode));
      const lbl = fitBtn.querySelector('.df-btn-label'); if (lbl) lbl.textContent = fitMode ? 'Fit' : '1:1';
      applyScale();
    });
  }
  if (window.ResizeObserver && vpWrap) { try { new ResizeObserver(scheduleScale).observe(vpWrap); } catch (_) {} }
  window.addEventListener('resize', () => { updateHostLayout(); scheduleScale(); scheduleToolbarLayout(); });

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

  document.body.dataset.hostKind = hostIdentity;
  document.documentElement.dataset.hostKind = hostIdentity;
  applyHostProfile({ surface: 'browser' });
  updateHostLayout();
  updateLayoutSurfaces();
  scheduleToolbarLayout();
  elementTree.build();
  applyScale();

})();
