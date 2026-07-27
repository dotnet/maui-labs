// DevFlow Web Inspector — Interaction Script
// Intercepts browser events and proxies them to the native app via the inspector server.
(function () {
  'use strict';

  const viewport = document.getElementById('app-viewport');
  const screenshot = document.getElementById('screenshot');

  // Determine base path for API calls (handles being served under /inspector/{id}/)
  const basePath = location.pathname.replace(/\/$/, '');

  let gesturePoints = [];
  let isGesturing = false;
  let isDragging = false;
  let refreshPromise = null;

  // Convert browser coordinates to app logical coordinates
  function toAppCoords(clientX, clientY) {
    const rect = viewport.getBoundingClientRect();
    return { x: clientX - rect.left, y: clientY - rect.top };
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

  // Refresh state via AJAX (no full page reload — avoids flash)
  async function refreshState(force = false) {
    if (refreshPromise) {
      if (!force) return refreshPromise;
      await refreshPromise;
    }

    const currentRefresh = (async () => {
      try {
        const resp = await fetch(`${basePath}/api/state`);
        if (!resp.ok) return null;
        const state = await resp.json();

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
        }

        // Smart DOM diff — only update elements that changed, preserving hover/selection
        if (state.elements) {
          patchElements(state.elements);
        }
        return state;
      } catch (err) {
        console.error('State refresh failed:', err);
        return null;
      }
    })();
    refreshPromise = currentRefresh;
    try {
      return await currentRefresh;
    } finally {
      if (refreshPromise === currentRefresh) refreshPromise = null;
    }
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
  function scheduleRefresh(delayMs) {
    if (refreshTimer) clearTimeout(refreshTimer);
    refreshTimer = setTimeout(() => {
      refreshTimer = null;
      refreshState();
    }, delayMs || 300);
  }

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
    const capabilities = (el.dataset.capabilities || '').toLowerCase().split(',');
    if (capabilities.includes('set-value')) return true;
    // Heuristic: traits often expose "TextInput" / "Editable"
    const traits = (el.dataset.traits || '').toLowerCase();
    return traits.includes('textinput') || traits.includes('editable');
  }

  // Overlay editor that we float on top of the clicked text element.
  let activeEditor = null;

  function removeEditor(editor) {
    if (activeEditor === editor) activeEditor = null;
    editor.remove();
  }

  function preserveEditorAfterFailure(editor, status) {
    delete editor.dataset.commitInProgress;
    editor.readOnly = false;
    editor.setAttribute('aria-invalid', 'true');
    editor.style.borderColor = '#d13438';
    editor.title = status
      ? `Unable to update the app (${status}). Your text has been preserved.`
      : 'Unable to update the app. Your text has been preserved.';
    if (activeEditor === editor) {
      setTimeout(() => editor.focus(), 0);
    }
  }

  async function commitEditor(editor, retryStaleCapture) {
    try {
      const response = await fetch(`${basePath}/api/fill`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          elementId: editor.dataset.elementId,
          text: editor.value,
          ...getElementCaptureMetadata(editor),
        }),
      });

      if (response.ok) {
        removeEditor(editor);
        scheduleRefresh(300);
        return;
      }

      if (retryStaleCapture && response.status === 409) {
        await refreshState(true);
        const currentTarget = findElementById(editor.dataset.elementId);
        if (currentTarget
            && isTextInput(currentTarget)
            && matchesEditorTarget(editor, currentTarget)) {
          copyElementCaptureMetadata(currentTarget, editor);
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
      if (editor.dataset.commitInProgress === 'true') return;
      editor.dataset.commitInProgress = 'true';
      editor.readOnly = true;
      commitEditor(editor, true);
      return;
    }
    removeEditor(editor);
  }

  function openEditor(targetEl) {
    if (activeEditor?.dataset.commitInProgress === 'true') return;
    closeEditor(false);
    const elementId = targetEl.getAttribute('data-id');
    if (!elementId) return;

    const rect = targetEl.getBoundingClientRect();
    const vpRect = viewport.getBoundingClientRect();
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
      left: (rect.left - vpRect.left) + 'px',
      top: (rect.top - vpRect.top) + 'px',
      width: rect.width + 'px',
      height: rect.height + 'px',
      zIndex: '10000',
      background: 'rgba(255,255,255,0.97)',
      color: '#000',
      border: '2px solid #4ec9b0',
      borderRadius: '2px',
      padding: '2px 4px',
      font: 'inherit',
      fontSize: Math.max(11, Math.min(20, rect.height * 0.5)) + 'px',
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
    let textEl = underCursor;
    while (textEl && textEl !== viewport && !isTextInput(textEl)) textEl = textEl.parentElement;
    if (textEl && textEl !== viewport && isTextInput(textEl)) {
      openEditor(textEl);
      return;
    }

    const { x, y } = toAppCoords(e.clientX, e.clientY);

    try {
      await fetch(`${basePath}/api/tap`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ x, y })
      });
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
        await fetch(`${basePath}/api/scroll`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ x, y, deltaX: dx, deltaY: dy })
        });
        scheduleRefresh(300);
      } catch (err) {
        console.error('Scroll failed:', err);
      }
    }, 100);
  }, { passive: false });

  // ── Pointer Drag → Gesture ──
  viewport.addEventListener('pointerdown', (e) => {
    const { x, y } = toAppCoords(e.clientX, e.clientY);
    gesturePoints = [{ x, y, t: Date.now() }];
    isGesturing = true;
    isDragging = false;
    viewport.setPointerCapture(e.pointerId);
  });

  viewport.addEventListener('pointermove', (e) => {
    if (!isGesturing) return;
    const { x, y } = toAppCoords(e.clientX, e.clientY);
    gesturePoints.push({ x, y, t: Date.now() });
    if (gesturePoints.length > 3) isDragging = true;
  });

  viewport.addEventListener('pointerup', async (e) => {
    if (!isGesturing) return;
    isGesturing = false;

    if (gesturePoints.length >= 2) {
      const first = gesturePoints[0];
      const last = gesturePoints[gesturePoints.length - 1];
      const dist = Math.sqrt(Math.pow(last.x - first.x, 2) + Math.pow(last.y - first.y, 2));

      if (dist > 20) {
        try {
          await fetch(`${basePath}/api/gesture`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ points: gesturePoints })
          });
          scheduleRefresh(300);
        } catch (err) {
          console.error('Gesture failed:', err);
        }
      }
    }

    gesturePoints = [];
    setTimeout(() => { isDragging = false; }, 50);
  });

  // ── Periodic refresh for app-side changes (AJAX, no flash) ──
  let pollInterval = setInterval(() => {
    if (!document.hidden && !refreshTimer) {
      refreshState();
    }
  }, 3000);

  document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
      clearInterval(pollInterval);
      pollInterval = null;
    } else if (!pollInterval) {
      pollInterval = setInterval(() => {
        if (!refreshTimer) refreshState();
      }, 3000);
    }
  });
})();
