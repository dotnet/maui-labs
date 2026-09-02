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

function largestWindowBounds(roots) {
  let largest = null;
  let largestArea = 0;
  const visit = (nodes) => {
    for (const node of nodes || []) {
      const bounds = node?.windowBounds;
      const width = num(bounds?.width);
      const height = num(bounds?.height);
      const area = width * height;
      if (area > largestArea) {
        largestArea = area;
        largest = { x: num(bounds?.x), y: num(bounds?.y), width, height };
      }
      visit(node?.children);
    }
  };
  visit(roots);
  return largest;
}

// PNG magic: 0x89 'P' ... (8-byte signature). Cheap guard so we never write a JSON error
// body or a truncated buffer to a .png file.
const isPng = (b) => !!b && b.length > 8 && b[0] === 0x89 && b[1] === 0x50;

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
      mutationLeaseHolderKind: "copilot-canvas",
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

  async setProperty(id, name, value) {
    const r = await this._client.setProperty(id, name, String(value));
    return r.ok ? { ok: true } : { ok: false, error: r.error.message };
  }

  async tap(sel) {
    const id = await this._resolveElementId(sel);
    if (!id) return { ok: false, error: "tap: could not resolve element" };
    const r = await this._client.tap({ elementId: id });
    return r.ok ? { ok: true } : { ok: false, error: r.error.message };
  }

  async fill(sel, text) {
    const id = await this._resolveElementId(sel);
    if (!id) return { ok: false, error: "fill: could not resolve element" };
    const r = await this._client.fill(id, String(text));
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
    const r = await this._client.scroll(args);
    return r.ok ? { ok: true } : { ok: false, error: r.error.message };
  }

  async navigate(route) {
    const r = await this._client.navigate(String(route));
    return r.ok ? { ok: true } : { ok: false, error: r.error.message };
  }

  async back() {
    const r = await this._client.back();
    return r.ok ? { ok: true } : { ok: false, error: r.error.message };
  }

  async resize(width, height) {
    const r = await this._client.resize(Number(width), Number(height));
    return r.ok ? { ok: true } : { ok: false, error: r.error.message };
  }

  async themeGet() {
    const r = await this._client.getTheme();
    if (r.ok && r.value) this._info.theme = r.value.requestedTheme || r.value.theme || this._info.theme;
    return r.ok ? { ok: true, data: r.value } : { ok: false, error: r.error.message };
  }

  async themeSet(theme) {
    const r = await this._client.setTheme(String(theme));
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
