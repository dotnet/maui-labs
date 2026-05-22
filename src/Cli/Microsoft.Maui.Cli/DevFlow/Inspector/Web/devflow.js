// DevFlow Web Inspector — Interaction Script
// Intercepts browser events and proxies them to the native app via the inspector server.
(function () {
  'use strict';

  const viewport = document.getElementById('app-viewport');
  const screenshot = document.getElementById('screenshot');

  let gesturePoints = [];
  let isGesturing = false;
  let isDragging = false;
  let currentScale = 1;

  // ── Zoom to fit ──
  function zoomToFit() {
    const appW = parseFloat(viewport.dataset.width) || viewport.offsetWidth;
    const appH = parseFloat(viewport.dataset.height) || viewport.offsetHeight;
    const winW = window.innerWidth;
    const winH = window.innerHeight;

    const scaleX = winW / appW;
    const scaleY = winH / appH;
    currentScale = Math.min(scaleX, scaleY, 1); // never upscale

    viewport.style.transform = `scale(${currentScale})`;
  }

  zoomToFit();
  window.addEventListener('resize', zoomToFit);

  // Convert browser coordinates to app logical coordinates (accounting for zoom)
  function toAppCoords(clientX, clientY) {
    const rect = viewport.getBoundingClientRect();
    const x = (clientX - rect.left) / currentScale;
    const y = (clientY - rect.top) / currentScale;
    return { x, y };
  }

  // ── Click → Tap ──
  viewport.addEventListener('click', async (e) => {
    if (isDragging) return;

    const { x, y } = toAppCoords(e.clientX, e.clientY);

    try {
      await fetch('/api/tap', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ x, y })
      });
      await refreshScreenshot();
    } catch (err) {
      console.error('Tap failed:', err);
    }
  });

  // ── Wheel → Scroll ──
  viewport.addEventListener('wheel', async (e) => {
    e.preventDefault();
    const { x, y } = toAppCoords(e.clientX, e.clientY);

    try {
      await fetch('/api/scroll', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ x, y, deltaX: e.deltaX, deltaY: e.deltaY })
      });
      await refreshScreenshot();
    } catch (err) {
      console.error('Scroll failed:', err);
    }
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
          await fetch('/api/gesture', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ points: gesturePoints })
          });
          await refreshScreenshot();
        } catch (err) {
          console.error('Gesture failed:', err);
        }
      }
    }

    gesturePoints = [];
    setTimeout(() => { isDragging = false; }, 50);
  });

  // ── Screenshot refresh ──
  async function refreshScreenshot() {
    await sleep(100);
    if (screenshot) {
      screenshot.src = '/screenshot.png?t=' + Date.now();
    }
  }

  function sleep(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
  }

  // ── WebSocket for live updates ──
  function connectWebSocket() {
    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(`${protocol}//${location.host}/ws/events`);

    ws.onmessage = (e) => {
      try {
        const event = JSON.parse(e.data);
        if (event.type === 'treeChange' || event.type === 'navigation') {
          clearTimeout(ws._refreshTimer);
          ws._refreshTimer = setTimeout(() => location.reload(), 200);
        }
      } catch { }
    };

    ws.onclose = () => {
      setTimeout(connectWebSocket, 2000);
    };

    ws.onerror = () => {
      ws.close();
    };
  }

  connectWebSocket();
})();
