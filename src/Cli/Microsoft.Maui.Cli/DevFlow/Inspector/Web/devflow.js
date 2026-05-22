// DevFlow Web Inspector — Interaction Script
// Intercepts browser events and proxies them to the native app via the inspector server.
(function () {
  'use strict';

  const viewport = document.getElementById('app-viewport');
  const screenshot = document.getElementById('screenshot');
  const btnBack = document.getElementById('btn-back');
  const btnRefresh = document.getElementById('btn-refresh');
  const statusEl = document.getElementById('connection-status');

  let gesturePoints = [];
  let isGesturing = false;
  let isDragging = false;

  // ── Click → Tap ──
  viewport.addEventListener('click', async (e) => {
    if (isDragging) return; // don't tap if we just finished a drag
    if (e.target.closest('#devflow-toolbar')) return;

    const rect = viewport.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;

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
    const rect = viewport.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;

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
    if (e.target.closest('#devflow-toolbar')) return;
    gesturePoints = [{ x: e.offsetX, y: e.offsetY, t: Date.now() }];
    isGesturing = true;
    isDragging = false;
    viewport.setPointerCapture(e.pointerId);
  });

  viewport.addEventListener('pointermove', (e) => {
    if (!isGesturing) return;
    gesturePoints.push({ x: e.offsetX, y: e.offsetY, t: Date.now() });
    if (gesturePoints.length > 3) isDragging = true;
  });

  viewport.addEventListener('pointerup', async (e) => {
    if (!isGesturing) return;
    isGesturing = false;

    // Only send gesture if there was meaningful movement (> 20px)
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
    // Reset isDragging after a short delay so the click handler can check it
    setTimeout(() => { isDragging = false; }, 50);
  });

  // ── Toolbar: Back ──
  btnBack.addEventListener('click', async (e) => {
    e.stopPropagation();
    try {
      await fetch('/api/back', { method: 'POST' });
      await refreshPage();
    } catch (err) {
      console.error('Back failed:', err);
    }
  });

  // ── Toolbar: Refresh ──
  btnRefresh.addEventListener('click', async (e) => {
    e.stopPropagation();
    await refreshPage();
  });

  // ── Screenshot refresh ──
  async function refreshScreenshot() {
    // Wait for app to settle
    await sleep(100);
    if (screenshot) {
      screenshot.src = '/screenshot.png?t=' + Date.now();
    }
  }

  async function refreshPage() {
    location.reload();
  }

  function sleep(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
  }

  // ── WebSocket for live updates ──
  function connectWebSocket() {
    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(`${protocol}//${location.host}/ws/events`);

    ws.onopen = () => {
      statusEl.textContent = '● Connected';
      statusEl.style.color = '#4ec9b0';
    };

    ws.onmessage = (e) => {
      try {
        const event = JSON.parse(e.data);
        if (event.type === 'treeChange' || event.type === 'navigation') {
          // Debounce rapid updates
          clearTimeout(ws._refreshTimer);
          ws._refreshTimer = setTimeout(() => refreshPage(), 200);
        }
      } catch { }
    };

    ws.onclose = () => {
      statusEl.textContent = '○ Disconnected';
      statusEl.style.color = '#f44747';
      setTimeout(connectWebSocket, 2000);
    };

    ws.onerror = () => {
      ws.close();
    };
  }

  connectWebSocket();
})();
