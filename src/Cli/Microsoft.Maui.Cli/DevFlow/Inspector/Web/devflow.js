// DevFlow Web Inspector — Interaction Script
// Composition root: coordinates browser events, app transport, recording, hosts, and feature modules.
import { createInspectorApi } from './inspector-api.js';
import { confirmModal } from './inspector-dialog.js';
import { createDataSnapshot, isSecretContextKey, supportsDataContextScope } from './inspector-data-context.js';
import { createPropertyGridController } from './inspector-properties.js';
import { createElementTreeController } from './inspector-tree.js';

(function () {
  'use strict';

  const viewport = document.getElementById('app-viewport');
  const screenshot = document.getElementById('screenshot');
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
  // Per-inspector read token for data tabs, injected into the page by InspectorServer. Same-origin
  // only — a cross-origin page can't set this custom header without a preflight the broker refuses.
  const inspectorToken = (document.querySelector('meta[name="devflow-inspector-token"]') || {}).content || '';
  const inspectorApi = createInspectorApi(basePath, inspectorToken);
  const apiPost = inspectorApi.post;
  function metaContent(name) {
    const meta = document.querySelector(`meta[name="${name}"]`);
    return meta && typeof meta.content === 'string' && meta.content ? meta.content : null;
  }
  const inspectorAgent = Object.freeze({
    id: metaContent('devflow-agent-id'),
    appName: metaContent('devflow-app-name'),
    platform: metaContent('devflow-platform'),
    port: Number(metaContent('devflow-agent-port')) || null,
  });
  // A per-tab writer token identifies this session for the single-writer lock. A global fetch
  // wrapper stamps it on every same-origin /api/ call and flips to read-only on a writer 409.
  const writerToken = (window.crypto && crypto.randomUUID) ? crypto.randomUUID() : ('w' + Math.random().toString(36).slice(2) + Date.now());
  let leaseHolderKind = 'web';
  let leaseHolderLabel = 'Browser Inspector';
  let isWriter = false;
  let leaseHeldByOther = false;
  let otherLeaseLabel = null;
  let otherLeaseExpiresInMs = null;
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
  // Replay + checkpoint (return-to-start-route) state.
  let lastMarkdown = null;
  let lastMarkdownName = null;
  let lastMarkdownSource = null;
  let checkpointRoute = null;
  let checkpointLabel = null;
  let replaying = false;

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

  // Refresh state via AJAX (no full page reload — avoids flash)
  async function refreshState() {
    if (refreshInProgress) return;
    refreshInProgress = true;
    try {
      const resp = await fetch(`${basePath}/api/state`);
      if (!resp.ok) { markConnected(false); return; }
      const state = await resp.json();
      markConnected(true);

      // Update screenshot without flash
      if (screenshot && state.screenshotUrl) {
        screenshot.src = state.screenshotUrl;
      }

      // Update viewport size if changed
      if (state.viewportWidth && state.viewportHeight) {
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
      if (state.elements) {
        patchElements(state.elements);
        onElementsUpdated();
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
    } else if (disconnectReturnFocus && disconnectReturnFocus.isConnected) {
      const active = document.activeElement;
      if (!active || active === document.body || active === document.documentElement ||
          disconnectedOverlay?.contains(active)) {
        disconnectReturnFocus.focus({ preventScroll: true });
      }
      disconnectReturnFocus = null;
    }
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
      fetch(`${basePath}/api/fill`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ elementId, text }),
      }).then((resp) => {
        if (recordingId && resp && resp.ok) recordStepById('fill', elementId, { value: text });
        scheduleRefresh(300);
      }).catch(err => console.error('Fill failed:', err));
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
    if (!ensureCanDrive()) return;

    let textEl = underCursor;
    while (textEl && textEl !== viewport && !isTextInput(textEl)) textEl = textEl.parentElement;
    if (textEl && textEl !== viewport && isTextInput(textEl)) {
      // Still send a tap so the native control gets focus on the app side.
      const { x: tx, y: ty } = toAppCoords(e.clientX, e.clientY);
      fetch(`${basePath}/api/tap`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ x: tx, y: ty }),
      }).catch(err => console.error('Tap failed:', err));
      openEditor(textEl);
      return;
    }

    // Capture the tap target BEFORE driving (a navigation can destroy it) so a recorded step
    // carries the durable selector of the element the user targeted. We drive by coordinate (works
    // for any element, interactive or not) and record the hit element — replay resolves by selector.
    const tapEl = underCursor && underCursor.closest ? underCursor.closest('.devflow-element') : null;
    const { x, y } = toAppCoords(e.clientX, e.clientY);

    try {
      const resp = await fetch(`${basePath}/api/tap`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ x, y })
      });
      if (recordingId) {
        if (tapEl && resp.ok) await recordStep('tap', tapEl);
        else if (!tapEl) setStatus('Tap not recorded — no element under the cursor.');
      }
      scheduleRefresh(400);
    } catch (err) {
      console.error('Tap failed:', err);
    }
  });

  // ── Wheel → Scroll ──
  let scrollAccumX = 0, scrollAccumY = 0;
  let scrollFlushTimer = null;
  let lastScrollX = 0, lastScrollY = 0;

  viewport.addEventListener('wheel', (e) => {
    e.preventDefault();
    if (!ensureCanDrive()) return;
    scrollAccumX += e.deltaX;
    scrollAccumY += e.deltaY;
    lastScrollX = e.clientX;
    lastScrollY = e.clientY;

    if (scrollFlushTimer) clearTimeout(scrollFlushTimer);
    scrollFlushTimer = setTimeout(async () => {
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
        if (recordingId && resp.ok) recordStep('scroll', null, { dx, dy });
        scheduleRefresh(300);
      } catch (err) {
        console.error('Scroll failed:', err);
      }
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
      eventsWs.onmessage = () => {
        if (!document.hidden && !replaying) scheduleRefresh(150);
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
    getIsWriter: () => isWriter && connected,
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
    more: document.getElementById('df-more'),
    secondary: document.getElementById('df-toolbar-secondary'),
    overflow: document.getElementById('df-toolbar-overflow'),
    status: document.getElementById('df-status'),
  };
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
      ? [...tb.overflow.querySelectorAll(':scope > button:not(:disabled)')]
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
    const toolbar = document.getElementById('df-toolbar');
    return !!toolbar && toolbar.scrollWidth <= toolbar.clientWidth + 1;
  }

  function layoutToolbarActions() {
    toolbarLayoutFrame = 0;
    if (!tb.secondary || !tb.overflow || !tb.more) return;
    const focused = document.activeElement;
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
      (hostLayout !== 'wide' && dockOpen);
    document.body.classList.toggle('df-props-open', propsOpen);
    if (paneScrim) paneScrim.classList.toggle('df-hidden', !showScrim);
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
    if (hoveredEl) hoveredEl.classList.remove('df-hover', 'df-hover-noninteractive');
    hoveredEl = el;
    if (!el) { if (badgeEl) badgeEl.style.display = 'none'; return; }
    el.classList.add('df-hover');
    const interactable = isInteractableOverlay(el);
    el.classList.toggle('df-hover-noninteractive', !interactable);
    const b = ensureBadge();
    b.classList.toggle('df-badge-noninteractive', !interactable);
    const w = Math.round(parseFloat(el.style.width) || el.offsetWidth);
    const h = Math.round(parseFloat(el.style.height) || el.offsetHeight);
    b.textContent = (el.getAttribute('data-type') || 'Element') + ' ' + w + '×' + h;
    b.style.left = el.offsetLeft + 'px';
    b.style.top = Math.max(0, el.offsetTop - 18) + 'px';
    b.style.display = 'block';
  }
  function isInteractableOverlay(el) {
    if (!el || !el.classList || !el.classList.contains('devflow-element')) return false;
    return el.dataset.interactable === 'true';
  }
  function hoverHitTest(clientX, clientY) {
    const nodes = mode === 'interact'
      ? document.elementsFromPoint(clientX, clientY)
      : [document.elementFromPoint(clientX, clientY)];
    const seen = new Set();
    let nonInteractiveFallback = null;
    for (const node of nodes) {
      const overlay = (node && node.closest) ? node.closest('.devflow-element') : null;
      if (!overlay || seen.has(overlay)) continue;
      seen.add(overlay);
      if (mode !== 'interact') return overlay;
      if (isInteractableOverlay(overlay)) return overlay;
      nonInteractiveFallback ??= overlay;
    }
    return nonInteractiveFallback;
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
    if (!id) { propertyGrid.close(); elementTree.updateSelection(); setStatus(''); updateHostButtons(); updateFlowButtons(); postSelectionToHost(null); return; }
    const el = elById(id);
    if (el) el.classList.add('df-selected');
    elementTree.updateSelection();
    elementTree.reveal(id);
    if (el) { propertyGrid.open(el); setStatus(elementLabel(el)); }
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
      if (el) el.classList.add('df-selected'); else selectElement(null);
    }
    if (hoveredEl && !hoveredEl.isConnected) { hoveredEl = null; if (badgeEl) badgeEl.style.display = 'none'; }
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
    recordBtn.classList.toggle('df-recording', !!recordingId);
    recordBtn.setAttribute('aria-pressed', String(!!recordingId));
    // Update only the label span so the leading icon and the responsive icon-collapse survive
    // (setting textContent here would wipe the .df-btn-label wrapper the toolbar relies on).
    const lbl = recordBtn.querySelector('.df-btn-label');
    const label = recordingStopping ? 'Stopping…' : (recordingId ? `Rec (${recStepCount})` : 'Record');
    if (lbl) lbl.textContent = label;
    else recordBtn.textContent = recordingId ? `\u25CF ${label}` : `\u25CF ${label}`;
    if (cancelRecordingBtn) cancelRecordingBtn.classList.toggle('df-hidden', !recordingId);
    scheduleToolbarLayout();
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
        if (hostHas('saveRecording') && postToHost('devflow:recordingComplete', { name: j.name, steps: j.steps, markdown: j.markdown })) {
          setStatus(`${pre}Recorded ${j.steps} step(s) — handed to the host to save. Replay is now available.`);
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
      workflowSelect.disabled = false;
      return;
    }
    if (result.supported === false) {
      workflowSelect.append(new Option('Project unavailable — choose a file', ''));
      workflowSelect.disabled = false;
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
    workflowSelect.disabled = false;
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
    // canDrive: this session holds the writer lease AND a live app is connected. The drive-actions
    // (record / replay / assert / return-to-start-route) 409 or fail otherwise, so disable them
    // rather than let a click error out. Record stays clickable WHILE recording so you can stop.
    const canDrive = isWriter && connected;
    updateRecordButton();
    if (recordBtn) recordBtn.disabled = recordingStopping || replaying || !canDrive;
    if (cancelRecordingBtn) cancelRecordingBtn.disabled = !recordingId || recordingStopping || replaying || !canDrive;
    setExplainedDisabled(assertBtn, !recordingId || !selectedId || replaying || !canDrive);
    setExplainedDisabled(replayBtn, !lastMarkdown || !!recordingId || replaying || !canDrive);
    setExplainedDisabled(checkpointBtn, !checkpointRoute || replaying || !canDrive);
    if (loadFlowBtn) loadFlowBtn.disabled = !!recordingId || replaying;
    if (workflowSelect) workflowSelect.disabled = !!recordingId || replaying;
    if (workflowFileBtn) workflowFileBtn.disabled = !!recordingId || replaying;
    if (workflowReplayBtn) workflowReplayBtn.disabled = !lastMarkdown || !!recordingId || replaying || !canDrive;
    propertyGrid.updateWriterState();
    document.querySelectorAll('#df-dock .df-alert-actions button').forEach((button) => {
      button.disabled = !canDrive;
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
    else if (!on && !pollInterval) { pollInterval = setInterval(() => { if (!refreshTimer && !wsLive) refreshState(); }, 3000); }
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
      scheduleRefresh(500);
    } catch (err) { setStatus('Navigate failed.'); }
  }

  async function replay() {
    if (!lastMarkdown || recordingId || replaying) return;
    const workflowLabel = lastMarkdownName ? ` “${lastMarkdownName}”` : '';
    if (!(await confirmModal(`Replay${workflowLabel} will drive the LIVE app and may change its data. Continue?`, 'Replay'))) return;
    // Auto-capture a "before replay" checkpoint so you can return to where you were.
    await captureCheckpoint('before replay');
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
    finally { setReplayUi(false); scheduleRefresh(400); }
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
  updateFlowButtons();

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
  let dockSnapshot = null;
  let dockActiveTab = null;
  const pendingHostRequests = new Map();
  let hostRequestSequence = 0;

  function postToHost(type, data) {
    if (!framed || !bridgeId) return false;
    window.parent.postMessage(Object.assign({ v: 1, bridgeId, type }, data || {}), '*');
    return true;
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
    if (++hsTries < 12) hsTimer = setTimeout(announceReady, 300);
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
      if (d.theme) applyTheme(d.theme);                  // host may bundle its theme with the capability ack
    } else if (d.type === 'devflow:theme') {
      if (d.profile) applyHostProfile(d.profile);
      applyTheme(d);                                     // host reports/updates its color scheme + palette
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
    setExplainedDisabled(sourceBtn, !(el && el.getAttribute('data-hasSource') === 'true'));
    const dataAvailable = !!dockSnapshot && supportsDataContextScope(dockSnapshot.scope);
    setExplainedDisabled(copilotBtn, !(el || lastMarkdown || dataAvailable));
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
      if (!j || !j.ok || !j.file) { setStatus('No source available for this element.'); return; }
      if (hostHas('openSource') && postToHost('devflow:openSource', {
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
      if (hostHas('copilotContext')) {
        setStatus(`Adding ${kind === 'combined' ? 'selection and workflow' : kind} context to Copilot…`);
        const result = await requestHost('devflow:attachCopilot', { context: kind, payload }, 10000);
        setStatus(result && result.ok
          ? (result.message || 'Added Inspector context to Copilot.')
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
        button.disabled = !isWriter || !connected;
        button.title = button.disabled ? 'Take control before dismissing native alerts.' : `Dismiss with ${label}`;
        button.addEventListener('click', async () => {
          if (!ensureCanDrive()) return;
          button.disabled = true;
          const response = await apiPost('/api/alerts/dismiss', { buttonLabel: label });
          if (response && response.ok && response.dismissed) {
            setStatus(`Dismissed native alert with ${label}.`);
            await loadTab('alerts');
          } else {
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
    dockEmpty('Loading…');
    setDockMeta('loading…');
    try {
      await tabLoaders[name](generation);
      if (!dockLoadIsCurrent(name, generation)) return;
      setDockMeta((name === 'network' ? 'live · updated ' : 'captured ') + new Date().toLocaleTimeString());
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
    syncPaneChrome();
  }
  function closeDock(restore = false) {
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
    if (dockCollapseBtn) {
      dockCollapseBtn.setAttribute('aria-expanded', String(!collapsed));
      dockCollapseBtn.title = collapsed ? 'Expand data panel' : 'Collapse data panel';
    }
  }
  if (toggleDockBtn) toggleDockBtn.addEventListener('click', () => (dockEl.classList.contains('df-hidden') ? openDock() : closeDock()));
  if (dockCloseBtn) dockCloseBtn.addEventListener('click', () => closeDock(true));
  if (dockCollapseBtn) dockCollapseBtn.addEventListener('click', toggleDockCollapsed);
  if (dockRefreshBtn) dockRefreshBtn.addEventListener('click', () => {
    if (dockActiveTab === 'network' && networkDetailId) loadNetworkDetail(networkDetailId);
    else loadTab(dockActiveTab);
  });
  if (attachDataBtn) attachDataBtn.addEventListener('click', attachDockDataToCopilot);
  const dockTabButtons = [...dockTabsEl.querySelectorAll('.df-dock-tab')];
  for (const b of dockTabButtons) {
    b.addEventListener('click', () => loadTab(b.getAttribute('data-tab')));
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
      loadTab(target.getAttribute('data-tab'));
    });
  }
  setInterval(() => {
    if (networkPollIsActive()) loadNetworkList({ automatic: true, generation: dockViewGeneration });
  }, NETWORK_AUTO_REFRESH_MS);

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
    renderWriterPresence();
    const t = document.getElementById('df-take-control');
    if (t) {
      t.classList.toggle('df-hidden', writer);
      t.title = heldByOther
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
    const j = await apiPost('/api/control', force ? { action, force: true } : { action });
    if (j) {
      const wasWriter = isWriter;
      setWriterUi(j.youAreWriter, j.heldByOther, j.label, j.expiresInMs);
      if ((j.youAreWriter && (!wasWriter || recordingId)) || (!j.youAreWriter && recordingId))
        await syncRecordingStatus();
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
    tb.bounds.setAttribute('aria-pressed', String(on));
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
    let s = 1;
    if (fitMode) {
      const availW = vpWrap.clientWidth, availH = vpWrap.clientHeight;
      if (availW > 0 && availH > 0) s = Math.min(availW / dw, availH / dh, 1);
      if (!(s > 0) || !isFinite(s)) s = 1;
    }
    viewport.style.transform = s === 1 ? 'none' : ('scale(' + s + ')');
    stage.style.width = (dw * s) + 'px';
    stage.style.height = (dh * s) + 'px';
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

  document.body.dataset.hostKind = hostKind;
  document.documentElement.dataset.hostKind = hostKind;
  updateHostLayout();
  scheduleToolbarLayout();
  elementTree.build();
  applyScale();

})();
