// shell.mjs — the Canvas panel embeds the shared DevFlow Web Inspector, so the Canvas and VS Code
// hosts expose the same visual tree, screenshot, interactions, property grid, and record/replay UI.
// The broker hosts it per-agent at http://localhost:{brokerPort}/inspector/{agentId}/. When no
// broker/agent is resolved yet, `renderDisconnected` (below) is the runtime fallback — a lightweight
// status shell in the same hybrid --df-* token language that self-heals via /inspector-ready polling.
//
// The nonce'd relay <script> is the canvas end of the authenticated host bridge. The canvas can't
// open VS Code Chat or an editor, so it advertises only `saveRecording`: when the inspector finishes
// a recording it hands the Markdown here and we POST it to the canvas server (/recording), which
// writes it into the project's maui-tests/ folder. Send-to-Copilot and open-source fall back to the
// inspector's own clipboard behavior. The bridge nonce rides in the iframe URL *fragment*, so it
// never reaches the broker over HTTP; every message in both directions is gated by it + event.source.

const UI_FONT_STACK = '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", sans-serif';

export function renderShell(inspectorUrl, appName, bridgeId) {
  const title = escapeHtml(appName || "MAUI app");
  const nonce = String(bridgeId || "").replace(/[^A-Za-z0-9_-]/g, "");
  const frameSrc = jsString(`${inspectorUrl}#devflowBridge=${nonce}`);
  const bridgeLiteral = jsString(nonce);
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>${title}</title>
  <meta http-equiv="Content-Security-Policy"
        content="default-src 'none'; frame-src http://127.0.0.1:* http://localhost:* https://*.vscode-webview.net; style-src 'unsafe-inline'; connect-src 'self'; script-src 'nonce-${nonce}';" />
  <style>
    /* Fill the whole canvas panel: no host chrome (the panel tab already names it), no default padding. */
    :root { color-scheme: light dark; }
    html, body { margin: 0; padding: 0; height: 100%; overflow: hidden; background: light-dark(#ffffff, #1e1e1e);
                 font: 13px/1.5 ${UI_FONT_STACK}; }
    #frame { position: fixed; inset: 0; width: 100%; height: 100%; border: 0; display: block; }
  </style>
</head>
<body>
  <iframe id="frame" sandbox="allow-scripts allow-forms allow-same-origin"></iframe>
  <script nonce="${nonce}">
    (function () {
      const frame = document.getElementById('frame');
      const bridgeId = ${bridgeLiteral};
      // Capabilities the canvas contributes: save recordings, receive the human's selection (so the
      // agent can answer about "the selected element"), and push that selection to Copilot as context.
      const capabilities = bridgeId ? ['saveRecording', 'selection', 'copilot', 'copilotContext', 'attachData'] : [];
      // Relay a control action to the canvas server (which updates the agent-facing selection store).
      function postControl(payload, cb) {
        try {
          fetch('/control', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(Object.assign({ bridgeId: bridgeId }, payload)) })
            .then(function (r) {
              if (!r || !r.ok) return { ok: false, error: 'The Canvas host rejected the DevFlow request.' };
              return r.json().catch(function () { return { ok: false, error: 'The Canvas host returned an invalid response.' }; });
            })
            .then(function (j) { if (cb) cb(j); })
            .catch(function () { if (cb) cb({ ok: false, error: 'The Canvas host did not respond.' }); });
        } catch (e) { if (cb) cb({ ok: false, error: 'The Canvas host could not send the request.' }); }
      }
      // The Copilot canvas is a Chromium surface that follows the host app's light/dark setting via
      // prefers-color-scheme. Forward that as an explicit mode so the inspector themes correctly even
      // though prefers-color-scheme doesn't reliably cross the iframe's origin boundary.
      function themeMode() {
        try { return (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) ? 'dark' : 'light'; }
        catch (e) { return 'dark'; }
      }
      // Literal GitHub Primer palette — the last-resort
      // fallback whenever a --fgColor-*/--bgColor-*/--borderColor-* Primer variable isn't defined on
      // the shell document (older/newer Canvas host build). Every value is a plain hex/rgba string —
      // safe to ship through the theme.palette bridge (the shared inspector re-validates it anyway).
      const PRIMER_FALLBACK = {
        light: {
          '--df-bg': '#ffffff', '--df-surface': '#f6f8fa', '--df-surface-2': '#eaeef2', '--df-fg': '#1f2328',
          '--df-muted': '#656d76', '--df-border': '#d0d7de', '--df-border-subtle': '#d8dee4',
          '--df-hover': '#eaeef2', '--df-hover-row': '#eaeef2', '--df-accent': '#0969da', '--df-accent-fg': '#ffffff',
          '--df-selected': 'rgba(9,105,218,.10)', '--df-selected-fg': '#1f2328', '--df-danger': '#cf222e',
          '--df-focus': '#0969da', '--df-warn': '#9a6700', '--df-error': '#cf222e',
          '--df-type': '#0969da', '--df-name': '#8250df', '--df-source': '#9a6700', '--df-success': '#1a7f37',
          '--df-outline-hover': '#0969da', '--df-outline-select': '#0969da'
        },
        dark: {
          '--df-bg': '#0d1117', '--df-surface': '#161b22', '--df-surface-2': '#12161d', '--df-fg': '#e6edf3',
          '--df-muted': '#8b949e', '--df-border': '#30363d', '--df-border-subtle': '#292e36',
          '--df-hover': '#1c2230', '--df-hover-row': '#1c2230', '--df-accent': '#2f81f7', '--df-accent-fg': '#ffffff',
          '--df-selected': 'rgba(47,129,247,.18)', '--df-selected-fg': '#e6edf3', '--df-danger': '#f85149',
          '--df-focus': '#2f81f7', '--df-warn': '#d29922', '--df-error': '#f85149',
          '--df-type': '#2f81f7', '--df-name': '#a371f7', '--df-source': '#d29922', '--df-success': '#3fb950',
          '--df-outline-hover': '#2f81f7', '--df-outline-select': '#2f81f7'
        }
      };
      // Best-effort Primer/Copilot CSS custom-property names for each shared df-* token — the canvas
      // is a GitHub Primer surface, so these commonly resolve. getComputedStyle() picks whichever the
      // host document actually defines; PRIMER_FALLBACK covers the rest.
      const PRIMER_MAP = {
        '--df-bg': ['--bgColor-default'], '--df-surface': ['--bgColor-muted'], '--df-surface-2': ['--bgColor-inset'],
        '--df-fg': ['--fgColor-default'], '--df-muted': ['--fgColor-muted'],
        '--df-border': ['--borderColor-default'], '--df-border-subtle': ['--borderColor-muted'],
        '--df-hover': ['--bgColor-neutral-muted'], '--df-hover-row': ['--bgColor-neutral-muted'],
        '--df-accent': ['--bgColor-accent-emphasis'], '--df-accent-fg': ['--fgColor-onEmphasis'],
        '--df-selected': ['--bgColor-accent-muted'], '--df-selected-fg': ['--fgColor-default'],
        '--df-danger': ['--fgColor-danger'], '--df-focus': ['--focus-outlineColor'],
        '--df-warn': ['--fgColor-attention'], '--df-error': ['--fgColor-danger'],
        '--df-type': ['--fgColor-accent'], '--df-name': ['--fgColor-done', '--fgColor-accent'],
        '--df-source': ['--fgColor-attention'], '--df-success': ['--fgColor-success'],
        '--df-outline-hover': ['--focus-outlineColor'], '--df-outline-select': ['--borderColor-accent-emphasis', '--fgColor-accent']
      };
      function resolveColor(value) {
        if (!value || !document.body) return '';
        try {
          const probe = document.createElement('span');
          probe.style.position = 'absolute';
          probe.style.visibility = 'hidden';
          probe.style.color = value;
          document.body.appendChild(probe);
          const color = getComputedStyle(probe).color;
          probe.remove();
          return color || '';
        } catch (e) { return ''; }
      }
      function buildPalette(mode) {
        const fallback = PRIMER_FALLBACK[mode] || PRIMER_FALLBACK.dark;
        const out = {};
        try {
          const cs = getComputedStyle(document.documentElement);
          for (const key in PRIMER_MAP) {
            let v = '';
            for (const cand of PRIMER_MAP[key]) { v = cs.getPropertyValue(cand).trim(); if (v) break; }
            out[key] = resolveColor(v) || fallback[key];
          }
        } catch (e) { return Object.assign({}, fallback); }
        return out;
      }
      function buildTheme() {
        const mode = themeMode();
        return { mode: mode, palette: buildPalette(mode) };
      }
      function prefersHighContrast() {
        try {
          return !!(window.matchMedia &&
            (window.matchMedia('(prefers-contrast: more)').matches ||
             window.matchMedia('(forced-colors: active)').matches));
        }
        catch (e) { return false; }
      }
      function prefersReducedMotion() {
        try { return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches); }
        catch (e) { return false; }
      }
      function safeFontFamily(v) {
        const s = typeof v === 'string' ? v.trim() : '';
        if (!s || s.length > 120 || !/^[A-Za-z0-9 ,'"_.\-]+$/.test(s)) return undefined;
        return s;
      }
      function safeFontSize(v) {
        const s = typeof v === 'string' ? v.trim() : '';
        return /^[0-9]{1,3}(\.[0-9]+)?px$/.test(s) ? s : undefined;
      }
      function buildFont() {
        // Host font metadata only — never routed through theme.palette (that channel is colors-only).
        try {
          const cs = getComputedStyle(document.body);
          const family = safeFontFamily(cs.fontFamily);
          const size = safeFontSize(cs.fontSize);
          const out = {};
          if (family) out.family = family;
          if (size) out.size = size;
          return Object.keys(out).length ? out : undefined;
        } catch (e) { return undefined; }
      }
      function buildProfile() {
        const profile = { surface: 'side-panel' };
        if (prefersHighContrast()) profile.contrast = 'high';
        if (prefersReducedMotion()) profile.reducedMotion = true;
        const font = buildFont();
        if (font) profile.font = font;
        return profile;
      }
      function announce() {
        try {
          if (frame.contentWindow && bridgeId) {
            frame.contentWindow.postMessage({ type: 'devflow:host', v: 1, bridgeId: bridgeId, capabilities: capabilities, hostKind: 'copilot-canvas-ui', hostLabel: 'GitHub Copilot Canvas', theme: buildTheme(), profile: buildProfile() }, '*');
          }
        } catch (e) { /* cross-origin during teardown */ }
      }
      function sendTheme() {
        try {
          if (frame.contentWindow && bridgeId) {
            frame.contentWindow.postMessage(Object.assign({ type: 'devflow:theme', v: 1, bridgeId: bridgeId, profile: buildProfile() }, buildTheme()), '*');
          }
        } catch (e) { /* cross-origin during teardown */ }
      }
      // Announce capabilities ONLY in response to the inspector's nonce-authenticated
      // 'devflow:ready' — never unconditionally on iframe 'load', so the bridge secret can't leak to
      // a page the iframe later navigates to.
      window.addEventListener('message', function (e) {
        if (e.source !== frame.contentWindow) return;           // only our embedded inspector
        const d = e.data;
        if (!d || !bridgeId || d.bridgeId !== bridgeId) return;  // nonce-authenticated
        if (d.type === 'devflow:ready') { announce(); return; }
        if (d.type === 'devflow:selectionChanged') {
          // Mirror the human's inspector selection into the extension's store so get_selection /
          // get_canvas (and "fix the selected element") resolve to the right control.
          const el = d.element;
          postControl(el && el.id ? { action: 'select', id: el.id } : { action: 'select', id: null });
          return;
        }
        if (d.type === 'devflow:sendToCopilot') {
          // Ensure the store selection matches, then push it into the composer as a context pill.
          // Pass the element itself so the server can attach it even if the store select missed.
          const pel = d.payload && d.payload.element;
          if (pel && pel.id) postControl({ action: 'select', id: pel.id }, function () { postControl({ action: 'attachSelection', element: pel }); });
          else postControl({ action: 'attachSelection' });
          return;
        }
        if (d.type === 'devflow:attachCopilot') {
          postControl({ action: 'attachCopilot', context: d.context, payload: d.payload }, function (result) {
            if (!frame.contentWindow || !d.requestId) return;
            frame.contentWindow.postMessage({
              type: 'devflow:hostResult',
              v: 1,
              bridgeId: bridgeId,
              requestId: d.requestId,
              ok: !!(result && result.ok),
              message: result && result.status ? String(result.status) : null,
              error: result && result.error ? String(result.error) : null,
            }, '*');
          });
          return;
        }
        if (d.type === 'devflow:attachData') {
          postControl({ action: 'attachData', snapshot: d.snapshot }, function (result) {
            if (!frame.contentWindow || !d.requestId) return;
            frame.contentWindow.postMessage({
              type: 'devflow:hostResult',
              v: 1,
              bridgeId: bridgeId,
              requestId: d.requestId,
              ok: !!(result && result.ok),
              message: result && result.status ? String(result.status) : null,
              error: result && result.error ? String(result.error) : null,
            }, '*');
          });
          return;
        }
        if (d.type === 'devflow:recordingComplete') {
          fetch('/recording', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            // Carry the bridge nonce so the server can reject cross-site localhost POSTs.
            body: JSON.stringify({ bridgeId: bridgeId, name: d.name, markdown: d.markdown }),
          }).catch(function () { /* best effort */ });
        }
      });
      // Attach the listener before navigating so no early 'ready' is lost.
      // Re-push mode/profile when the host app toggles light/dark, contrast, or reduced-motion.
      try {
        ['(prefers-color-scheme: dark)', '(prefers-contrast: more)', '(forced-colors: active)', '(prefers-reduced-motion: reduce)'].forEach(function (q) {
          const mq = window.matchMedia(q);
          if (mq && mq.addEventListener) mq.addEventListener('change', sendTheme);
          else if (mq && mq.addListener) mq.addListener(sendTheme);
        });
      } catch (e) { /* matchMedia is available in the canvas webview */ }
      frame.src = ${frameSrc};
    })();
  </script>
</body>
</html>`;
}

// renderDisconnected — the runtime fallback shown when no broker/agent has resolved yet (panel
// opened mid-(re)connect, app not yet launched, broker restarting, …). This small, dependency-free
// status shell speaks the SAME
// hybrid --df-* token language as the shared inspector and the two host shells (light/dark only —
// there is no embedded inspector document to hand a Primer palette to yet), so the panel doesn't
// visually jar once it converges to the real inspector. It polls /inspector-ready and reloads into
// the shared inspector the moment it resolves.
export function renderDisconnected(appName, nonce) {
  const title = escapeHtml(appName || "MAUI app");
  const safeNonce = String(nonce || "").replace(/[^A-Za-z0-9_-]/g, "") || "df";
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>${title}</title>
  <meta http-equiv="Content-Security-Policy"
        content="default-src 'none'; style-src 'unsafe-inline'; connect-src 'self'; script-src 'nonce-${safeNonce}';" />
  <style>
    :root {
      color-scheme: light dark;
      --df-bg: light-dark(#ffffff, #0d1117); --df-surface: light-dark(#f6f8fa, #161b22);
      --df-fg: light-dark(#1f2328, #e6edf3); --df-muted: light-dark(#656d76, #8b949e);
      --df-border: light-dark(#d0d7de, #30363d); --df-accent: light-dark(#0969da, #2f81f7);
      --df-warn: light-dark(#9a6700, #d29922);
    }
    * { box-sizing: border-box; }
    html, body { margin: 0; padding: 0; height: 100%; background: var(--df-bg); color: var(--df-fg);
                 font: 13px/1.5 ${UI_FONT_STACK}; }
    main { height: 100%; display: flex; flex-direction: column; align-items: center; justify-content: center;
           gap: 12px; padding: 24px; text-align: center; }
    .card { max-width: 360px; padding: 20px 24px; border: 1px solid var(--df-border); border-radius: 10px;
            background: var(--df-surface); }
    .title { font-weight: 600; margin: 0 0 4px; }
    .status { color: var(--df-muted); margin: 0 0 14px; }
    .spinner { width: 22px; height: 22px; margin: 0 auto 14px; border-radius: 50%;
               border: 2px solid var(--df-border); border-top-color: var(--df-accent);
               animation: df-spin 0.9s linear infinite; }
    @media (prefers-reduced-motion: reduce) { .spinner { animation: none; border-top-color: var(--df-warn); } }
    @keyframes df-spin { to { transform: rotate(360deg); } }
  </style>
</head>
<body>
  <main>
    <div class="card">
      <div class="spinner" role="presentation"></div>
      <p class="title">${title}</p>
      <p class="status" id="df-status">Waiting for the MAUI DevFlow agent to connect…</p>
    </div>
  </main>
  <script nonce="${safeNonce}">
    (function () {
      // Self-heal: poll /inspector-ready and reload into the shared Inspector as soon as a broker
      // and running app resolve. The 5s guard stops a
      // flapping broker from hot-looping reloads.
      var statusEl = document.getElementById('df-status');
      function setStatus(text) { if (statusEl) statusEl.textContent = text; }
      async function heal() {
        try {
          const r = await fetch('/inspector-ready', { cache: 'no-store' });
          const j = await r.json();
          if (j && j.ready) {
            const last = +sessionStorage.getItem('df_healAt') || 0;
            if (Date.now() - last > 5000) {
              sessionStorage.setItem('df_healAt', String(Date.now()));
              setStatus('Connected — opening the inspector…');
              location.reload();
            }
          } else {
            setStatus('Waiting for the MAUI DevFlow agent to connect…');
          }
        } catch (e) { setStatus('Waiting for the MAUI DevFlow broker…'); }
      }
      heal();
      setInterval(heal, 2500);
    })();
  </script>
</body>
</html>`;
}

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

function jsString(s) {
  // Safe string literal for inlining into the <script>: JSON-encode, then neutralize `<`.
  return JSON.stringify(String(s)).replace(/</g, "\\u003c");
}
