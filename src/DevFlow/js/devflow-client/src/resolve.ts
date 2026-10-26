// Agent resolution: broker-first (authoritative registry) → verify live → fast port-scan
// fallback. Selection REFUSES to silently pick among multiple matches (returns an
// `agent-ambiguous` error with candidates) unless `allowAmbiguousMostRecent` is set —
// silently mutating the wrong app is dangerous once mutations are exposed. Resolution is
// memoized behind a mutex so concurrent first-calls share ONE resolution.

import { AdbForwarder } from "./adb.js";
import { discoverBroker } from "./broker.js";
import { probeStatus } from "./probe.js";
import { err, ok } from "./types.js";
import type {
  AgentRegistration,
  AgentStatus,
  AgentTarget,
  BootstrapPolicy,
  DevFlowResult,
} from "./types.js";

const SCAN_BASE = 10223;
const SCAN_COUNT = 20;
const DEFAULT_AGENT_PORT = 9223;

// ── Pure selection helpers (unit-tested) ─────────────────────────────────────

export function androidForwardingPolicy(options: {
  platform?: string;
  device?: string;
  adb?: boolean;
}): { enabled: boolean; fallbackScan: boolean } {
  const platform = options.platform?.trim();
  const hasAndroidHint = platform ? /android/i.test(platform) : !!options.device;
  const enabled = options.adb ?? true;
  return {
    enabled,
    fallbackScan: enabled && (options.adb === true || hasAndroidHint),
  };
}

export function normPath(p?: string | null): string {
  if (!p) return "";
  const trimmed = String(p).replace(/[\\/]+$/, "");
  return process.platform === "win32" ? trimmed.toLowerCase() : trimmed;
}

export function isProjectInRoot(project: string | undefined | null, root: string | undefined | null): boolean {
  const r = normPath(root);
  const pr = normPath(project);
  if (!r || !pr) return false;
  return pr === r || pr.startsWith(`${r}\\`) || pr.startsWith(`${r}/`);
}

function connectedAtMs(s?: string): number {
  const t = s ? Date.parse(s) : Number.NaN;
  return Number.isFinite(t) ? t : 0;
}

export interface SelectionOpts {
  agentPort?: number;
  projectRoot?: string;
  platform?: string;
  allowAmbiguousMostRecent?: boolean;
}

export type SelectionOutcome<T> =
  | { kind: "unique"; agent: T }
  | { kind: "none" }
  | { kind: "ambiguous"; candidates: T[] };

/**
 * Select a broker registration for the given filters: pinned port (exact) → project
 * root → platform/TFM. Unique-or-ambiguous; never a silent "most recent" unless opted in.
 */
export function selectRegistration(
  agents: AgentRegistration[],
  opts: SelectionOpts,
): SelectionOutcome<AgentRegistration> {
  let cands = (agents || []).filter((a) => a && typeof a.port === "number");
  if (!cands.length) return { kind: "none" };

  if (opts.agentPort) {
    const pinned = cands.find((a) => a.port === opts.agentPort);
    return pinned ? { kind: "unique", agent: pinned } : { kind: "none" };
  }
  if (opts.projectRoot) {
    const m = cands.filter((a) => isProjectInRoot(a.project, opts.projectRoot));
    if (m.length) cands = m;
  }
  if (opts.platform) {
    const want = opts.platform.toLowerCase();
    const m = cands.filter(
      (a) =>
        String(a.platform || "").toLowerCase().includes(want) ||
        String(a.tfm || "").toLowerCase().includes(want),
    );
    if (m.length) cands = m;
  }
  if (cands.length === 1) return { kind: "unique", agent: cands[0] };
  if (opts.allowAmbiguousMostRecent) {
    const sorted = [...cands].sort((a, b) => connectedAtMs(b.connectedAt) - connectedAtMs(a.connectedAt));
    return { kind: "unique", agent: sorted[0] };
  }
  return { kind: "ambiguous", candidates: cands };
}

export function candidatePorts(agentPort?: number): number[] {
  const set = new Set<number>();
  if (agentPort) set.add(agentPort);
  set.add(DEFAULT_AGENT_PORT);
  for (let i = 0; i < SCAN_COUNT; i++) set.add(SCAN_BASE + i);
  return [...set];
}

export function androidRegistrationPorts(
  registrations: AgentRegistration[],
  fallbackToHint: boolean,
): number[] {
  return registrations
    .filter((registration) => {
      const metadata = `${registration.platform ?? ""} ${registration.tfm ?? ""}`.trim();
      return /android/i.test(metadata) || (!metadata && fallbackToHint);
    })
    .map((registration) => registration.port);
}

export interface LiveAgent {
  port: number;
  status: AgentStatus;
  running: boolean;
  platform?: string;
  appName?: string;
}

/** Select among live (scanned) agents. Same unique-or-ambiguous discipline as the broker path. */
export function selectLive(live: LiveAgent[], opts: SelectionOpts): SelectionOutcome<LiveAgent> {
  if (!live.length) return { kind: "none" };
  if (opts.agentPort) {
    const pinned = live.find((a) => a.port === opts.agentPort);
    return pinned ? { kind: "unique", agent: pinned } : { kind: "none" };
  }
  let cands = live.slice();
  if (opts.platform) {
    const want = opts.platform.toLowerCase();
    const m = cands.filter((a) => String(a.platform || "").toLowerCase().includes(want));
    if (m.length) cands = m;
  }
  const running = cands.filter((a) => a.running);
  if (running.length) cands = running;
  if (cands.length === 1) return { kind: "unique", agent: cands[0] };
  if (opts.allowAmbiguousMostRecent) return { kind: "unique", agent: cands[0] };
  return { kind: "ambiguous", candidates: cands };
}

// ── Stateful resolver (mutex + cache + lifecycle) ────────────────────────────

export interface ResolverConfig {
  agentPort?: number;
  platform?: string;
  device?: string;
  projectRoot?: string;
  brokerPort?: number;
  bootstrapBroker: BootstrapPolicy;
  allowAmbiguousMostRecent: boolean;
  adbMode?: boolean;
  adbEnabled: boolean;
  adbFallbackScan: boolean;
  adbPath?: string;
  mauiCliPath?: string;
  probeTimeoutMs: number;
}

export interface ResolvedConnection {
  port: number;
  target: AgentTarget;
  status: AgentStatus;
  brokerPort?: number;
}

type RetargetPatch = Partial<Pick<ResolverConfig, "agentPort" | "platform" | "device" | "projectRoot">>;

export class Resolver {
  private cfg: ResolverConfig;
  private cached: ResolvedConnection | null = null;
  private inflight: Promise<DevFlowResult<ResolvedConnection>> | null = null;
  private adb: AdbForwarder | null = null;
  private disposed = false;
  private epoch = 0;

  constructor(cfg: ResolverConfig) {
    this.cfg = cfg;
  }

  get current(): ResolvedConnection | null {
    return this.cached;
  }

  get config(): Readonly<ResolverConfig> {
    return this.cfg;
  }

  retarget(patch: RetargetPatch): void {
    const next = { ...this.cfg, ...patch };
    const adbPolicy = androidForwardingPolicy({
      platform: next.platform,
      device: next.device,
      adb: next.adbMode,
    });
    this.cfg = {
      ...next,
      adbEnabled: adbPolicy.enabled,
      adbFallbackScan: adbPolicy.fallbackScan,
    };
    this.cached = null;
    this.inflight = null;
    this.adb = null;
    this.epoch++;
  }

  /** Drop the cached connection so the next call re-resolves (e.g. after a socket error). */
  invalidate(): void {
    this.cached = null;
    this.epoch++;
  }

  dispose(): void {
    this.disposed = true;
    this.cached = null;
    this.inflight = null;
    this.epoch++;
  }

  async resolve(force = false): Promise<DevFlowResult<ResolvedConnection>> {
    if (this.disposed) {
      return err({ kind: "disposed", message: "DevFlow client has been disposed.", operation: "resolve", retriable: false });
    }
    if (this.cached && !force) return ok(this.cached, this.cached.target);
    if (this.inflight && !force) return this.inflight;
    // A forced resolve supersedes any in-flight (non-force) one: bump the epoch so the
    // older resolution cannot overwrite this fresher result via cache() when it completes.
    const myEpoch = ++this.epoch;
    const p = this.doResolve(myEpoch);
    this.inflight = p;
    try {
      return await p;
    } finally {
      if (this.inflight === p) this.inflight = null;
    }
  }

  private wantAndroid(): boolean {
    const platform = this.cfg.platform?.trim();
    return platform ? /android/i.test(platform) : !!this.cfg.device;
  }

  private forwarder(): AdbForwarder | null {
    if (!this.cfg.adbEnabled) return null;
    if (!this.adb) this.adb = new AdbForwarder({ adbPath: this.cfg.adbPath, device: this.cfg.device });
    return this.adb;
  }

  private async doResolve(epoch: number): Promise<DevFlowResult<ResolvedConnection>> {
    const selOpts: SelectionOpts = {
      agentPort: this.cfg.agentPort,
      platform: this.cfg.platform,
      projectRoot: this.cfg.projectRoot,
      allowAmbiguousMostRecent: this.cfg.allowAmbiguousMostRecent,
    };

    // 1) Broker-first: the official registry.
    const broker = await discoverBroker({
      bootstrap: this.cfg.bootstrapBroker,
      cliPath: this.cfg.mauiCliPath,
      brokerPort: this.cfg.brokerPort,
    });

    if (broker && broker.agents.length) {
      // Do NOT collapse ambiguity to "most recent" here — defer that until AFTER liveness
      // probing (below), so allowAmbiguousMostRecent picks the newest *live* broker candidate.
      const outcome = selectRegistration(broker.agents, { ...selOpts, allowAmbiguousMostRecent: false });
      if (outcome.kind === "unique") {
        const reg = outcome.agent;
        const androidPorts = androidRegistrationPorts([reg], this.wantAndroid());
        if (androidPorts.length) await this.forwarder()?.ensureForwards(androidPorts);
        const status = await probeStatus(reg.port, this.cfg.probeTimeoutMs);
        if (status) return this.cache(reg.port, status, reg, broker.port, epoch);
        // Broker named this port but it's dead → fall through to scan.
      } else if (outcome.kind === "ambiguous") {
        // Disambiguate stale-vs-live: a leaked/dead registration must not block a live one.
        // Probe every candidate; only truly ambiguous when more than one is actually alive.
        const cands = outcome.candidates;
        const androidPorts = androidRegistrationPorts(cands, this.wantAndroid());
        if (androidPorts.length) await this.forwarder()?.ensureForwards(androidPorts);
        const liveRegs = await this.probeRegistrations(cands);
        if (liveRegs.length === 1) {
          const only = liveRegs[0];
          return this.cache(only.reg.port, only.status, only.reg, broker.port, epoch);
        }
        if (liveRegs.length > 1) {
          if (this.cfg.allowAmbiguousMostRecent) {
            const newest = [...liveRegs].sort(
              (a, b) => connectedAtMs(b.reg.connectedAt) - connectedAtMs(a.reg.connectedAt),
            )[0];
            return this.cache(newest.reg.port, newest.status, newest.reg, broker.port, epoch);
          }
          return this.ambiguous(liveRegs.map((r) => r.reg));
        }
        // 0 live → fall through to scan.
      }
    }

    // 2) Fast parallel scan fallback.
    const ports = candidatePorts(this.cfg.agentPort);
    if (this.wantAndroid()) await this.forwarder()?.ensureForwards(ports);
    let live = await this.scan(ports);
    if (!live.length && !this.wantAndroid() && this.cfg.adbFallbackScan) {
      const fwd = this.forwarder();
      if (fwd && (await fwd.hasDevice())) {
        await fwd.ensureForwards(ports);
        live = await this.scan(ports);
      }
    }
    const liveOutcome = selectLive(live, selOpts);
    if (liveOutcome.kind === "ambiguous") {
      return this.ambiguous(liveOutcome.candidates.map(liveToRegistration));
    }
    if (liveOutcome.kind === "unique") {
      const a = liveOutcome.agent;
      return this.cache(a.port, a.status, liveToRegistration(a), broker?.port, epoch);
    }

    // 3) Nothing found — no CLI last resort; return an actionable error.
    if (!broker) {
      return err({
        kind: "broker-not-found",
        message:
          "DevFlow broker not found (~/.mauidevflow/broker.json missing or unreachable) and no agent answered a port scan. Start a MAUI app with the DevFlow agent, or run `maui devflow list` (or set bootstrapBroker).",
        operation: "resolve",
        retriable: true,
      });
    }
    return err({
      kind: "no-agents",
      message: "No running DevFlow agent found via the broker registry or a port scan.",
      operation: "resolve",
      retriable: true,
    });
  }

  private ambiguous(candidates: AgentRegistration[]): DevFlowResult<ResolvedConnection> {
    const list = candidates
      .map((c) => `${c.appName || "?"} (${c.platform || "?"} @ ${c.port})`)
      .join(", ");
    return err({
      kind: "agent-ambiguous",
      message: `Multiple DevFlow agents match. Disambiguate with agentPort, platform, or projectRoot (or set allowAmbiguousMostRecent). Candidates: ${list}.`,
      operation: "resolve",
      candidates,
      retriable: false,
    });
  }

  private cache(
    port: number,
    status: AgentStatus,
    reg: AgentRegistration | undefined,
    brokerPort: number | undefined,
    epoch: number,
  ): DevFlowResult<ResolvedConnection> {
    const target: AgentTarget = {
      port,
      platform: reg?.platform ?? status.device?.platform,
      appName: reg?.appName ?? status.app?.name,
      registration: reg,
    };
    const conn: ResolvedConnection = { port, target, status, brokerPort };
    // Only publish to the shared cache if this resolution is still the current one
    // (a newer forced resolve, invalidate, retarget, or dispose bumps the epoch).
    if (epoch === this.epoch) this.cached = conn;
    return ok(conn, target);
  }

  private async probeRegistrations(
    regs: AgentRegistration[],
  ): Promise<Array<{ reg: AgentRegistration; status: AgentStatus }>> {
    const results = await Promise.all(
      regs.map(async (reg) => {
        const status = await probeStatus(reg.port, this.cfg.probeTimeoutMs);
        return status ? { reg, status } : null;
      }),
    );
    return results.filter(
      (x): x is { reg: AgentRegistration; status: AgentStatus } => x !== null,
    );
  }

  private async scan(ports: number[]): Promise<LiveAgent[]> {
    const results = await Promise.all(
      ports.map(async (port): Promise<LiveAgent | null> => {
        const status = await probeStatus(port, this.cfg.probeTimeoutMs);
        if (!status) return null;
        return {
          port,
          status,
          running: status.running === true,
          platform: status.device?.platform,
          appName: status.app?.name,
        };
      }),
    );
    return results.filter((x): x is LiveAgent => x !== null);
  }
}

function liveToRegistration(a: LiveAgent): AgentRegistration {
  return {
    id: "",
    project: "",
    tfm: "",
    platform: a.platform ?? "",
    appName: a.appName ?? "",
    port: a.port,
  };
}
