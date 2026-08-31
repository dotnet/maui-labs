// devflow.mjs — the DevflowDevice transport for the MAUI DevFlow Inspector Canvas host.
//
// Thin adapter over @maui-devflow/client, preserving the method names and return shapes consumed
// by store.mjs and the rest of the extension.

import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { DevFlowClient } from "@maui-devflow/client";

function num(v, fallback = 0) {
  const n = typeof v === "number" ? v : parseFloat(v);
  return Number.isFinite(n) ? n : fallback;
}

function normalizeResizeDimension(value) {
  const n = num(value, NaN);
  if (!Number.isFinite(n) || n <= 0) return null;
  return Math.round(n);
}
function largestWindowBounds(roots) {
  const rootCandidates = (roots || []).filter((root) => root?.type === "Window");
  const candidates = rootCandidates.length ? rootCandidates : (roots || []);
  let largest = null;
  let largestArea = 0;
  for (const node of candidates) {
    const bounds = node?.windowBounds || node?.bounds;
    const width = num(bounds?.width);
    const height = num(bounds?.height);
    const area = width * height;
    if (area > largestArea) {
      largestArea = area;
      largest = { x: num(bounds?.x), y: num(bounds?.y), width, height };
    }
  }
  return largest;
}

// PNG magic: 0x89 'P' ... (8-byte signature). Cheap guard so we never write a JSON error
// body or a truncated buffer to a .png file.
const isPng = (b) => !!b && b.length > 8 && b[0] === 0x89 && b[1] === 0x50;

function isTapVisible(el) {
  return el?.isVisible !== false && el?.state?.displayed !== false && el?.isEnabled !== false && el?.state?.enabled !== false && Number(el?.opacity ?? 1) !== 0;
}

function isSyntheticElement(el) {
  return String(el?.fullType || "").startsWith("Microsoft.Maui.DevFlow.Agent.Core.");
}

function isActionableOrSynthetic(el) {
  return isSyntheticElement(el) || (Array.isArray(el?.traits) && el.traits.some((t) => String(t).toLowerCase() === "interactive"));
}

function normalizeText(value) {
  return String(value ?? "").trim();
}

function exactSelectorMatch(sel, el) {
  if (sel?.id != null) return String(el?.id) === String(sel.id);
  if (sel?.automationId != null) return String(el?.automationId ?? "") === String(sel.automationId);
  if (sel?.text != null) return normalizeText(el?.text) === normalizeText(sel.text);
  return false;
}

function visibleCandidateLabel(el) {
  const bits = [String(el?.id ?? "?")];
  if (el?.automationId) bits.push(`automationId=${el.automationId}`);
  if (el?.text) bits.push(`text=${el.text}`);
  bits.push(`type=${el?.type ?? "?"}`);
  return bits.join(" · ");
}

// The mutation lease is one global lock with a short TTL that any other DevFlow host
// (browser Inspector, VS Code, CLI, MCP) may legitimately hold for a moment — running
// several hosts against one app is a supported workflow. The shared client already marks
// `lease-held` as retriable but nothing acts on it, so a neighbouring host holding the lock
// for a second turns into a hard failure for the caller. Waiting briefly converts the
// transient case into success; the cap keeps a genuinely busy app from wedging the turn.
const LEASE_RETRY_BUDGET_MS = 6000;
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

export class DevflowDevice {
  constructor(opts = {}) {
    this.opts = { ...opts };
    this._agentPort = opts.agentPort ?? null;
    this._client = new DevFlowClient({
      agentPort: opts.agentPort,
      platform: opts.platform,
      device: opts.device,
      projectRoot: opts.projectRoot,
      brokerPort: opts.brokerPort,
      // The canvas historically auto-started the broker; keep that unless overridden.
      bootstrapBroker: opts.bootstrapBroker ?? "once",
      mutationLeaseId: opts.mutationLeaseId,
      mutationLeaseHolderKind: "canvas",
      mutationLeaseLabel: "GitHub Copilot Canvas",
    });
    this._eventHandle = null;
    this._screenshotDir = null;
    this._screenshotPath = null;
    this._info = {
      platform: opts.platform || "device",
      appName: "app",
      connected: false,
      density: 1,
      theme: null,
      window: { x: 0, y: 0, width: 0, height: 0 },
    };
  }

  info() {
    return this._info;
  }

  whichPort() {
    return this._client.target?.port ?? this._agentPort ?? null;
  }

  resolvedAgent() {
    return this._client.target?.registration ?? null;
  }

  transport() {
    return "http";
  }

  retarget({ platform, agentPort, device } = {}) {
    if (platform !== undefined) this.opts.platform = platform ? String(platform) : undefined;
    if (device !== undefined) this.opts.device = device ? String(device) : undefined;
    if (agentPort !== undefined) {
      const p = Number(agentPort);
      this._agentPort = Number.isFinite(p) && p > 0 ? p : null;
      this.opts.agentPort = this._agentPort ?? undefined;
    }
    // Close any live event stream first so it can't keep delivering the OLD agent's
    // events; the store re-opens a fresh stream against the new target after retarget.
    this._closeEventStream();
    this._client.retarget({ platform: this.opts.platform, agentPort: this.opts.agentPort, device: this.opts.device });
    this._info = { ...this._info, connected: false };
    return this;
  }

  async _ensureConnection() {
    const r = await this._client.connect();
    if (r.ok) this._info = { ...this._info, connected: true, agentPort: r.value.port };
    return { transport: r.ok ? "http" : null, port: r.ok ? r.value.port : null };
  }

  _applyStatus(d, ok = true) {
    if (!d || typeof d !== "object") return;
    const dev = d.device || {};
    const app = d.app || {};
    const agent = d.agent || {};
    const width = num(dev.windowWidth, this._info.window.width);
    const height = num(dev.windowHeight, this._info.window.height);
    this._info = {
      ...this._info,
      appName: app.name || this._info.appName,
      packageId: app.packageId,
      appVersion: app.version,
      platform: dev.platform || this.opts.platform || this._info.platform,
      idiom: dev.idiom,
      deviceType: dev.deviceType,
      density: num(dev.displayDensity, this._info.density || 1),
      framework: agent.framework,
      frameworkVersion: agent.frameworkVersion,
      capabilities: d.capabilities || {},
      connected: d.running === true || !!ok,
      agentPort: this.whichPort(),
      window: { x: 0, y: 0, width, height },
      raw: d,
    };
  }

  async status() {
    const r = await this._client.getStatus();
    return r.ok
      ? { ok: true, data: r.value, error: null }
      : { ok: false, data: null, error: r.error.message };
  }

  async refreshInfo() {
    const st = await this.status();
    this._applyStatus(st.data || {}, st.ok);
    return this._info;
  }

  async getRoots(depth = 0) {
    const r = await this._client.getTree(depth > 0 ? depth : undefined);
    if (!r.ok) return { ok: false, error: r.error.message, roots: [] };
    const roots = r.value || [];
    const window = largestWindowBounds(roots);
    if (window) this._info.window = window;
    return { ok: true, roots, window };
  }

  async getElement(id) {
    const r = await this._client.getElement(id);
    return r.ok ? r.value : null;
  }

  async query({ type, automationId, text, selector } = {}) {
    const r = selector
      ? await this._client.queryCss(selector)
      : await this._client.query({ type, automationId, text });
    return r.ok
      ? { ok: true, elements: r.value || [] }
      : { ok: false, error: r.error.message, elements: [] };
  }

  async hitTest(x, y) {
    const r = await this._client.hitTest(x, y);
    if (!r.ok) return { ok: false, error: r.error.message };
    const el = (r.value || [])[0] || null;
    return el ? { ok: true, element: el } : { ok: false, error: `no element at (${x}, ${y})` };
  }

  async getProperty(id, name) {
    const r = await this._client.getProperty(id, name);
    return r.ok ? { ok: true, value: r.value } : { ok: false, error: r.error.message };
  }

  // Back-compat shim: resolves to the first match. Kept for replay.mjs (legacy offline
  // fixture) and the recorder selftest. New callers should use _resolveTarget, which
  // refuses to guess between several matches.
  async _resolveElementId(sel) {
    if (sel == null) return null;
    if (typeof sel === "object") {
      if (sel.id != null) return String(sel.id);
      if (sel.automationId || sel.text) {
        const q = await this.query({ automationId: sel.automationId, text: sel.text });
        return q.ok && q.elements[0]?.id != null ? String(q.elements[0].id) : null;
      }
      return null;
    }
    return String(sel);
  }

  // Resolve a drive target (tap/fill) to EXACTLY one element id.
  //
  // A non-id selector that matches several elements is rejected instead of silently
  // resolving to the first hit. MAUI CollectionView/BindableLayout rows reuse the same
  // AutomationId and text for every row, so "the first match" is arbitrary — picking it
  // can fire a destructive action (delete, confirm, pay) on a row the caller never meant.
  // The error names the candidates so a caller can re-issue with an explicit id.
  async _resolveTarget(sel) {
    if (sel == null) return { error: "no target given — pass id, automationId, or text" };
    if (typeof sel !== "object") return { id: String(sel) };
    if (sel.id != null) return { id: String(sel.id) };
    if (!sel.automationId && !sel.text) {
      return { error: "no target given — pass id, automationId, or text" };
    }

    const label = sel.automationId
      ? `automationId "${sel.automationId}"`
      : `text "${sel.text}"`;
    const q = await this.query({ automationId: sel.automationId, text: sel.text });
    if (!q.ok) return { error: q.error || `could not resolve ${label}` };

    const matches = (q.elements || []).filter((el) => el?.id != null);
    const visible = matches.filter(isTapVisible);
    if (visible.length === 0) return { error: `no visible element matches ${label}` };

    const exactVisible = visible.filter((el) => exactSelectorMatch(sel, el));
    const actionableExact = exactVisible.filter(isActionableOrSynthetic);
    const actionableVisible = visible.filter(isActionableOrSynthetic);

    const pick = (items) => (items.length === 1 ? { id: String(items[0].id) } : null);
    const ambiguous = (items) => ({
      error:
        `${label} is ambiguous: ${items.length} visible elements match (${items.slice(0, 5).map(visibleCandidateLabel).join(", ")}${items.length > 5 ? `, +${items.length - 5} more` : ""}). ` +
        `Re-issue with an explicit id to choose one.`,
    });

    return pick(actionableExact) ||
      (actionableExact.length > 1 ? ambiguous(actionableExact) : null) ||
      pick(exactVisible) ||
      (exactVisible.length > 1 ? ambiguous(exactVisible) : null) ||
      pick(actionableVisible) ||
      (actionableVisible.length > 1 ? ambiguous(actionableVisible) : null) ||
      pick(visible) ||
      ambiguous(visible);
  }

  // Run a mutation, waiting out transient mutation-lease contention from another host.
  // Returns the raw client result so callers keep their existing success/error shapes.
  async _mutate(run) {
    const deadline = Date.now() + LEASE_RETRY_BUDGET_MS;
    for (let attempt = 1; ; attempt++) {
      const r = await run();
      if (r.ok || r.error?.kind !== "lease-held" || Date.now() >= deadline) return r;
      await sleep(Math.min(200 * attempt, 1000));
    }
  }

  async setProperty(id, name, value) {
    const r = await this._mutate(() => this._client.setProperty(id, name, String(value)));
    return r.ok ? { ok: true } : { ok: false, error: r.error.message };
  }

  async tap(sel) {
    const target = await this._resolveTarget(sel);
    if (target.error) return { ok: false, error: `tap: ${target.error}` };
    const r = await this._mutate(() => this._client.tap({ elementId: target.id }));
    return r.ok ? { ok: true } : { ok: false, error: r.error.message };
  }

  async fill(sel, text) {
    const target = await this._resolveTarget(sel);
    if (target.error) return { ok: false, error: `fill: ${target.error}` };
    const r = await this._mutate(() => this._client.fill(target.id, String(text)));
    return r.ok ? { ok: true } : { ok: false, error: r.error.message };
  }

  async scroll({ element, dx = 0, dy = 0, itemIndex, position, animated, x, y } = {}) {
    const args = { deltaX: num(dx), deltaY: num(dy), animated: !!animated };
    if (element) args.elementId = String(element);
    if (itemIndex != null) args.itemIndex = Number(itemIndex);
    if (position) args.scrollToPosition = String(position);
    // Coordinate scroll (inspector-style) is supported by the client too; keep it faithful.
    if (x != null && y != null) {
      args.x = num(x);
      args.y = num(y);
    }
    const r = await this._mutate(() => this._client.scroll(args));
    return r.ok ? { ok: true } : { ok: false, error: r.error.message };
  }

  async navigate(route) {
    const r = await this._mutate(() => this._client.navigate(String(route)));
    return r.ok ? { ok: true } : { ok: false, error: r.error.message };
  }

  async back() {
    const r = await this._mutate(() => this._client.back());
    return r.ok ? { ok: true } : { ok: false, error: r.error.message };
  }

  async resize(width, height) {
    const normalizedWidth = normalizeResizeDimension(width);
    const normalizedHeight = normalizeResizeDimension(height);
    if (normalizedWidth == null || normalizedHeight == null) {
      return { ok: false, error: "resize requires positive, finite width and height" };
    }
    const r = await this._mutate(() => this._client.resize(normalizedWidth, normalizedHeight));
    return r.ok ? { ok: true } : { ok: false, error: r.error.message };
  }

  async themeGet() {
    const r = await this._client.getTheme();
    if (r.ok && r.value) this._info.theme = r.value.requestedTheme || r.value.theme || this._info.theme;
    return r.ok ? { ok: true, data: r.value } : { ok: false, error: r.error.message };
  }

  async themeSet(theme) {
    const r = await this._mutate(() => this._client.setTheme(String(theme)));
    if (r.ok && r.value) this._info.theme = r.value.requestedTheme || r.value.theme || String(theme);
    return r.ok ? { ok: true, data: r.value } : { ok: false, error: r.error.message };
  }

  async logs(limit = 100) {
    const r = await this._client.getLogs(Number(limit) || 100);
    if (!r.ok) return { ok: false, error: r.error.message };
    let data = r.value;
    try {
      data = JSON.parse(r.value);
    } catch {
      /* leave as raw string */
    }
    return { ok: true, data };
  }

  async recordingStart({ name, preconditions } = {}) {
    const r = await this._client.controlMutationRecording("start", { name, preconditions });
    return r.ok ? r.value : { ok: false, error: r.error.message };
  }

  async recordingStatus() {
    const r = await this._client.controlMutationRecording("status");
    return r.ok ? r.value : { ok: false, error: r.error.message };
  }

  async recordingStop(recordingId) {
    const r = await this._client.controlMutationRecording("stop", { recordingId });
    return r.ok ? r.value : { ok: false, error: r.error.message };
  }

  async recordingCancel(recordingId) {
    const r = await this._client.controlMutationRecording("cancel", { recordingId });
    return r.ok ? r.value : { ok: false, error: r.error.message };
  }

  async claimMutationLease(force = false) {
    const r = await this._client.controlMutationLease("claim", !!force);
    return r.ok ? r.value : { ok: false, error: r.error.message };
  }

  async releaseMutationLease() {
    const r = await this._client.controlMutationLease("release");
    return r.ok ? r.value : { ok: false, error: r.error.message };
  }

  async listAgents() {
    const r = await this._client.listAgents();
    return r.ok ? r.value : [];
  }

  async screenshot() {
    let r;
    try {
      r = await this._client.screenshot({ scale: "auto" });
    } catch (e) {
      return { ok: false, error: String(e?.message || e) };
    }
    if (!r.ok) return { ok: false, error: r.error.message };
    if (!isPng(r.value)) return { ok: false, error: "screenshot returned no PNG data" };
    try {
      if (!this._screenshotDir) {
        this._screenshotDir = mkdtempSync(join(tmpdir(), "maui-live-canvas-"));
        this._screenshotPath = join(this._screenshotDir, "latest.png");
      }
      const file = this._screenshotPath;
      writeFileSync(file, r.value, { mode: 0o600 });
      return { ok: true, path: file, data: { via: "http" } };
    } catch (e) {
      return { ok: false, error: String(e?.message || e) };
    }
  }

  async elementShot(elementId) {
    const id = String(elementId || "").trim();
    if (!id) return { ok: false };
    let r;
    try {
      r = await this._client.screenshot({ elementId: id, scale: "auto" });
    } catch {
      return { ok: false };
    }
    if (r.ok && isPng(r.value)) return { ok: true, buffer: r.value, mimeType: "image/png" };
    return { ok: false };
  }

  _closeEventStream() {
    if (this._eventHandle) {
      try {
        this._eventHandle.close();
      } catch {
        /* ignore */
      }
      this._eventHandle = null;
    }
  }

  openEventStream(onEvent, onStatus = () => {}) {
    // Close any stream we previously opened so a retarget/re-open can't leave a stale
    // stream delivering the previous agent's events.
    this._closeEventStream();
    const handle = this._client.openEvents({
      onEvent: (e) => {
        try {
          onEvent(e);
        } catch {
          /* a throwing consumer must not kill the stream */
        }
      },
      onStatus: (s) => {
        try {
          onStatus({ connected: !!s.connected });
        } catch {
          /* ignore */
        }
      },
    });
    this._eventHandle = handle;
    return {
      close: () => {
        if (this._eventHandle === handle) this._eventHandle = null;
        try {
          handle.close();
        } catch {
          /* ignore */
        }
      },
    };
  }

  dispose() {
    this._closeEventStream();
    try {
      this._client.dispose();
    } finally {
      if (this._screenshotDir) {
        try {
          rmSync(this._screenshotDir, { recursive: true, force: true });
        } catch {
          /* best effort */
        }
        this._screenshotDir = null;
        this._screenshotPath = null;
      }
    }
  }
}
