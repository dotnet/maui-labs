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
  let refreshInProgress = false;

  // Convert browser coordinates to app logical coordinates
  function toAppCoords(clientX, clientY) {
    const rect = viewport.getBoundingClientRect();
    return { x: clientX - rect.left, y: clientY - rect.top };
  }

  // Refresh state via AJAX (no full page reload — avoids flash)
  async function refreshState() {
    if (refreshInProgress) return;
    refreshInProgress = true;
    try {
      const resp = await fetch(`${basePath}/api/state`);
      if (!resp.ok) return;
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
    } catch (err) {
      console.error('State refresh failed:', err);
    } finally {
      refreshInProgress = false;
    }
  }

  // Keyed DOM diff: match elements by data-id, update in-place if changed
  function patchElements(newHtml) {
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

  // ── Click → Tap ──
  viewport.addEventListener('click', async (e) => {
    if (isDragging) return;

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
