// DevFlow Web Inspector — Interaction Script
// Intercepts browser events and proxies them to the native app via the inspector server.
(function () {
  'use strict';

  const viewport = document.getElementById('app-viewport');
  const screenshot = document.getElementById('screenshot');
  const diagnosticsList = document.getElementById('diagnostics-list');
  const diagnosticsSummary = document.getElementById('diagnostics-summary');
  const diagnosticsCoverage = document.getElementById('diagnostics-coverage');
  const diagnosticsFilter = document.getElementById('diagnostics-filter');
  const diagnosticsSeverity = document.getElementById('diagnostics-severity');
  const diagnosticsConfidence = document.getElementById('diagnostics-confidence');
  const diagnosticsRule = document.getElementById('diagnostics-rule');
  const diagnosticsSuppressed = document.getElementById('diagnostics-suppressed');
  const diagnosticOverlays = document.getElementById('diagnostic-overlays');

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
      renderDiagnostics(state.diagnostics, state.rootOffsetX || 0, state.rootOffsetY || 0);
    } catch (err) {
      console.error('State refresh failed:', err);
    } finally {
      refreshInProgress = false;
    }
  }

  let latestDiagnostics = null;
  let latestRootOffsetX = 0;
  let latestRootOffsetY = 0;
  const severityRanks = { info: 0, minor: 1, moderate: 2, serious: 3, critical: 4 };
  const confidenceRanks = { low: 0, medium: 1, high: 2, exact: 3 };

  function renderDiagnostics(diagnostics, rootOffsetX, rootOffsetY) {
    latestDiagnostics = diagnostics || null;
    latestRootOffsetX = rootOffsetX;
    latestRootOffsetY = rootOffsetY;
    if (!diagnosticsList || !diagnosticsSummary || !diagnosticOverlays) return;

    diagnosticsList.replaceChildren();
    diagnosticOverlays.replaceChildren();
    clearDiagnosticHighlights();

    if (!diagnostics) {
      diagnosticsSummary.textContent = 'Unavailable';
      if (diagnosticsCoverage) diagnosticsCoverage.textContent = 'The connected agent does not provide layout diagnostics.';
      return;
    }

    const summary = diagnostics.summary || {};
    diagnosticsSummary.textContent =
      `${summary.violations || 0} violations, ${summary.observations || 0} observations, ` +
      `${summary.incomplete || 0} incomplete, ${summary.passes || 0} passes, ` +
      `${summary.notApplicable || 0} n/a, ${summary.suppressed || 0} suppressed`;
    const limitations = diagnostics.coverage?.limitations || [];
    if (diagnosticsCoverage) {
      diagnosticsCoverage.textContent = limitations.length
        ? limitations.slice(0, 3).join(' ')
        : `Coverage: ${diagnostics.coverage?.overall || 'unknown'}`;
    }

    const outcomeFilter = diagnosticsFilter?.value || 'actionable';
    const severityFilter = diagnosticsSeverity?.value || 'all';
    const confidenceFilter = diagnosticsConfidence?.value || 'all';
    const ruleFilter = (diagnosticsRule?.value || '').trim().toLowerCase();
    const includeSuppressed = diagnosticsSuppressed?.checked === true;
    const findings = (diagnostics.findings || []).filter(finding => {
      if (finding.suppressed && !includeSuppressed) return false;
      if (outcomeFilter === 'incomplete' && finding.outcome !== 'incomplete') return false;
      if (outcomeFilter === 'passes' && finding.outcome !== 'pass') return false;
      if (outcomeFilter === 'actionable' && finding.outcome !== 'violation' && finding.outcome !== 'incomplete') return false;
      if (severityFilter !== 'all' &&
          (severityRanks[finding.severity || 'info'] || 0) < severityRanks[severityFilter]) return false;
      if (confidenceFilter !== 'all' &&
          (confidenceRanks[finding.confidence || 'low'] || 0) < confidenceRanks[confidenceFilter]) return false;
      if (ruleFilter && !(finding.ruleId || '').toLowerCase().includes(ruleFilter)) return false;
      return true;
    });

    for (const finding of findings) {
      const item = document.createElement('div');
      item.className = 'diagnostic-item';
      item.tabIndex = 0;
      item.setAttribute('role', 'button');

      const title = document.createElement('div');
      title.className = 'diagnostic-item-title';
      const rule = document.createElement('span');
      rule.textContent = finding.ruleId || 'layout finding';
      const severity = document.createElement('span');
      severity.className = `severity-${finding.severity || 'info'}`;
      severity.textContent = `${(finding.severity || 'info').toUpperCase()} · ${finding.confidence || 'unknown'}`;
      title.append(rule, severity);

      const message = document.createElement('div');
      message.className = 'diagnostic-item-message';
      message.textContent = finding.message || '';

      const element = document.createElement('div');
      element.className = 'diagnostic-item-element';
      const elementRef = finding.element || {};
      element.textContent = `${elementRef.type || 'Element'}#${elementRef.automationId || elementRef.id || '?'}`;

      item.append(title, message, element);
      addSourceLink(item, elementRef);
      addRelatedElements(item, finding.relatedElements || []);
      addDiagnosticActions(item, finding);
      item.addEventListener('click', () => selectDiagnosticFinding(finding, rootOffsetX, rootOffsetY));
      item.addEventListener('keydown', event => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          selectDiagnosticFinding(finding, rootOffsetX, rootOffsetY);
        }
      });
      diagnosticsList.appendChild(item);
    }
  }

  function selectDiagnosticFinding(finding, rootOffsetX, rootOffsetY) {
    diagnosticOverlays.replaceChildren();
    clearDiagnosticHighlights();
    highlightElement(finding.element?.id, 'diagnostic-selected', true);
    for (const related of finding.relatedElements || [])
      highlightElement(related.element?.id, 'diagnostic-related-highlight', false);

    addDiagnosticRegion(finding.evidence?.fullRegion, 'diagnostic-region-full', rootOffsetX, rootOffsetY);
    addDiagnosticRegion(finding.evidence?.visibleRegion, 'diagnostic-region-visible', rootOffsetX, rootOffsetY);
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

  function addSourceLink(item, element) {
    if (!element?.sourceFile) return;
    const link = document.createElement('a');
    link.className = 'diagnostic-source';
    const normalized = element.sourceFile.replace(/\\/g, '/');
    link.href = `vscode://file/${encodeURI(normalized)}:${element.sourceLine || 1}:${element.sourceColumn || 1}`;
    link.textContent = `Source ${element.sourceLine || '?'}`;
    link.addEventListener('click', event => event.stopPropagation());
    item.appendChild(link);
  }

  function addRelatedElements(item, relatedElements) {
    if (!relatedElements.length) return;
    const container = document.createElement('div');
    container.className = 'diagnostic-related';
    for (const related of relatedElements) {
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = `${related.relation}: ${related.element?.automationId || related.element?.id || '?'}`;
      button.addEventListener('click', event => {
        event.stopPropagation();
        clearDiagnosticHighlights();
        highlightElement(related.element?.id, 'diagnostic-related-highlight', true);
      });
      container.appendChild(button);
    }
    item.appendChild(container);
  }

  function addDiagnosticActions(item, finding) {
    const actions = document.createElement('div');
    actions.className = 'diagnostic-item-actions';
    const suppress = document.createElement('button');
    suppress.type = 'button';
    suppress.textContent = finding.suppressed ? 'Unsuppress' : 'Suppress';
    suppress.addEventListener('click', async event => {
      event.stopPropagation();
      const endpoint = finding.suppressed ? 'unsuppress' : 'suppress';
      const response = await fetch(`${basePath}/api/diagnostics/${endpoint}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ findingId: finding.id })
      });
      if (!response.ok) {
        const error = await response.json().catch(() => null);
        suppress.textContent = 'Policy conflict';
        suppress.title = error?.message || 'The suppression policy could not be changed.';
        return;
      }
      scheduleRefresh(50);
    });

    const copy = document.createElement('button');
    copy.type = 'button';
    copy.textContent = 'Copy agent payload';
    copy.addEventListener('click', async event => {
      event.stopPropagation();
      const response = await fetch(`${basePath}/api/diagnostics/agent-payload`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ findingId: finding.id })
      });
      if (!response.ok) return;
      const text = await response.text();
      await navigator.clipboard?.writeText(text);
    });
    actions.append(suppress, copy);
    item.appendChild(actions);
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
      target.scrollIntoView({ block: 'center', inline: 'center', behavior: 'smooth' });
  }

  for (const control of [
    diagnosticsFilter,
    diagnosticsSeverity,
    diagnosticsConfidence,
    diagnosticsSuppressed
  ]) {
    control?.addEventListener('change', () =>
      renderDiagnostics(latestDiagnostics, latestRootOffsetX, latestRootOffsetY));
  }
  diagnosticsRule?.addEventListener('input', () =>
    renderDiagnostics(latestDiagnostics, latestRootOffsetX, latestRootOffsetY));

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
    // Heuristic: traits often expose "TextInput" / "Editable"
    const traits = (el.dataset.traits || '').toLowerCase();
    return traits.includes('textinput') || traits.includes('editable');
  }

  // Overlay editor that we float on top of the clicked text element.
  let activeEditor = null;
  function closeEditor(commit) {
    if (!activeEditor) return;
    const editor = activeEditor;
    activeEditor = null;
    if (commit) {
      const elementId = editor.dataset.elementId;
      const text = editor.value;
      fetch(`${basePath}/api/fill`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ elementId, text }),
      }).then(() => scheduleRefresh(300)).catch(err => console.error('Fill failed:', err));
    }
    editor.remove();
  }

  function openEditor(targetEl) {
    closeEditor(false);
    const elementId = targetEl.getAttribute('data-id');
    if (!elementId) return;

    const rect = targetEl.getBoundingClientRect();
    const vpRect = viewport.getBoundingClientRect();
    const isMultiline = ['Editor', 'TextArea', 'TextView', 'UITextView'].includes(targetEl.dataset.type || '');
    const editor = document.createElement(isMultiline ? 'textarea' : 'input');
    if (!isMultiline) editor.type = 'text';
    editor.value = targetEl.dataset.text || targetEl.dataset.value || '';
    editor.dataset.elementId = elementId;
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

  function connectEventStream() {
    const scheme = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const socket = new WebSocket(`${scheme}//${location.host}${basePath}/ws/events`);
    socket.addEventListener('message', event => {
      try {
        const message = JSON.parse(event.data);
        if (message.type === 'layout-diagnostics-delta') {
          applyDiagnosticsDelta(message);
          scheduleRefresh(250);
          return;
        }
      } catch {
      }
      scheduleRefresh(100);
    });
    socket.addEventListener('close', () => {
      if (!document.hidden) setTimeout(connectEventStream, 1000);
    });
    socket.addEventListener('error', () => socket.close());
  }

  function applyDiagnosticsDelta(delta) {
    if (!latestDiagnostics) {
      scheduleRefresh(50);
      return;
    }
    const renderedTreeRevision = latestDiagnostics.snapshot?.treeRevision || '';
    const deltaTreeRevision = delta.snapshot?.treeRevision || '';
    if (renderedTreeRevision && deltaTreeRevision &&
        renderedTreeRevision !== deltaTreeRevision) {
      scheduleRefresh(50);
      return;
    }

    const findings = new Map(
      (latestDiagnostics.findings || []).map(finding => [finding.id, finding]));
    for (const id of delta.removed || [])
      findings.delete(id);
    for (const finding of [...(delta.added || []), ...(delta.updated || [])])
      findings.set(finding.id, finding);

    latestDiagnostics = {
      ...latestDiagnostics,
      snapshot: delta.snapshot || latestDiagnostics.snapshot,
      summary: delta.summary || latestDiagnostics.summary,
      coverage: delta.coverage || latestDiagnostics.coverage,
      findings: [...findings.values()]
    };
    renderDiagnostics(
      latestDiagnostics,
      latestRootOffsetX,
      latestRootOffsetY);
  }

  refreshState();
  connectEventStream();
})();
