// store.mjs — the live bridge between the running MAUI app and the canvas surface.
//
// The DevFlow CLI is async (each call spawns a process), but the canvas UI and its SSE stream
// want a cheap synchronous snapshot. So the store keeps a cached snapshot that it serves
// instantly, and every mutation shells out, then re-pulls tree + screenshot and emits the new
// snapshot to all subscribers (browser via SSE, and the agent via capability return values).

import { DevflowDevice } from "./devflow.mjs";
import { readFile } from "node:fs/promises";

function nowISO() {
  return new Date().toISOString();
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// FNV-1a over raw bytes — a cheap fingerprint of a screenshot PNG so we can tell
// when the rendered frame has actually stopped changing (theme flip has settled).
function hashBytes(buf) {
  let acc = 2166136261;
  for (let i = 0; i < buf.length; i++) {
    acc ^= buf[i];
    acc = Math.imul(acc, 16777619);
  }
  return acc >>> 0;
}

// Derive a coarse platform name from a TFM like "net10.0-android" when the broker
// registration doesn't carry an explicit platform field.
function platformFromTfm(tfm) {
  const s = String(tfm || "").toLowerCase();
  if (s.includes("android")) return "android";
  if (s.includes("maccatalyst")) return "maccatalyst";
  if (s.includes("ios")) return "ios";
  if (s.includes("macos")) return "macos";
  if (s.includes("windows")) return "windows";
  if (s.includes("tizen")) return "tizen";
  return null;
}

function numberOrZero(v) {
  const n = Number(v);
  return Number.isFinite(n) ? n : 0;
}

function hasPositiveSize(b) {
  return b && b.width > 0 && b.height > 0;
}

function intersectsWindow(b, window) {
  if (!window || !(window.width > 0) || !(window.height > 0)) return true;
  return b.x + b.width > 0 && b.y + b.height > 0 && b.x < window.width && b.y < window.height;
}

function isDisplayed(el) {
  return el?.isVisible !== false && el?.state?.displayed !== false && Number(el?.opacity ?? 1) !== 0;
}

function toRect(b, ax = 0, ay = 0) {
  return {
    x: ax + numberOrZero(b?.x),
    y: ay + numberOrZero(b?.y),
    width: numberOrZero(b?.width),
    height: numberOrZero(b?.height),
  };
}

// Window-space rect for a node: the agent's `windowBounds` is already absolute; a plain
// `bounds` accumulates from the parent origin. Mirrors the logic inside renderedRoots' walk.
function absOf(el, ax, ay) {
  return el.windowBounds ? toRect(el.windowBounds) : toRect(el.bounds, ax, ay);
}

function isDescendantOf(id, ancestorId, parent) {
  let cur = parent.get(id);
  while (cur != null) {
    if (cur === ancestorId) return true;
    cur = parent.get(cur);
  }
  return false;
}

// MAUI Shell keeps EVERY tab/flyout page mounted and marked visible, so a single tree pull
// contains all pages at once — the inactive ones with their content collapsed to the origin
// (0,0) but still `isVisible=true` with page-sized bounds. Rendering that verbatim makes the
// visual tree + hit-test overlay "stick" on a stale page after navigation and stacks
// hit-targets from pages that aren't on screen. This returns the set of node ids to DROP
// (inactive pages + their Shell wrappers) so the canvas reflects only what's displayed.
//
// Active-page detection, most reliable first:
//   1. the selected Tab/FlyoutItem (state.selected) whose id encodes the target page type,
//   2. otherwise the uniquely most-populated page (real content laid out in the viewport).
// If it's genuinely ambiguous we suppress NOTHING (fail safe → show everything, never hide
// the page the user is actually looking at).
function inactivePageIds(roots, window) {
  if (process.env.MAUI_CANVAS_NO_PAGE_FILTER) return new Set();

  const flat = [];
  const parent = new Map();
  const byId = new Map();
  const abs = new Map();
  const kids = new Map();
  const visit = (el, pid, ax, ay) => {
    if (!el) return;
    flat.push(el);
    parent.set(el.id, pid);
    byId.set(el.id, el);
    if (!kids.has(pid)) kids.set(pid, []);
    kids.get(pid).push(el);
    const b = absOf(el, ax, ay);
    abs.set(el.id, b);
    for (const c of el.children || []) visit(c, el.id, b.x, b.y);
  };
  for (const r of roots || []) visit(r, null, 0, 0);

  const isDeclaredPage = (e) =>
    String(e.role || "").toLowerCase() === "page" || /Page$/.test(String(e.type || ""));
  const hasPageShape = (e) => {
    const b = abs.get(e.id);
    return !!(b && b.width > 1 && b.height > 1 && (kids.get(e.id) || []).length);
  };
  const declaredPages = flat.filter((e) => e.isVisible !== false && isDeclaredPage(e));
  const hasExplicitCurrentPage = declaredPages.some(
    (page) => page.state?.selected === true || page.isSelected === true
  );
  const legacyPageParents = new Set(
    hasExplicitCurrentPage
      ? []
      : declaredPages.map((page) => parent.get(page.id)).filter((parentId) => parentId != null)
  );
  const pages = flat.filter(
    (e) =>
      e.isVisible !== false &&
      (isDeclaredPage(e) || (legacyPageParents.has(parent.get(e.id)) && hasPageShape(e)))
  );
  if (pages.length <= 1) return new Set();

  // Viewport body band: below the nav bar, above the tab bar (fall back to window fractions).
  const H = window && window.height > 0 ? window.height : 0;
  let navBottom = 0;
  let tabTop = Infinity;
  for (const e of flat) {
    const t = String(e.type || "");
    const b = abs.get(e.id) || {};
    if (/^(NavBar|Toolbar)/.test(t) && b.height > 0) navBottom = Math.max(navBottom, b.y + b.height);
    if (t === "Tab" && b.height > 0) tabTop = Math.min(tabTop, b.y);
  }
  if (!(tabTop < Infinity)) tabTop = H ? H * 0.9 : Infinity;
  if (!(navBottom > 0)) navBottom = H ? H * 0.1 : 0;

  // score(page) = visible, positive-size descendants actually laid out in the viewport body
  // (not collapsed to the origin). Collapsed inactive pages score ~0.
  const score = (page) => {
    let n = 0;
    const stack = [...(kids.get(page.id) || [])];
    while (stack.length) {
      const el = stack.pop();
      for (const c of kids.get(el.id) || []) stack.push(c);
      if (el.isVisible === false) continue;
      const b = abs.get(el.id);
      if (!b || !(b.width > 1) || !(b.height > 1)) continue;
      const atOrigin = Math.abs(b.x) < 2 && Math.abs(b.y) < 2;
      const cy = b.y + b.height / 2;
      if (!atOrigin && cy > navBottom && cy < tabTop) n++;
    }
    return n;
  };
  const scoreById = new Map(pages.map((p) => [p.id, score(p)]));

  // Selected nav items (Tab / FlyoutItem) whose id encodes the target page type.
  const norm = (s) => String(s || "").toLowerCase().replace(/[^a-z0-9]/g, "");
  const selectedNav = flat.filter(
    (e) => (e.state?.selected === true || e.isFocused === true) && /tab|flyout/i.test(String(e.type || ""))
  );
  const tabMatchLen = (page) => {
    const pt = norm(page.type);
    if (!pt) return 0;
    let best = 0;
    for (const nav of selectedNav) if (norm(nav.id).includes(pt)) best = Math.max(best, pt.length);
    return best;
  };

  // Pick the active page.
  let active = null;
  let confident = false;
  const explicitlyActive = pages
    .filter((page) => page.state?.selected === true || page.isSelected === true)
    .sort((a, b) => (scoreById.get(b.id) || 0) - (scoreById.get(a.id) || 0));
  if (explicitlyActive.length) {
    active = explicitlyActive[0];
    confident = true;
  } else {
    const tabbed = pages
      .map((p) => ({ p, len: tabMatchLen(p), s: scoreById.get(p.id) || 0 }))
      .filter((x) => x.len > 0)
      .sort((a, b) => b.len - a.len || b.s - a.s);
    if (tabbed.length && tabbed[0].s > 0) {
      active = tabbed[0].p;
      confident = true; // the selected Tab/FlyoutItem authoritatively names the on-screen page
    } else {
      const sorted = [...pages].sort((a, b) => (scoreById.get(b.id) || 0) - (scoreById.get(a.id) || 0));
      const top = scoreById.get(sorted[0]?.id) || 0;
      const second = scoreById.get(sorted[1]?.id) || 0;
      if (top > 0 && top > second) active = sorted[0]; // require a strict, unambiguous winner
    }
  }
  if (!active) return new Set();

  // Drop every OTHER page plus that page's exclusive Shell wrapper chain so no empty container
  // box is left behind. When we only GUESSED the active page (geometry fallback), keep the
  // safety net: never hide a page MORE populated than our guess. When the selected Tab told us
  // authoritatively (confident), drop the others unconditionally — a just-left page can still
  // have MORE retained/laid-out content than the freshly-navigated one until MAUI tears it down.
  const activeScore = scoreById.get(active.id) || 0;
  const drop = new Set();
  for (const p of pages) {
    if (p.id === active.id) continue;
    const keptPages = explicitlyActive.length ? explicitlyActive : [active];
    if (keptPages.some((kept) =>
      p.id === kept.id ||
      isDescendantOf(kept.id, p.id, parent) ||
      isDescendantOf(p.id, kept.id, parent))) continue;
    if (!confident && (scoreById.get(p.id) || 0) > activeScore) continue; // safety only when guessing
    drop.add(p.id);
    let cur = parent.get(p.id);
    while (cur != null) {
      const e = byId.get(cur);
      if (!e) break;
      const t = String(e.type || "");
      if (t !== "ShellContent" && t !== "ShellSection") break;
      // Never drop a wrapper that also contains the active page (or any page we're keeping).
      const hostsKeptPage = pages.some((x) => !drop.has(x.id) && isDescendantOf(x.id, cur, parent));
      if (hostsKeptPage) break;
      drop.add(cur);
      cur = parent.get(cur);
    }
  }
  return drop;
}

export function renderedRoots(roots, window) {
  const dropIds = inactivePageIds(roots, window);
  const walk = (el, ax, ay) => {
    if (!el) return [];
    if (dropIds.has(el.id)) return [];
    const absBounds = el.windowBounds ? toRect(el.windowBounds) : toRect(el.bounds, ax, ay);
    const children = [];
    for (const c of el.children || []) children.push(...walk(c, absBounds.x, absBounds.y));
    const draws = isDisplayed(el) && hasPositiveSize(absBounds) && intersectsWindow(absBounds, window);
    if (!draws) return children;
    const { sourceFile: _sourceFile, ...publicElement } = el;
    return [{ ...publicElement, absBounds, children }];
  };

  const out = [];
  for (const r of roots || []) out.push(...walk(r, 0, 0));
  return alignPageRoots(out);
}

function isAbsoluteChrome(el) {
  return /^(NavBar|Toolbar|Tab)/.test(String(el?.type || ""));
}

function translateTree(el, dx, dy) {
  const b = el.absBounds || toRect(el.bounds);
  const absoluteChrome = isAbsoluteChrome(el);
  const offsetX = absoluteChrome ? 0 : dx;
  const offsetY = absoluteChrome ? 0 : dy;
  const childDx = absoluteChrome ? 0 : dx;
  const childDy = absoluteChrome ? 0 : dy;
  return {
    ...el,
    absBounds: { ...b, x: b.x + offsetX, y: b.y + offsetY },
    children: (el.children || []).map((c) => translateTree(c, childDx, childDy)),
  };
}

function alignPageRoots(roots) {
  const tabTop = Math.min(
    ...roots
      .filter((r) => r.type === "Tab" && r.absBounds?.height > 0)
      .map((r) => r.absBounds.y)
  );
  const navBottom = Math.max(
    0,
    ...roots
      .filter((r) => /^(NavBar|Toolbar)/.test(String(r.type || "")) && r.absBounds?.height > 0)
      .map((r) => r.absBounds.y + r.absBounds.height)
  );

  return roots.map((root) => {
    const b = root.absBounds;
    if (!b || !String(root.type || "").endsWith("Page") || b.y !== 0 || !(b.height > 0)) return root;

    const offsetFromTab = Number.isFinite(tabTop) ? tabTop - b.height : 0;
    const dy = Math.max(navBottom, offsetFromTab, 0);
    return dy > 0 ? translateTree(root, 0, dy) : root;
  });
}

// Depth-first flatten → Map(id → {el, parentId, depth}) for O(1) lookup + list rendering.
// Each node already carries window-space `absBounds` (computed in renderedRoots, which
// prefers the agent's authoritative `windowBounds` and falls back to accumulating
// parent-relative bounds). We keep the accumulation here only as a defensive backstop
// for any node that reached this point without absBounds set.
function indexRoots(roots) {
  const index = new Map();
  const order = [];
  const walk = (el, parentId, depth, ax, ay) => {
    if (!el || el.id == null) return;
    const b = el.bounds || { x: 0, y: 0, width: 0, height: 0 };
    const x = Number.isFinite(Number(el.absBounds?.x)) ? Number(el.absBounds.x) : ax + (Number(b.x) || 0);
    const y = Number.isFinite(Number(el.absBounds?.y)) ? Number(el.absBounds.y) : ay + (Number(b.y) || 0);
    el.absBounds = {
      x,
      y,
      width: Number.isFinite(Number(el.absBounds?.width)) ? Number(el.absBounds.width) : Number(b.width) || 0,
      height: Number.isFinite(Number(el.absBounds?.height)) ? Number(el.absBounds.height) : Number(b.height) || 0,
    };
    index.set(String(el.id), { el, parentId, depth });
    order.push(String(el.id));
    for (const c of el.children || []) walk(c, String(el.id), depth + 1, x, y);
  };
  for (const r of roots) walk(r, null, 0, 0, 0);
  return { index, order };
}

export class LiveStore {
  constructor(deviceOpts = {}) {
    const { inspectSnapshot, inspectQuery, inspectAction, ...transportOptions } = deviceOpts;
    this.device = new DevflowDevice(transportOptions);
    this._inspectSnapshot = typeof inspectSnapshot === "function" ? inspectSnapshot : null;
    this._inspectQuery = typeof inspectQuery === "function" ? inspectQuery : null;
    this._inspectAction = typeof inspectAction === "function" ? inspectAction : null;
    this.subscribers = new Set();
    this.timeline = [];
    this.state = {
      connected: false,
      info: this.device.info(),
      roots: [],
      order: [],
      selectedId: null,
      selectedElement: null,
      agents: [],
      activePort: null,
      shotSeq: 0,
      busy: false,
      lastError: null,
      projection: "agentFallback",
      projectionWarning: null,
      snapshotId: null,
      revision: null,
      updatedAt: nowISO(),
    };
    this._index = new Map();
    this._shotPath = null;
    // Live-sync bookkeeping
    this._treeHash = null;
    this._lastTheme = null;
    this._lastShotAt = 0;
    this._lastShotHash = null;   // fingerprint of the current screenshot bytes
    // Theme-change settle guard: every theme flip bumps _themeEpoch; a settle loop
    // only publishes frames while its epoch is still current, so a late frame from
    // an earlier toggle can never clobber a newer one.
    this._themeEpoch = 0;
    // Monotonic snapshot revision — the browser drops any snapshot older than the
    // last one it applied, so overlapping refresh/sync/settle can't flicker the UI.
    this._rev = 0;
    this._polling = false;
    this._pollTimer = null;
    this._pollIntervalMs = 1200;
    this._minShotGapMs = 700;
    // WebSocket push (agent /ws/v1/ui/events). When connected we lean on push and
    // slow the safety poll; when it drops we fall back to the fast poll.
    this._eventStream = null;
    this._wsConnected = false;
    this._syncDebounce = null;
    this._pollFastMs = 1200;    // no WS: poll briskly
    this._pollSafetyMs = 5000;  // WS up: occasional reconcile in case an event is missed
    // Selection race guard: a background hit-test reconcile must not stomp a newer selection.
    this._selSeq = 0;
    this._recordingStatus = null;
  }

  // ── Subscriptions (SSE) ─────────────────────────────────────────────────────
  subscribe(fn) {
    this.subscribers.add(fn);
    return () => this.subscribers.delete(fn);
  }

  _emit() {
    this._rev++;
    const snap = this.snapshot();
    for (const fn of this.subscribers) {
      try {
        fn(snap);
      } catch {
        /* a dead subscriber shouldn't break the others */
      }
    }
  }

  // Cheap, JSON-serializable view of the world. Never includes local file paths.
  snapshot() {
    return {
      connected: this.state.connected,
      info: this.state.info,
      roots: this.state.roots,
      selectedId: this.state.selectedId,
      selectedElement: this.state.selectedElement,
      agents: this.state.agents,
      activePort: this.state.activePort,
      shotSeq: this.state.shotSeq,
      rev: this._rev,
      busy: this.state.busy,
      lastError: this.state.lastError,
      projection: this.state.projection,
      projectionWarning: this.state.projectionWarning,
      snapshotId: this.state.snapshotId,
      revision: this.state.revision,
      timeline: this.timeline.slice(-40),
      recording: !!this._recordingStatus?.recording,
      recorder: this._recordingStatus
        ? {
            name: this._recordingStatus.name || null,
            count: Number(this._recordingStatus.steps || 0),
            steps: [],
          }
        : null,
      updatedAt: this.state.updatedAt,
    };
  }

  currentShotPath() {
    return this._shotPath;
  }

  _log(kind, detail, ok = true) {
    this.timeline.push({ t: nowISO(), kind, detail, ok });
    if (this.timeline.length > 200) this.timeline.shift();
  }

  // ── Refresh (tree + status + theme + screenshot, all in parallel) ───────────
  // info=false skips the status+theme fetch (mutations only need fresh tree+shot).
  async refresh({ shot = true, info = true } = {}) {
    this.state.busy = true;
    this._emit();
    try {
      // Resolve the connection once so the parallel calls below don't each port-scan.
      await this.device._ensureConnection().catch(() => {});

      const treeP = this._getTree();
      const infoP = info ? this.device.refreshInfo() : Promise.resolve(this.device.info());
      const themeP = info ? this.device.themeGet() : Promise.resolve(null);
      const shotP = shot ? this._grabShot() : Promise.resolve(null);
      // Keep the platform picker current — cheap broker GET, run in parallel with everything else.
      const agentsP = info ? this.device.listAgents().catch(() => null) : Promise.resolve(null);
      const [t, , , , agentList] = await Promise.all([treeP, infoP, themeP, shotP, agentsP]);

      this.state.info = {
        ...this.device.info(),
        ...(t.window ? { window: t.window } : {}),
      };
      this.state.connected = !!this.state.info.connected;
      this.state.activePort = this.device.whichPort();
      if (info && agentList) this.state.agents = this._normalizeAgents(agentList);
      if (this.state.info.theme) this._lastTheme = this.state.info.theme;

      if (t.ok) {
        this._applyRoots(t.roots, { canonical: t.projection === "activeVisual" });
        this.state.projection = t.projection || "agentFallback";
        this.state.projectionWarning = t.warning || null;
        this.state.snapshotId = t.snapshotId || null;
        this.state.revision = t.revision || null;
        this.state.lastError = null;
      } else {
        this.state.lastError = t.error || "tree unavailable";
      }
      this.state.updatedAt = nowISO();
    } finally {
      this.state.busy = false;
      this._emit();
    }
    return this.snapshot();
  }

  async _getTree() {
    if (this._inspectSnapshot) {
      try {
        const snapshot = await this._inspectSnapshot();
        if (snapshot?.ok && snapshot.projection === "activeVisual" && Array.isArray(snapshot.roots)) {
          return {
            ok: true,
            roots: snapshot.roots,
            window: snapshot.viewport
              ? {
                  x: 0,
                  y: 0,
                  width: Number(snapshot.viewport.width) || 0,
                  height: Number(snapshot.viewport.height) || 0,
                }
              : null,
            projection: "activeVisual",
            snapshotId: snapshot.snapshotId,
            revision: snapshot.revision,
          };
        }
        const warning = snapshot?.error?.message || snapshot?.error || "canonical Inspector snapshot unavailable";
        const fallback = await this.device.getRoots(0);
        return fallback.ok ? { ...fallback, projection: "agentFallback", warning } : fallback;
      } catch (error) {
        const fallback = await this.device.getRoots(0);
        return fallback.ok
          ? { ...fallback, projection: "agentFallback", warning: String(error?.message || error) }
          : fallback;
      }
    }
    const fallback = await this.device.getRoots(0);
    return fallback.ok ? { ...fallback, projection: "agentFallback" } : fallback;
  }

  // Turn canonical Inspector roots or raw agent roots into an indexed tree and reconcile selection.
  // Shared by refresh() and the live-sync poll tick. Also refreshes the tree hash.
  _applyRoots(rawRoots, { canonical = false } = {}) {
    const roots = canonical ? rawRoots : renderedRoots(rawRoots, this.state.info.window);
    this.state.roots = roots;
    const { index, order } = indexRoots(roots);
    this._index = index;
    this.state.order = order;
    this._treeHash = this._hashRoots(roots);
    if (this.state.selectedId && index.has(String(this.state.selectedId))) {
      const known = index.get(String(this.state.selectedId)).el;
      // Preserve authoritative windowBounds captured at hit-test time.
      const wb = this.state.selectedElement?.windowBounds;
      this.state.selectedElement = wb ? { ...known, windowBounds: wb } : known;
    } else if (this.state.selectedId) {
      this.state.selectedId = null;
      this.state.selectedElement = null;
    }
    return roots;
  }

  // Cheap structural fingerprint: id + type + text + rounded bounds per node.
  // Changes when the app navigates, re-lays-out, or edits text — the signal for live-sync.
  _hashRoots(roots) {
    let acc = 2166136261; // FNV-1a seed
    const feed = (s) => {
      for (let i = 0; i < s.length; i++) {
        acc ^= s.charCodeAt(i);
        acc = Math.imul(acc, 16777619);
      }
    };
    const walk = (el) => {
      if (!el) return;
      const b = el.absBounds || el.bounds || {};
      feed(
        `${el.id}|${el.type}|${el.text ?? ""}|${Math.round(b.x || 0)},${Math.round(b.y || 0)},${Math.round(
          b.width || 0
        )},${Math.round(b.height || 0)}|`
      );
      for (const c of el.children || []) walk(c);
    };
    for (const r of roots || []) walk(r);
    return acc >>> 0;
  }

  async _grabShot() {
    const s = await this.device.screenshot();
    if (s.ok) {
      this._shotPath = s.path;
      this._lastShotAt = Date.now();
      let hash = null;
      try { hash = hashBytes(await readFile(s.path)); } catch { /* keep null → treat as changed */ }
      s.hash = hash;
      // Only advance shotSeq (→ browser reloads the image) when the pixels changed.
      // This lets the theme-settle loop poll cheaply and stop the moment the render
      // stabilises, instead of forcing a reload on every identical capture.
      if (hash == null || hash !== this._lastShotHash) {
        this._lastShotHash = hash;
        this.state.shotSeq += 1;
        s.changed = true;
      } else {
        s.changed = false;
      }
    }
    return s;
  }

  // Throttled screenshot for the poll loop — avoids hammering the agent on rapid changes.
  async _grabShotThrottled() {
    if (Date.now() - this._lastShotAt < this._minShotGapMs) return null;
    return this._grabShot();
  }

  // After a theme flip the app re-renders asynchronously, so the first screenshot can
  // show the old/partial theme. Poll until the frame stabilises (two consecutive
  // byte-identical captures) or we hit the timeout, emitting progress as it converges.
  // Guarded by `epoch`: if a newer theme change supersedes us we bail immediately, so a
  // stale settle can never clobber a newer toggle's frame.
  async _settleThemeShot(epoch) {
    const delays = [150, 250, 400, 700, 1000]; // ~2.5s worst case; usually settles in 1–2 ticks
    let prevHash = this._lastShotHash;
    let stable = 0;
    for (const d of delays) {
      await sleep(d);
      if (epoch !== this._themeEpoch) return;      // a newer toggle owns the frame now
      if (this.state.busy || this._polling) continue; // don't contend with a refresh/sync
      const s = await this._grabShot();
      if (epoch !== this._themeEpoch) return;
      if (!s || !s.ok || s.hash == null) continue;
      if (s.hash === prevHash) {
        if (++stable >= 1) {                       // two identical in a row → settled
          this.state.updatedAt = nowISO();
          this._emit();
          return;
        }
      } else {
        stable = 0;
        prevHash = s.hash;
        this.state.updatedAt = nowISO();
        this._emit();                              // show progress toward the final theme
      }
    }
    if (epoch === this._themeEpoch) this._emit();  // timed out — publish whatever we have
  }

  // ── Live sync ───────────────────────────────────────────────────────────────
  // Reflect changes made DIRECTLY in the running app (navigation, in-app edits, OS
  // theme toggle) in the canvas. Primary channel is the agent's push stream
  // (/ws/v1/ui/events); a timer reconciles as a safety net — fast while the socket
  // is down, occasional once push is connected.
  startLiveSync(intervalMs = this._pollFastMs) {
    this._pollFastMs = intervalMs;
    if (this._pollTimer || this._eventStream) return;
    this._openEventStream();
    const tick = async () => {
      await this._syncNow();
      if (this._pollTimer) {
        this._pollTimer = setTimeout(tick, this._wsConnected ? this._pollSafetyMs : this._pollFastMs);
      }
    };
    this._pollTimer = setTimeout(tick, this._pollFastMs);
  }

  stopLiveSync() {
    if (this._pollTimer) {
      clearTimeout(this._pollTimer);
      this._pollTimer = null;
    }
    if (this._syncDebounce) {
      clearTimeout(this._syncDebounce);
      this._syncDebounce = null;
    }
    if (this._eventStream) {
      try { this._eventStream.close(); } catch { /* */ }
      this._eventStream = null;
    }
    this._wsConnected = false;
  }

  _openEventStream() {
    if (this._eventStream) {
      try { this._eventStream.close(); } catch { /* */ }
      this._eventStream = null;
    }
    this._eventStream = this.device.openEventStream(
      (ev) => this._onAgentEvent(ev),
      (st) => { this._wsConnected = !!st.connected; }
    );
  }

  // Reopen the push stream against the currently-targeted agent (after selectAgent,
  // which re-resolves the connection to a new port).
  _reopenEventStream() {
    if (!this._pollTimer && !this._eventStream) return; // live-sync not running
    this._openEventStream();
  }

  // Agent pushed an event. treeChange/navigation/lifecycle mean "something moved";
  // themeChange means the app's light/dark theme flipped (emitted whenever the theme is
  // set through the agent — our canvas, the #295 web inspector, or any other caller). All
  // warrant a refresh, so debounce a sync — a burst (e.g. rapid navigation) collapses into one.
  _onAgentEvent(ev) {
    const t = ev && ev.type;
    if (t !== "treeChange" && t !== "navigation" && t !== "lifecycle" && t !== "themeChange") return;
    if (this._syncDebounce) clearTimeout(this._syncDebounce);
    this._syncDebounce = setTimeout(() => {
      this._syncDebounce = null;
      this._syncNow();
    }, 120);
  }

  async _syncNow() {
    // Never contend with an in-flight refresh/mutation or another sync.
    if (this.state.busy || this._polling) return;
    this._polling = true;
    try {
      const [t, theme] = await Promise.all([this._getTree(), this.device.themeGet()]);
      if (!t.ok) {
        if (this.state.connected) {
          this.state.connected = false;
          this.state.lastError = t.error || "agent unreachable";
          this._emit();
        }
        return;
      }
      if (t.window) this.state.info = { ...this.state.info, window: t.window };
      const themeVal = theme && theme.ok ? theme.data?.effectiveTheme || theme.data?.theme || null : this._lastTheme;
      const projected = t.projection === "activeVisual"
        ? t.roots
        : renderedRoots(t.roots, this.state.info.window);
      const hash = this._hashRoots(projected);
      const treeChanged = hash !== this._treeHash;
      const themeChanged = themeVal && themeVal !== this._lastTheme;

      if (!this.state.connected) this.state.connected = true;
      if (!treeChanged && !themeChanged) return;

      if (themeChanged) {
        this._lastTheme = themeVal;
        this.state.info = { ...this.state.info, theme: themeVal };
      }
      if (treeChanged) {
        this._applyRoots(t.roots, { canonical: t.projection === "activeVisual" });
        this.state.projection = t.projection || "agentFallback";
        this.state.projectionWarning = t.warning || null;
        this.state.snapshotId = t.snapshotId || null;
        this.state.revision = t.revision || null;
      }

      if (themeChanged) {
        // A theme flip MUST refresh the image (bypass the throttle), then settle to
        // the fully-rendered frame — the first shot after a flip is often mid-render.
        const epoch = ++this._themeEpoch;
        await this._grabShot();
        this.state.updatedAt = nowISO();
        this._log("live-sync", { tree: treeChanged, theme: themeVal, push: this._wsConnected }, true);
        this._emit();
        this._settleThemeShot(epoch); // fire-and-forget, epoch-guarded
        return;
      }

      await this._grabShotThrottled();
      this.state.updatedAt = nowISO();
      this._log("live-sync", { tree: treeChanged, theme: undefined, push: this._wsConnected }, true);
      this._emit();
    } catch {
      /* transient error — the safety timer will retry */
    } finally {
      this._polling = false;
    }
  }

  // ── Read ops ────────────────────────────────────────────────────────────────
  getElement(id) {
    const hit = this._index.get(String(id));
    return hit ? hit.el : null;
  }

  async getElementLive(id) {
    const el = await this.device.getElement(id);
    return el || this.getElement(id);
  }

  async query(sel) {
    if (this._inspectQuery) {
      try {
        const result = await this._inspectQuery(sel);
        if (result?.ok && Array.isArray(result.elements))
          return { ok: true, elements: result.elements };
        if (result?.error)
          return { ok: false, error: result.error?.message || String(result.error), elements: [] };
      } catch {
        // The direct agent query below is the explicit recovery path when the broker is unavailable.
      }
    }
    return this.device.query(sel);
  }

  async getProperty(id, name) {
    if (this._inspectAction) {
      const result = await this._inspectAction("/api/getProperty", { elementId: id, name });
      if (result?.ok) return { ok: true, value: result.value };
      if (result?.attempted !== false)
        return { ok: false, error: result?.error?.message || String(result?.error || "read failed") };
    }
    return this.device.getProperty(id, name);
  }

  async _runMutation(path, body, directFallback) {
    if (this._inspectAction) {
      const result = await this._inspectAction(path, body);
      if (result?.ok) return { ok: true, value: result.value };
      if (result?.attempted !== false) {
        return {
          ok: false,
          error: result?.error?.message || String(result?.error || "Inspector mutation failed"),
          code: result?.error?.code,
        };
      }
    }
    // The direct client may retry only a lease-held rejection, which occurs before the agent
    // dispatches the action. It never retries a transport-ambiguous mutation completion.
    return directFallback();
  }

  async _resolveMutationTarget(sel) {
    if (sel == null) return { error: "no target given — pass id, automationId, or text" };
    if (typeof sel !== "object") return { id: String(sel) };
    if (sel.id != null) return { id: String(sel.id) };
    if (!sel.automationId && !sel.text)
      return { error: "no target given — pass id, automationId, or text" };

    const result = await this.query({ automationId: sel.automationId, text: sel.text });
    if (!result.ok) return { error: result.error || "target query failed" };
    const visible = (result.elements || []).filter((element) =>
      element?.id != null &&
      element?.isVisible !== false &&
      element?.state?.displayed !== false &&
      element?.isEnabled !== false &&
      element?.state?.enabled !== false &&
      Number(element?.opacity ?? 1) !== 0);
    if (visible.length === 1) return { id: String(visible[0].id) };
    const label = sel.automationId
      ? `automationId "${sel.automationId}"`
      : `text "${sel.text}"`;
    return {
      error: visible.length === 0
        ? `no visible element matches ${label}`
        : `${label} is ambiguous: ${visible.length} visible elements match. Re-issue with an explicit id.`,
    };
  }

  // ── Selection ───────────────────────────────────────────────────────────────
  select(id) {
    this._selSeq++; // invalidate any in-flight hit-test reconcile
    const el = this.getElement(id);
    this.state.selectedId = el ? String(id) : null;
    this.state.selectedElement = el;
    this._log("select", { id: this.state.selectedId, type: el?.type }, !!el);
    this._emit();
    return el;
  }

  // Instant hit-test against the locally-rendered index: the smallest displayed,
  // positive-size element whose window-space absBounds contains (x,y). Same coordinate
  // space as the screenshot click and the agent hit-test, so results line up.
  localHitTest(x, y) {
    let best = null;
    let bestArea = Infinity;
    for (const { el } of this._index.values()) {
      if (el.isVisible === false) continue;
      const b = el.absBounds;
      if (!b || !(b.width > 0) || !(b.height > 0)) continue;
      if (x < b.x || x > b.x + b.width || y < b.y || y > b.y + b.height) continue;
      const area = b.width * b.height;
      if (area < bestArea) { best = el; bestArea = area; }
    }
    return best;
  }

  // The best window-space rectangle for an element: the agent's `windowBounds` when it's real,
  // otherwise the tree-derived `absBounds`. Guards the platform quirk (observed on Android) where
  // the hit-test endpoint reports windowBounds anchored at the origin (0,0) even though the tree
  // reports real coordinates — in that case absBounds is the trustworthy rect.
  _windowRect(el) {
    const wb = el && el.windowBounds;
    const ab = el && el.absBounds;
    const nearOrigin = (b) => b && Math.abs(Number(b.x) || 0) < 0.5 && Math.abs(Number(b.y) || 0) < 0.5;
    const hasRealPos = (b) => b && (Math.abs(Number(b.x) || 0) > 0.5 || Math.abs(Number(b.y) || 0) > 0.5);
    if (wb && Number(wb.width) > 0 && !(nearOrigin(wb) && hasRealPos(ab))) return wb;
    if (ab && Number(ab.width) > 0) return ab;
    return wb || ab || undefined;
  }

  // Return the element with a usable window-space `windowBounds` (so the overlay/selection box
  // always lands on the real control, even when the raw element only carries `absBounds`).
  _withWindowBounds(el) {
    if (!el) return el;
    const rect = this._windowRect(el);
    return rect && rect !== el.windowBounds ? { ...el, windowBounds: rect } : el;
  }

  async hitTestSelect(x, y) {
    const seq = ++this._selSeq;
    // 1) Instant: pick from our local rendered index so selection is immediate — no round-trip.
    const local = this.localHitTest(x, y);
    if (local) {
      this.state.selectedId = String(local.id);
      this.state.selectedElement = this._withWindowBounds(local);
      this._log("hit-test-local", { x, y, id: local.id, type: local.type }, true);
      this._emit();
    }
    // 2) Reconcile in the background against the agent for authoritative id + windowBounds.
    try {
      const r = await this.device.hitTest(x, y);
      if (seq !== this._selSeq) return this.state.selectedElement; // a newer selection won
      if (r.ok && r.element) {
        const id = String(r.element.id);
        const known = this.getElement(id);
        if (!known) {
          // The agent resolved the click to an element that ISN'T in our rendered tree — i.e. it
          // pointed at something the user can't see. This happens with MAUI Shell, which keeps
          // every tab/flyout page mounted: the agent's hit-test (on Android, with degenerate (0,0)
          // bounds) can surface a collapsed inactive-page element. Keep the visible local hit
          // rather than stomping the on-screen selection with an off-screen element.
          if (local) {
            this._log("hit-test", { x, y, id, type: r.element.type, keptLocal: true }, true);
            return this.state.selectedElement;
          }
          this.state.selectedId = id;
          this.state.selectedElement = this._withWindowBounds(r.element);
          this._log("hit-test", { x, y, id, type: r.element.type, offscreen: true }, true);
          this._emit();
          return this.state.selectedElement;
        }
        // Known on-screen element: merge with the agent's authoritative windowBounds, but prefer
        // the element's real absBounds if the agent bounds are degenerate (origin) — see _windowRect.
        const windowBounds = this._windowRect({ windowBounds: r.element.windowBounds, absBounds: known.absBounds });
        const merged = { ...known, windowBounds };
        const changed =
          id !== this.state.selectedId ||
          JSON.stringify(this.state.selectedElement?.windowBounds) !== JSON.stringify(merged.windowBounds);
        this.state.selectedId = id;
        this.state.selectedElement = merged;
        this._log("hit-test", { x, y, id, type: r.element.type, reconciled: changed }, true);
        if (changed || !local) this._emit();
        return this.state.selectedElement;
      }
      if (!local) {
        this._log("hit-test", { x, y, error: r.error }, false);
        this._emit();
      }
    } catch (e) {
      // Agent unreachable — the instant local selection stands.
      if (!local) this._log("hit-test", { x, y, error: String(e?.message || e) }, false);
    }
    // Only the local hit can still be reported here: every path where the agent resolved an
    // element returns above. When nothing was hit at all, report the miss instead of handing
    // back the previous selection — a stale element would look like a successful hit-test and
    // send the caller on to tap/fill the wrong control.
    return local ? this.state.selectedElement : null;
  }

  // ── Selection → agent context ───────────────────────────────────────────────
  // Rich, pull-only description of the current selection. When the human says
  // "the selected/highlighted element", the Copilot agent calls get_selection and
  // gets this: identity, text, bounds, state, and concrete next actions to run.
  selectionContext() {
    const el = this.state.selectedElement;
    if (!el) {
      return {
        selectedId: null,
        element: null,
        hint: "Nothing is selected. Ask the user to click an element in the canvas, or call hit_test(x,y) or query(...).",
      };
    }
    const b = el.windowBounds || el.absBounds || el.bounds || {};
    const bounds = {
      x: Math.round(Number(b.x) || 0),
      y: Math.round(Number(b.y) || 0),
      width: Math.round(Number(b.width) || 0),
      height: Math.round(Number(b.height) || 0),
    };
    const summary =
      `${el.type}` +
      `${el.automationId ? ` #${el.automationId}` : ""}` +
      `${el.text ? ` "${el.text}"` : ""}` +
      ` at (${bounds.x},${bounds.y}) ${bounds.width}×${bounds.height}`;
    const isInput = /Entry|Editor|SearchBar/i.test(String(el.type || ""));
    const suggestedActions = [{ action: "get_property", args: { id: el.id, name: "Text" } }];
    suggestedActions.push(isInput ? { action: "fill", args: { id: el.id, text: "<new text>" } } : { action: "tap", args: { id: el.id } });
    suggestedActions.push({ action: "set_property", args: { id: el.id, name: "Text", value: "<new value>" } });
    return {
      selectedId: this.state.selectedId,
      summary,
      element: {
        id: el.id,
        type: el.type,
        fullType: el.fullType,
        automationId: el.automationId || null,
        text: el.text ?? null,
        role: el.role || null,
        isVisible: el.isVisible !== false,
        isEnabled: el.isEnabled !== false,
        bounds,
        state: el.state || null,
      },
      app: { name: this.state.info?.appName, platform: this.state.info?.platform },
      suggestedActions,
    };
  }

  // Best-effort element screenshot as base64 — the visual for "Attach to Copilot".
  async elementShot(id) {
    try {
      const r = await this.device.elementShot(id);
      if (r && r.ok && r.buffer) return { ok: true, base64: r.buffer.toString("base64"), mimeType: r.mimeType || "image/png" };
    } catch { /* best-effort: no visual */ }
    return { ok: false };
  }

  // ── Recorder support (used by recorder.mjs while recording a workflow) ──────────
  // Turn a live element id into the most DURABLE selector we can:
  //   AutomationId > exact text > type+index (fragile) > raw id (fragile).
  // avoidText: skip the text candidate for actions that CHANGE the element's Text
  //   (fill / setProperty Text) — selecting an element by the very text you're about to
  //   write is circular and won't resolve on a clean replay. type+index survives a text edit.
  _bestSelector(id, { avoidText = false } = {}) {
    const el = this.getElement(id);
    if (!el) return { selectorKind: "id", selector: String(id), id: String(id), fragile: true };
    const base = { type: el.type, id: String(el.id) };
    if (el.automationId) {
      return { ...base, selectorKind: "automationId", selector: el.automationId, automationId: el.automationId, text: el.text ?? null, fragile: false };
    }
    const text = el.text != null ? String(el.text).trim() : "";
    if (text && !avoidText) {
      return { ...base, selectorKind: "text", selector: text, text, automationId: null, fragile: false };
    }
    const index = this._typeIndexOf(el);
    if (index >= 0) {
      return { ...base, selectorKind: "typeIndex", selector: `${el.type}[${index}]`, index, automationId: null, text: null, fragile: true };
    }
    return { ...base, selectorKind: "id", selector: String(id), automationId: null, text: null, fragile: true };
  }

  // Stable index of an element among all same-type elements, in render order.
  _typeIndexOf(el) {
    let i = 0;
    for (const oid of this.state.order) {
      const cur = this._index.get(oid)?.el;
      if (!cur || cur.type !== el.type) continue;
      if (String(cur.id) === String(el.id)) return i;
      i++;
    }
    return -1;
  }

  // Resolve a recorded typeIndex selector back to a live id (used on replay).
  _resolveTypeIndex(type, index) {
    let i = 0;
    for (const oid of this.state.order) {
      const cur = this._index.get(oid)?.el;
      if (!cur || cur.type !== type) continue;
      if (i === index) return String(cur.id);
      i++;
    }
    return null;
  }

  // Lightweight signature of the current page: the largest visible *Page / Shell root's
  // identity, plus the structural tree hash. Used for navigation detection + step context.
  _pageSignature() {
    let best = null;
    let bestArea = -1;
    for (const { el } of this._index.values()) {
      const t = String(el.type || "");
      if (!/Page$/.test(t) && !/^Shell/.test(t)) continue;
      if (el.isVisible === false) continue;
      const b = el.absBounds || el.bounds || {};
      const area = (b.width || 0) * (b.height || 0);
      if (area > bestArea) { best = el; bestArea = area; }
    }
    const label = best ? (best.automationId || best.text || best.type) : (this.state.info?.appName || null);
    return { label, hash: (this._treeHash || 0) >>> 0 };
  }

  // A Text set on an Entry/Editor/SearchBar is really a "fill"; everything else is setProperty.
  _classifySetProp(id, name) {
    const el = this.getElement(id);
    const input = el && /Entry|Editor|SearchBar/i.test(String(el.type || ""));
    return String(name) === "Text" && input ? "fill" : "setProperty";
  }

  // Turn a device selector (id string | {id} | {automationId} | {text}) into recorder target meta.
  _selMeta(sel) {
    if (sel == null) return null;
    if (typeof sel === "string" || typeof sel === "number") return { id: String(sel) };
    if (sel.id != null) return { id: String(sel.id) };
    if (sel.automationId) return { automationId: sel.automationId };
    if (sel.text) return { text: sel.text };
    return null;
  }

  // Keep the fallback canvas UI in sync with the broker-owned recording.
  async _recordAction(meta) {
    if (meta && meta.ok === false) return;
    try {
      this._recordingStatus = await this.device.recordingStatus();
      this._emit();
    } catch {
      /* recording must never break a live action */
    }
  }

  // ── Mutations (act → light refresh → emit). info:false skips the status/theme
  //    round-trip since a UI action doesn't change device metadata. ───────────────
  async setProperty(id, name, value) {
    const beforeHash = this._treeHash;
    const r = await this._runMutation(
      "/api/setProperty",
      { elementId: id, name, value },
      () => this.device.setProperty(id, name, value));
    this._log("set-property", { id, name, value, error: r.ok ? undefined : r.error }, r.ok);
    await this.refresh({ info: false });
    await this._recordAction({ action: this._classifySetProp(id, name), target: { id: String(id) }, name, value, beforeHash, ok: r.ok });
    return r;
  }

  async tap(sel) {
    const beforeHash = this._treeHash;
    const target = await this._resolveMutationTarget(sel);
    const r = target.error
      ? { ok: false, error: target.error }
      : await this._runMutation(
          "/api/tap",
          { elementId: target.id },
          () => this.device.tap({ id: target.id }));
    this._log("tap", { sel, error: r.ok ? undefined : r.error }, r.ok);
    await this.refresh({ info: false });
    await this._recordAction({ action: "tap", target: this._selMeta(sel), beforeHash, ok: r.ok });
    return r;
  }

  async fill(sel, text) {
    const beforeHash = this._treeHash;
    const target = await this._resolveMutationTarget(sel);
    const r = target.error
      ? { ok: false, error: target.error }
      : await this._runMutation(
          "/api/fill",
          { elementId: target.id, text },
          () => this.device.fill({ id: target.id }, text));
    this._log("fill", { sel, text, error: r.ok ? undefined : r.error }, r.ok);
    await this.refresh({ info: false });
    await this._recordAction({ action: "fill", target: this._selMeta(sel), value: text, beforeHash, ok: r.ok });
    return r;
  }

  async scroll(opts) {
    const beforeHash = this._treeHash;
    const r = await this._runMutation(
      "/api/scroll",
      {
        elementId: opts?.element,
        deltaX: Number(opts?.dx) || 0,
        deltaY: Number(opts?.dy) || 0,
        itemIndex: opts?.itemIndex,
        scrollToPosition: opts?.position,
        animated: opts?.animated,
        x: opts?.x,
        y: opts?.y,
      },
      () => this.device.scroll(opts));
    this._log("scroll", { ...opts, error: r.ok ? undefined : r.error }, r.ok);
    await this.refresh({ info: false });
    await this._recordAction({ action: "scroll", target: opts?.element != null ? { id: String(opts.element) } : null, args: { ...opts }, beforeHash, ok: r.ok });
    return r;
  }

  async navigate(route) {
    const beforeHash = this._treeHash;
    const r = await this._runMutation(
      "/api/navigate",
      { route },
      () => this.device.navigate(route));
    this._log("navigate", { route, error: r.ok ? undefined : r.error }, r.ok);
    await this.refresh({ info: false });
    await this._recordAction({ action: "navigate", value: route, args: { route }, beforeHash, ok: r.ok });
    return r;
  }

  async back() {
    const beforeHash = this._treeHash;
    const r = await this._runMutation(
      "/api/back",
      {},
      () => this.device.back());
    this._log("back", { error: r.ok ? undefined : r.error }, r.ok);
    await this.refresh({ info: false });
    await this._recordAction({ action: "back", beforeHash, ok: r.ok });
    return r;
  }

  async resize(width, height) {
    const r = await this.device.resize(width, height);
    this._log("resize", { width, height, error: r.ok ? undefined : r.error }, r.ok);
    // A resize DOES change window metadata, so do a full refresh when it succeeds.
    if (r.ok) await this.refresh();
    return r;
  }

  // ── Agent picker: list every running app, and switch which one the canvas drives ──
  // Normalize broker/CLI agent records into a compact, UI-friendly shape (one per port).
  _normalizeAgents(list) {
    const arr = Array.isArray(list) ? list : list && Array.isArray(list.agents) ? list.agents : [];
    const active = this.device.whichPort();
    const seen = new Set();
    const out = [];
    for (const a of arr) {
      const port = Number(a?.port);
      if (!Number.isFinite(port) || seen.has(port)) continue;
      seen.add(port);
      out.push({
        port,
        platform: a.platform || platformFromTfm(a.tfm) || "device",
        tfm: a.tfm || null,
        appName: a.appName || a.project || "app",
        active: port === active,
      });
    }
    out.sort((x, y) => String(x.platform).localeCompare(String(y.platform)) || x.port - y.port);
    return out;
  }

  async listAgents() {
    let list;
    try {
      list = await this.device.listAgents();
    } catch {
      list = [];
    }
    this.state.agents = this._normalizeAgents(list);
    this.state.activePort = this.device.whichPort();
    this._emit();
    return this.state.agents;
  }

  // Switch the canvas to a different running app/platform. Port is preferred (unique);
  // the platform hint keeps Android adb-forward detection working when switching by port.
  async selectAgent({ platform, port } = {}) {
    const target = port != null && port !== "" ? Number(port) : null;
    this._log("select-agent", { platform, port: target });
    // Element ids aren't valid across a different app/agent — drop any current selection.
    this.state.selectedId = null;
    this.state.selectedElement = null;
    this._treeHash = null;
    this.device.retarget({ platform, agentPort: target });
    this.state.activePort = target ?? this.device.whichPort();
    const snap = await this.refresh(); // full refresh: new window/info/theme + tree + shot + agents
    this._reopenEventStream();         // re-point the push stream at the new agent's port
    return snap;
  }

  async setTheme(theme) {
    const beforeHash = this._treeHash;
    const epoch = ++this._themeEpoch; // tag this flip so a stale settle can't clobber it
    const r = await this.device.themeSet(theme);
    // themeSet already updated device._info.theme; surface the true effective theme.
    const eff = r.ok ? r.data?.effectiveTheme || r.data?.theme || this.device.info().theme : null;
    if (eff) {
      this._lastTheme = eff;
      // Reflect immediately in the snapshot (refresh below skips the theme fetch).
      this.state.info = { ...this.state.info, theme: eff };
    }
    this._log("set-theme", { theme, effective: eff, error: r.ok ? undefined : r.error }, r.ok);
    // Immediate refresh for responsiveness — but the first shot often catches a mid-render
    // frame (old/partial theme). The settle loop then converges to the fully-rendered theme.
    await this.refresh({ info: false });
    await this._recordAction({ action: "setTheme", value: theme, args: { theme }, beforeHash, ok: r.ok });
    if (r.ok) this._settleThemeShot(epoch); // fire-and-forget, epoch-guarded
    return { ...r, effective: eff };
  }

  async getLogs(limit = 100) {
    return this.device.logs(limit);
  }

  // ── Apply-and-verify: set a property, then read it back to confirm ──────────
  async applyAndVerify(id, name, value) {
    const beforeHash = this._treeHash;
    const set = await this._runMutation(
      "/api/setProperty",
      { elementId: id, name, value },
      () => this.device.setProperty(id, name, value));
    if (!set.ok) {
      this._log("apply-verify", { id, name, value, error: set.error }, false);
      await this.refresh({ info: false });
      return { ok: false, stage: "set", error: set.error };
    }
    const read = await this.getProperty(id, name);
    const actual = read.ok ? read.value : undefined;
    const verified =
      read.ok && String(actual).trim() === String(value).trim();
    this._log("apply-verify", { id, name, value, actual, verified }, verified);
    await this.refresh({ info: false });
    await this._recordAction({ action: this._classifySetProp(id, name), target: { id: String(id) }, name, value, beforeHash, ok: true });
    return { ok: true, verified, expected: value, actual };
  }
}
