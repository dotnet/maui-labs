// Public entry point: the DevFlowClient facade. It owns the resolver (mutex + cache +
// lifecycle) and executes typed agent requests through ONE path that centralizes retry,
// target attribution, and typed-error mapping. Reads auto-retry once on a genuine socket
// error; mutations do NOT (a lost response may mean the change was already applied),
// unless `retryMutations` is set.

import { discoverBroker } from "./broker.js";
import { randomBytes } from "node:crypto";
import { agentErrorMessage, buildQuery, requests } from "./agent.js";
import type { AgentRequest, GestureArgs, ScrollArgs } from "./agent.js";
import { httpJson, httpRaw, isConnError } from "./http.js";
import type { JsonResponse } from "./http.js";
import { openEventStream } from "./events.js";
import type { DevFlowEvent, EventStreamHandle, EventStreamStatus } from "./events.js";
import { optionsFromEnv } from "./env.js";
import { androidForwardingPolicy, Resolver } from "./resolve.js";
import type { ResolvedConnection, ResolverConfig } from "./resolve.js";
import { createTransport } from "./transport.js";
import type { Transport, TransportOptions } from "./transport.js";
import { err, ok } from "./types.js";
import type {
  AgentRegistration,
  AgentStatus,
  AgentTarget,
  DevFlowClientOptions,
  DevFlowResult,
  DevFlowTheme,
  ElementInfo,
  MutationLeaseStatus,
  MutationRecordingStatus,
  ThemeResult,
} from "./types.js";

export interface ScreenshotOptions {
  window?: number;
  elementId?: string;
  selector?: string;
  maxWidth?: number;
  scale?: string;
}

export interface OpenEventsHandlers {
  onEvent: (e: DevFlowEvent) => void;
  onStatus?: (s: EventStreamStatus) => void;
  events?: string[];
}

/** Tap target: by element id, or by viewport coordinates. */
export interface TapTarget {
  elementId?: string;
  x?: number;
  y?: number;
}

export class DevFlowClient {
  private readonly resolver: Resolver;
  private readonly requestTimeoutMs: number;
  private readonly retryMutations: boolean;
  private readonly bootstrapBroker: DevFlowClientOptions["bootstrapBroker"];
  private readonly mauiCliPath?: string;
  private readonly brokerPort?: number;
  private readonly mutationLeaseId: string;
  private readonly mutationLeaseHolderKind: string;
  private readonly mutationLeaseLabel?: string;
  private readonly autoAcquireMutationLease: boolean;
  private readonly streams = new Set<EventStreamHandle>();
  private disposed = false;

  constructor(options: DevFlowClientOptions = {}) {
    this.requestTimeoutMs = options.requestTimeoutMs ?? 8000;
    this.retryMutations = options.retryMutations ?? false;
    this.bootstrapBroker = options.bootstrapBroker ?? "never";
    this.mauiCliPath = options.mauiCliPath;
    this.brokerPort = options.brokerPort;
    this.mutationLeaseId = options.mutationLeaseId ?? randomLeaseId();
    this.mutationLeaseHolderKind = options.mutationLeaseHolderKind ?? "node-client";
    this.mutationLeaseLabel = options.mutationLeaseLabel;
    this.autoAcquireMutationLease = options.autoAcquireMutationLease ?? true;

    const adbPolicy = androidForwardingPolicy(options);
    const config: ResolverConfig = {
      agentPort: options.agentPort,
      platform: options.platform,
      device: options.device,
      projectRoot: options.projectRoot,
      brokerPort: options.brokerPort,
      bootstrapBroker: this.bootstrapBroker,
      allowAmbiguousMostRecent: options.allowAmbiguousMostRecent ?? false,
      adbMode: options.adb,
      // Auto mode keeps the forwarder available for broker-discovered Android agents even when
      // the caller did not know the target platform up front. Blind ADB fallback scanning remains
      // opt-in through an Android hint or adb:true, avoiding adb startup on ordinary desktop misses.
      adbEnabled: adbPolicy.enabled,
      adbFallbackScan: adbPolicy.fallbackScan,
      adbPath: options.adbPath,
      mauiCliPath: options.mauiCliPath,
      probeTimeoutMs: options.probeTimeoutMs ?? 600,
    };
    this.resolver = new Resolver(config);
  }

  /** Build a client from MAUI_DEVFLOW_* environment variables, with optional overrides. */
  static fromEnv(overrides: DevFlowClientOptions = {}): DevFlowClient {
    return new DevFlowClient({ ...optionsFromEnv(), ...overrides });
  }

  /** Identity of the currently resolved agent (null until a call resolves one). */
  get target(): AgentTarget | null {
    return this.resolver.current?.target ?? null;
  }

  // ── Discovery / lifecycle ──────────────────────────────────────────────────

  /** List every agent the broker knows about (does not resolve/verify a single one). */
  async listAgents(): Promise<DevFlowResult<AgentRegistration[]>> {
    if (this.disposed) return this.disposedErr("listAgents");
    const broker = await discoverBroker({
      bootstrap: this.bootstrapBroker ?? "never",
      cliPath: this.mauiCliPath,
      brokerPort: this.brokerPort,
    });
    if (!broker) {
      return err({
        kind: "broker-not-found",
        message: "DevFlow broker not found. Start a MAUI app with the agent, or set bootstrapBroker.",
        operation: "listAgents",
        retriable: true,
      });
    }
    return ok(broker.agents);
  }

  /** Resolve (and verify) the target agent, returning its connection details. */
  async connect(force = false): Promise<DevFlowResult<ResolvedConnection>> {
    if (this.disposed) return this.disposedErr("connect");
    return this.resolver.resolve(force);
  }

  /** Re-point the client at a different agent (clears the resolution cache). */
  retarget(patch: Pick<DevFlowClientOptions, "agentPort" | "platform" | "device" | "projectRoot">): this {
    this.resolver.retarget(patch);
    return this;
  }

  // ── Reads ──────────────────────────────────────────────────────────────────

  getStatus(window?: number): Promise<DevFlowResult<AgentStatus | null>> {
    return this.run(requests.status(window));
  }
  getTree(depth?: number, window?: number): Promise<DevFlowResult<ElementInfo[]>> {
    return this.run(requests.tree(depth, window));
  }
  getElement(id: string): Promise<DevFlowResult<ElementInfo | null>> {
    return this.run(requests.element(id));
  }
  query(q: { type?: string; automationId?: string; text?: string }): Promise<DevFlowResult<ElementInfo[]>> {
    return this.run(requests.query(q));
  }
  queryCss(selector: string): Promise<DevFlowResult<ElementInfo[]>> {
    return this.run(requests.queryCss(selector));
  }
  hitTest(x: number, y: number, window?: number): Promise<DevFlowResult<ElementInfo[]>> {
    return this.run(requests.hitTest(x, y, window));
  }
  getProperty(id: string, name: string): Promise<DevFlowResult<string | null>> {
    return this.run(requests.getProperty(id, name));
  }
  getTheme(): Promise<DevFlowResult<ThemeResult | null>> {
    return this.run(requests.themeGet());
  }

  // ── Mutations ──────────────────────────────────────────────────────────────

  async tap(target: TapTarget): Promise<DevFlowResult<void>> {
    if (target.elementId) return this.run(requests.tapElement(target.elementId));
    if (typeof target.x === "number" && typeof target.y === "number") {
      // The agent has no coordinate tap; resolve to an element via hit-test, then tap the
      // first element that accepts it (most-specific → general), like the InspectorServer.
      const hit = await this.hitTest(target.x, target.y);
      if (!hit.ok) return hit;
      for (const el of hit.value) {
        if (!el.id) continue;
        const r = await this.run(requests.tapElement(el.id));
        if (r.ok) return r;
        // Only advance to the next element on a semantic rejection (delivered, not
        // accepted). A transport/HTTP error must stop here — retrying would risk a
        // double-apply if the first tap actually reached the app.
        if (r.error.kind !== "action-rejected") return r;
      }
      return err({ kind: "not-found", message: `no tappable element at (${target.x}, ${target.y})`, operation: "tap", target: this.target ?? undefined, retriable: false });
    }
    return err({ kind: "invalid-argument", message: "tap requires elementId or x/y", operation: "tap", retriable: false });
  }
  fill(elementId: string, text: string): Promise<DevFlowResult<void>> {
    return this.run(requests.fill(elementId, text));
  }
  clear(elementId: string): Promise<DevFlowResult<void>> {
    return this.run(requests.clear(elementId));
  }
  focus(elementId: string): Promise<DevFlowResult<void>> {
    return this.run(requests.focus(elementId));
  }
  async scroll(args: ScrollArgs): Promise<DevFlowResult<void>> {
    if (!args.elementId && typeof args.x === "number" && typeof args.y === "number") {
      // Coordinate scroll: hit-test, then scroll the first element that accepts the delta;
      // fall back to a global scroll. Mirrors the InspectorServer's translation.
      const hit = await this.hitTest(args.x, args.y);
      if (!hit.ok) return hit;
      for (const el of hit.value) {
        if (!el.id) continue;
        const r = await this.run(requests.scroll({ elementId: el.id, deltaX: args.deltaX, deltaY: args.deltaY, animated: args.animated }));
        if (r.ok) return r;
        // As with tap: fall through only on a semantic rejection, never on a transport error.
        if (r.error.kind !== "action-rejected") return r;
      }
      return this.run(requests.scroll({ deltaX: args.deltaX, deltaY: args.deltaY, animated: args.animated }));
    }
    return this.run(requests.scroll(args));
  }
  gesture(args: GestureArgs): Promise<DevFlowResult<void>> {
    return this.run(requests.gesture(args));
  }
  back(): Promise<DevFlowResult<void>> {
    return this.run(requests.back());
  }
  navigate(route: string): Promise<DevFlowResult<void>> {
    return this.run(requests.navigate(route));
  }
  key(key: string, elementId?: string, text?: string): Promise<DevFlowResult<void>> {
    return this.run(requests.key(key, elementId, text));
  }
  resize(width: number, height: number, window?: number): Promise<DevFlowResult<void>> {
    return this.run(requests.resize(width, height, window));
  }
  setProperty(id: string, name: string, value: string): Promise<DevFlowResult<void>> {
    return this.run(requests.setProperty(id, name, value));
  }
  setTheme(theme: DevFlowTheme): Promise<DevFlowResult<ThemeResult | null>> {
    return this.run(requests.themeSet(theme));
  }

  async controlMutationLease(
    action: "claim" | "status" | "heartbeat" | "release",
    force = false,
  ): Promise<DevFlowResult<MutationLeaseStatus>> {
    if (this.disposed) return this.disposedErr("mutationLease");
    const resolved = await this.resolver.resolve();
    if (!resolved.ok) return { ok: false, error: resolved.error };
    const conn = resolved.value;
    const r = await httpJson(conn.port, "POST", "/api/v1/agent/lease", {
      timeoutMs: this.requestTimeoutMs,
      headers: this.mutationHeaders(),
      json: {
        action,
        leaseId: this.mutationLeaseId,
        holderKind: this.mutationLeaseHolderKind,
        label: this.mutationLeaseLabel,
        force,
      },
    });
    if (r.status === 404) {
      return ok({
        ok: true,
        allowed: true,
        youHold: true,
        heldByOther: false,
        authority: "unsupported",
      }, conn.target);
    }
    if (!r.ok || !r.data || typeof r.data !== "object") {
      return err({
        kind: "http",
        message: `mutation lease ${action} failed (${r.error ?? `HTTP ${r.status}`})`,
        operation: "mutationLease",
        target: conn.target,
        status: r.status,
        retriable: true,
      });
    }
    return ok(r.data as MutationLeaseStatus, conn.target);
  }

  async controlMutationRecording(
    action: "start" | "status" | "stop" | "cancel",
    options: { name?: string; app?: string; platform?: string; preconditions?: string; recordingId?: string } = {},
  ): Promise<DevFlowResult<MutationRecordingStatus>> {
    if (this.disposed) return this.disposedErr("mutationRecording");
    const resolved = await this.resolver.resolve();
    if (!resolved.ok) return { ok: false, error: resolved.error };
    const conn = resolved.value;
    if (action !== "status" && this.autoAcquireMutationLease) {
      const lease = await this.ensureMutationLease(conn);
      if (!lease.ok) return { ok: false, error: lease.error };
    }
    const r = await httpJson(conn.port, "POST", "/api/v1/agent/recording", {
      timeoutMs: this.requestTimeoutMs,
      headers: this.mutationHeaders(),
      json: { action, ...options },
    });
    if (!r.ok || !r.data || typeof r.data !== "object") {
      return err({
        kind: r.status === 409 ? "lease-held" : "http",
        message: `mutation recording ${action} failed (${agentErrorMessage(r.data) ?? r.error ?? `HTTP ${r.status}`})`,
        operation: "mutationRecording",
        target: conn.target,
        status: r.status,
        retriable: r.status === 409 || r.status >= 500,
      });
    }
    return ok(r.data as MutationRecordingStatus, conn.target);
  }

  // ── Binary / text reads (handled outside run() because they aren't JSON) ─────

  async screenshot(opts: ScreenshotOptions = {}): Promise<DevFlowResult<Buffer>> {
    if (this.disposed) return this.disposedErr("screenshot");
    const path = `/api/v1/ui/screenshot${buildQuery({
      window: opts.window,
      elementId: opts.elementId,
      selector: opts.selector,
      maxWidth: opts.maxWidth,
      scale: opts.scale,
    })}`;
    const resolved = await this.resolver.resolve();
    if (!resolved.ok) return { ok: false, error: resolved.error };
    let conn = resolved.value;
    let r = await httpRaw(conn.port, "GET", path, { timeoutMs: this.requestTimeoutMs });
    if (isConnError(r)) {
      this.resolver.invalidate();
      const re = await this.resolver.resolve(true);
      if (!re.ok) return { ok: false, error: re.error };
      conn = re.value;
      r = await httpRaw(conn.port, "GET", path, { timeoutMs: this.requestTimeoutMs });
    }
    if (!r.ok || !r.buffer || r.buffer.length === 0) {
      return err({
        kind: r.error === "timeout" ? "timeout" : isConnError(r) ? "agent-unreachable" : "http",
        message: `screenshot failed (${r.error ?? `HTTP ${r.status}`})`,
        operation: "screenshot",
        target: conn.target,
        status: r.status,
        retriable: true,
      });
    }
    return ok(r.buffer, conn.target);
  }

  async getLogs(limit = 100, skip = 0, source?: string): Promise<DevFlowResult<string>> {
    if (this.disposed) return this.disposedErr("getLogs");
    const path = `/api/v1/logs${buildQuery({ limit, skip, source: source && source !== "all" ? source : undefined })}`;
    const resolved = await this.resolver.resolve();
    if (!resolved.ok) return { ok: false, error: resolved.error };
    let conn = resolved.value;
    let r = await httpRaw(conn.port, "GET", path, { timeoutMs: this.requestTimeoutMs });
    if (isConnError(r)) {
      this.resolver.invalidate();
      const re = await this.resolver.resolve(true);
      if (!re.ok) return { ok: false, error: re.error };
      conn = re.value;
      r = await httpRaw(conn.port, "GET", path, { timeoutMs: this.requestTimeoutMs });
    }
    if (!r.ok) {
      return err({
        kind: r.error === "timeout" ? "timeout" : isConnError(r) ? "agent-unreachable" : "http",
        message: `getLogs failed (${r.error ?? `HTTP ${r.status}`})`,
        operation: "getLogs",
        target: conn.target,
        status: r.status,
        retriable: true,
      });
    }
    return ok(r.buffer ? r.buffer.toString("utf8") : "", conn.target);
  }

  // ── Events + transport seam ──────────────────────────────────────────────────

  openEvents(handlers: OpenEventsHandlers): EventStreamHandle {
    const handle = openEventStream({
      resolvePort: async () => {
        const r = await this.resolver.resolve();
        return r.ok ? r.value.port : null;
      },
      supportsEvents: async (port) => {
        const capabilities = await httpJson(
          port,
          "GET",
          "/api/v1/agent/capabilities",
          { timeoutMs: this.requestTimeoutMs },
        );
        if (!capabilities.ok)
          return capabilities.status === 404 ? false : null;
        if (!capabilities.data || typeof capabilities.data !== "object") return null;
        const domains = (capabilities.data as Record<string, unknown>).capabilities;
        if (!domains || typeof domains !== "object") return false;
        const events = (domains as Record<string, unknown>)["ui.events"];
        if (!events || typeof events !== "object") return false;
        const features = (events as Record<string, unknown>).features;
        return Array.isArray(features) && features.includes("stream");
      },
      onEvent: handlers.onEvent,
      onStatus: handlers.onStatus,
      events: handlers.events,
    });
    this.streams.add(handle);
    return {
      close: () => {
        this.streams.delete(handle);
        handle.close();
      },
    };
  }

  /** Create a permission-gated, validated Transport (for a Canvas/VS Code host proxy). */
  createTransport(opts: TransportOptions = {}): Transport {
    return createTransport(this, opts);
  }

  dispose(): void {
    this.disposed = true;
    for (const s of this.streams) {
      try {
        s.close();
      } catch {
        /* ignore */
      }
    }
    this.streams.clear();
    this.resolver.dispose();
  }

  // ── Internals ────────────────────────────────────────────────────────────────

  private async run<T>(req: AgentRequest<T>): Promise<DevFlowResult<T>> {
    if (this.disposed) return this.disposedErr(req.operation);
    const resolved = await this.resolver.resolve();
    if (!resolved.ok) return { ok: false, error: resolved.error };
    let conn = resolved.value;
    if (!req.idempotent && this.autoAcquireMutationLease) {
      const lease = await this.ensureMutationLease(conn);
      if (!lease.ok) return { ok: false, error: lease.error };
    }
    let r = await httpJson(conn.port, req.method, req.path, {
      json: req.body,
      timeoutMs: this.requestTimeoutMs,
      headers: req.idempotent ? undefined : this.mutationHeaders(),
    });
    if (isConnError(r) && (req.idempotent || this.retryMutations)) {
      this.resolver.invalidate();
      const re = await this.resolver.resolve(true);
      if (!re.ok) return { ok: false, error: re.error };
      conn = re.value;
      r = await httpJson(conn.port, req.method, req.path, {
        json: req.body,
        timeoutMs: this.requestTimeoutMs,
        headers: req.idempotent ? undefined : this.mutationHeaders(),
      });
    }
    return this.mapResponse(req, r, conn.target);
  }

  private async ensureMutationLease(conn: ResolvedConnection): Promise<DevFlowResult<MutationLeaseStatus>> {
    const r = await httpJson(conn.port, "POST", "/api/v1/agent/lease", {
      timeoutMs: this.requestTimeoutMs,
      headers: this.mutationHeaders(),
      json: {
        action: "claim",
        leaseId: this.mutationLeaseId,
        holderKind: this.mutationLeaseHolderKind,
        label: this.mutationLeaseLabel,
        force: false,
      },
    });
    const status = r.data && typeof r.data === "object" ? (r.data as MutationLeaseStatus) : null;
    if (r.status === 404) {
      return ok({
        ok: true,
        allowed: true,
        youHold: true,
        heldByOther: false,
        authority: "unsupported",
      }, conn.target);
    }
    if (!r.ok || !status?.youHold) {
      return err({
        kind: "lease-held",
        message: status?.heldByOther
          ? `Another DevFlow session is driving this app (${status.label ?? status.holderKind ?? "unknown holder"}).`
          : status?.error ?? "Could not acquire the DevFlow mutation lease.",
        operation: "mutationLease",
        target: conn.target,
        status: r.status || 409,
        retriable: true,
      });
    }
    return ok(status, conn.target);
  }

  private mutationHeaders(): Record<string, string> {
    return {
      "X-DevFlow-Lease": this.mutationLeaseId,
      "X-DevFlow-Holder": this.mutationLeaseHolderKind,
      ...(this.mutationLeaseLabel ? { "X-DevFlow-Label": this.mutationLeaseLabel } : {}),
    };
  }

  private mapResponse<T>(req: AgentRequest<T>, r: JsonResponse, target: AgentTarget): DevFlowResult<T> {
    if (!r.ok) {
      if (r.error === "timeout") {
        return err({ kind: "timeout", message: `${req.operation} timed out after ${this.requestTimeoutMs}ms`, operation: req.operation, target, retriable: true, cause: r.error });
      }
      if (isConnError(r)) {
        return err({ kind: "agent-unreachable", message: `agent unreachable during ${req.operation} (${r.error})`, operation: req.operation, target, retriable: true, cause: r.error });
      }
      const msg = agentErrorMessage(r.data) ?? `HTTP ${r.status}`;
      if (r.status === 409 && r.data && typeof r.data === "object" && (r.data as Record<string, unknown>).reason === "lease") {
        const details = (r.data as Record<string, unknown>).details as Record<string, unknown> | undefined;
        return err({
          kind: "lease-held",
          message: `${req.operation} blocked: ${msg}${details?.label ? ` (${String(details.label)})` : ""}`,
          operation: req.operation,
          target,
          status: r.status,
          retriable: true,
        });
      }
      // The real agent returns HTTP 400 (HttpResponse.Error) with {success:false,error} for
      // a semantic ACTION decline (element not tappable/scrollable, gone, etc.). Classify a
      // 4xx from an action endpoint (req.appError set) as a semantic rejection so composite
      // coordinate ops can safely advance to the next element. Connection errors, timeouts,
      // and 5xx are NOT semantic declines and must stop the composite (no double-apply).
      if (req.appError && r.status >= 400 && r.status < 500) {
        return err({ kind: "action-rejected", message: `${req.operation}: ${msg}`, operation: req.operation, target, status: r.status, retriable: false });
      }
      return err({ kind: "http", message: `${req.operation} failed: ${msg}`, operation: req.operation, target, status: r.status, bodySnippet: snippet(r.data), retriable: r.status >= 500 });
    }
    if (req.appError) {
      const m = req.appError(r.data);
      // 2xx but the agent declined the action (e.g. element not tappable/scrollable). This
      // is a SEMANTIC rejection — distinct from transport/HTTP errors — so composite
      // coordinate ops can safely try the next element without risking a double-apply.
      if (m) return err({ kind: "action-rejected", message: `${req.operation}: ${m}`, operation: req.operation, target, status: r.status, retriable: false });
    }
    return ok(req.parse(r.data), target);
  }

  private disposedErr<T>(operation: string): DevFlowResult<T> {
    return err({ kind: "disposed", message: "DevFlow client has been disposed.", operation, retriable: false });
  }
}

function snippet(data: unknown): string | undefined {
  if (data == null) return undefined;
  try {
    const s = typeof data === "string" ? data : JSON.stringify(data);
    return s.length > 200 ? `${s.slice(0, 200)}…` : s;
  } catch {
    return undefined;
  }
}

// ── Public re-exports ──────────────────────────────────────────────────────────
export * from "./types.js";
export { optionsFromEnv, parsePort } from "./env.js";
export { openEventStream } from "./events.js";
export type { DevFlowEvent, EventStreamHandle, EventStreamStatus, EventStreamOptions } from "./events.js";
export type { ScrollArgs, GestureArgs, AgentRequest } from "./agent.js";
export { toRoots, buildQuery, requests } from "./agent.js";
export type { ResolvedConnection } from "./resolve.js";
export {
  selectRegistration,
  selectLive,
  isProjectInRoot,
  candidatePorts,
  normPath,
} from "./resolve.js";
export {
  createTransport,
  READ_ONLY,
  READ_SCREENSHOT,
  INTERACT,
  FULL,
} from "./transport.js";

function randomLeaseId(): string {
  return randomBytes(16).toString("hex");
}
export type { Transport, TransportClient, TransportOptions, SeamOp, SeamPermissions } from "./transport.js";
export { brokerStatePath, readBrokerState, discoverBroker } from "./broker.js";
export type { BrokerDiscovery } from "./broker.js";
export { isConnError } from "./http.js";
